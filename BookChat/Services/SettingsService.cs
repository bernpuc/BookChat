using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BookChat.Models;
using BookChat.Services.Interfaces;

namespace BookChat.Services
{
    /// <summary>
    /// Service for managing application settings persistence using JSON files
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly string _settingsDirectory;
        private readonly string _calibreSettingsFile;

        public SettingsService()
        {
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BookChat");

            _calibreSettingsFile = Path.Combine(_settingsDirectory, "calibre-settings.json");

            // Ensure settings directory exists
            Directory.CreateDirectory(_settingsDirectory);
        }

        /// <summary>
        /// Loads Calibre settings from JSON file
        /// </summary>
        public async Task<CalibreSettings?> LoadCalibreSettingsAsync()
        {
            try
            {
                if (!File.Exists(_calibreSettingsFile))
                    return null;

                var json = await File.ReadAllTextAsync(_calibreSettingsFile);
                return JsonSerializer.Deserialize<CalibreSettings>(json);
            }
            catch (Exception)
            {
                // If load fails, return null (will use defaults)
                return null;
            }
        }

        /// <summary>
        /// Saves Calibre settings to JSON file
        /// </summary>
        public async Task SaveCalibreSettingsAsync(CalibreSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(_calibreSettingsFile, json);
            }
            catch (Exception)
            {
                // Silently fail - settings won't be persisted but app continues
            }
        }
    }
}
