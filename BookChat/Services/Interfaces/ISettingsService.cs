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
        /// Loads application settings from storage
        /// </summary>
        Task<AppSettings?> LoadSettingsAsync();

        /// <summary>
        /// Saves application settings to storage
        /// </summary>
        Task SaveSettingsAsync(AppSettings settings);
    }
}
