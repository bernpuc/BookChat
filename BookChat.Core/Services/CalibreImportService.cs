using System.Diagnostics;
using BookChat.Core.Models;
using BookChat.Core.Services.Interfaces;

namespace BookChat.Core.Services;

/// <summary>
/// Shells out to the `calibredb` CLI, same approach as the WPF app's CalibreService — this works
/// identically from a Linux container calling the containerized Calibre's calibredb, provided the
/// `calibredb` binary is on PATH in the BookChat.Web image (see WEB_PORT_PLAN.md §4.4). On
/// kassandra the primary path is just dropping the file into Calibre's watched intake folder, so
/// this calibredb path is a secondary/explicit "import now" option, not the only route in.
/// </summary>
public class CalibreImportService : ICalibreImportService
{
    public string LastError { get; private set; } = string.Empty;

    public async Task<bool> AddBookToLibraryAsync(string bookFilePath, CalibreSettings settings)
    {
        LastError = string.Empty;

        if (!File.Exists(bookFilePath))
        {
            LastError = $"Book file not found: {bookFilePath}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.LibraryPath) || !Directory.Exists(settings.LibraryPath))
        {
            LastError = $"Calibre library directory not found: {settings.LibraryPath}";
            return false;
        }

        var duplicateHandling = new[] { "ignore", "overwrite", "new_record" }.Contains(settings.DuplicateHandling)
            ? settings.DuplicateHandling
            : "ignore";

        var arguments = $"add \"{bookFilePath}\" --library-path \"{settings.LibraryPath}\" --duplicates {duplicateHandling}";
        var result = await ExecuteCalibredbAsync(arguments);

        if (!result.Success && IsLibraryLockedError(result.ErrorOutput))
        {
            arguments = !string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password)
                ? $"add \"{bookFilePath}\" --with-library {settings.ContentServerUrl} --username {settings.Username} --password {settings.Password} --duplicates {duplicateHandling}"
                : $"add \"{bookFilePath}\" --with-library {settings.ContentServerUrl} --duplicates {duplicateHandling}";

            result = await ExecuteCalibredbAsync(arguments);
            if (!result.Success)
            {
                LastError = $"Calibre is running but content server connection failed: {result.ErrorOutput}";
                return false;
            }

            return true;
        }

        if (!result.Success)
        {
            LastError = result.ErrorOutput;
            return false;
        }

        return true;
    }

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

    private static bool IsLibraryLockedError(string errorOutput) =>
        !string.IsNullOrWhiteSpace(errorOutput) && (
            errorOutput.Contains("another program", StringComparison.OrdinalIgnoreCase) ||
            errorOutput.Contains("calibre-server", StringComparison.OrdinalIgnoreCase) ||
            errorOutput.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
            errorOutput.Contains("currently running", StringComparison.OrdinalIgnoreCase));

    private static async Task<CalibredbResult> ExecuteCalibredbAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
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
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            return new CalibredbResult(
                process.ExitCode == 0,
                string.IsNullOrWhiteSpace(error) ? output : error);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new CalibredbResult(false, "calibredb not found. Ensure Calibre is installed and calibredb is on PATH.");
        }
        catch (Exception ex)
        {
            return new CalibredbResult(false, $"Failed to execute calibredb: {ex.Message}");
        }
    }

    private record CalibredbResult(bool Success, string ErrorOutput);
}
