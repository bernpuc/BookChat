using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookChat.Services.Interfaces;

namespace BookChat.Services
{
    /// <summary>
    /// Service for integrating with Calibre e-book library management using calibredb CLI
    /// </summary>
    public class CalibreService : ICalibreService
    {
        private string _lastError = string.Empty;

        public string LastError => _lastError;

        /// <summary>
        /// Adds a book to the Calibre library using calibredb command-line tool
        /// </summary>
        public async Task<bool> AddBookToLibraryAsync(string bookFilePath, string libraryPath, string duplicateHandling = "ignore", string contentServerUrl = "http://localhost:8080", string username = "", string password = "")
        {
            _lastError = string.Empty;

            // Validate inputs
            if (string.IsNullOrWhiteSpace(bookFilePath))
            {
                _lastError = "Book file path is required";
                return false;
            }

            if (!File.Exists(bookFilePath))
            {
                _lastError = $"Book file not found: {bookFilePath}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                _lastError = "Calibre library path is required";
                return false;
            }

            if (!Directory.Exists(libraryPath))
            {
                _lastError = $"Calibre library directory not found: {libraryPath}";
                return false;
            }

            // Validate duplicate handling option
            if (!new[] { "ignore", "overwrite", "new_record" }.Contains(duplicateHandling))
            {
                duplicateHandling = "ignore";
            }

            try
            {
                // Try with --library-path first (direct database access)
                var arguments = $"add \"{bookFilePath}\" --library-path \"{libraryPath}\" --duplicates {duplicateHandling}";
                var result = await ExecuteCalibredbAsync(arguments);

                // Check if failed due to Calibre being open
                if (!result.Success && IsLibraryLockedError(result.ErrorOutput))
                {
                    // Retry with --with-library using content server URL with authentication
                    // Format: http://localhost:8080 (or http://localhost:8080/#library_name for specific library)
                    if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                    {
                        arguments = $"add \"{bookFilePath}\" --with-library {contentServerUrl} --username {username} --password {password} --duplicates {duplicateHandling}";
                    }
                    else
                    {
                        // Try without credentials if not provided
                        arguments = $"add \"{bookFilePath}\" --with-library {contentServerUrl} --duplicates {duplicateHandling}";
                    }

                    result = await ExecuteCalibredbAsync(arguments);

                    if (!result.Success)
                    {
                        _lastError = $"Calibre is running but content server connection failed. " +
                                   $"Make sure content server is running and credentials are correct. " +
                                   $"Error: {result.ErrorOutput}";
                        return false;
                    }

                    return true;
                }

                if (!result.Success)
                {
                    _lastError = result.ErrorOutput;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _lastError = $"Error adding book to Calibre: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Checks if the error message indicates the library database is locked
        /// </summary>
        private bool IsLibraryLockedError(string errorOutput)
        {
            if (string.IsNullOrWhiteSpace(errorOutput))
                return false;

            return errorOutput.Contains("another program", StringComparison.OrdinalIgnoreCase) ||
                   errorOutput.Contains("calibre-server", StringComparison.OrdinalIgnoreCase) ||
                   errorOutput.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
                   errorOutput.Contains("currently running", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if calibredb is available on the system
        /// </summary>
        public async Task<bool> IsCalibreInstalledAsync()
        {
            try
            {
                var result = await ExecuteCalibredbAsync("--version");
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Executes calibredb command and returns the result
        /// </summary>
        private async Task<CalibredbResult> ExecuteCalibredbAsync(string arguments)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "calibredb",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                return new CalibredbResult
                {
                    Success = process.ExitCode == 0,
                    Output = output,
                    ErrorOutput = string.IsNullOrWhiteSpace(error) ? output : error,
                    ExitCode = process.ExitCode
                };
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // calibredb not found in PATH
                return new CalibredbResult
                {
                    Success = false,
                    ErrorOutput = "calibredb not found. Please ensure Calibre is installed and calibredb is in your system PATH.",
                    ExitCode = -1
                };
            }
            catch (Exception ex)
            {
                return new CalibredbResult
                {
                    Success = false,
                    ErrorOutput = $"Failed to execute calibredb: {ex.Message}",
                    ExitCode = -1
                };
            }
        }

        /// <summary>
        /// Result from calibredb command execution
        /// </summary>
        private class CalibredbResult
        {
            public bool Success { get; set; }
            public string Output { get; set; } = string.Empty;
            public string ErrorOutput { get; set; } = string.Empty;
            public int ExitCode { get; set; }
        }
    }
}
