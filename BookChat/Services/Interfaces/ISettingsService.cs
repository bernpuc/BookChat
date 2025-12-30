using System.Threading.Tasks;
using BookChat.Models;

namespace BookChat.Services.Interfaces
{
    /// <summary>
    /// Service for managing application settings persistence
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Loads Calibre settings from storage
        /// </summary>
        Task<CalibreSettings?> LoadCalibreSettingsAsync();

        /// <summary>
        /// Saves Calibre settings to storage
        /// </summary>
        Task SaveCalibreSettingsAsync(CalibreSettings settings);
    }
}
