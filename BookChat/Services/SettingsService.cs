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
        private readonly string _settingsFile;

        public SettingsService()
        {
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BookChat");

            _settingsFile = Path.Combine(_settingsDirectory, "app-settings.json");

            // Ensure settings directory exists
            Directory.CreateDirectory(_settingsDirectory);
        }

        /// <summary>
        /// Loads application settings from JSON file
        /// </summary>
        public async Task<AppSettings?> LoadSettingsAsync()
        {
            try
            {
                if (!File.Exists(_settingsFile))
                    return null;

                var json = await File.ReadAllTextAsync(_settingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json);
            }
            catch (Exception)
            {
                // If load fails, return null (will use defaults)
                return null;
            }
        }

        /// <summary>
        /// Saves application settings to JSON file
        /// </summary>
        public async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(_settingsFile, json);
            }
            catch (Exception)
            {
                // Silently fail - settings won't be persisted but app continues
            }
        }
    }
}
