using System.Threading.Tasks;

namespace BookChat.Services.Interfaces
{
    /// <summary>
    /// Service for integrating with Calibre e-book library management
    /// </summary>
    public interface ICalibreService
    {
        /// <summary>
        /// Adds a book to the Calibre library using calibredb command-line tool
        /// </summary>
        /// <param name="bookFilePath">Full path to the e-book file</param>
        /// <param name="libraryPath">Path to the Calibre library directory</param>
        /// <param name="duplicateHandling">How to handle duplicates: "ignore", "overwrite", or "new_record"</param>
        /// <param name="contentServerUrl">URL of Calibre content server (e.g., http://localhost:8080) for when Calibre is running</param>
        /// <param name="username">Username for content server authentication</param>
        /// <param name="password">Password for content server authentication</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> AddBookToLibraryAsync(string bookFilePath, string libraryPath, string duplicateHandling = "ignore", string contentServerUrl = "http://localhost:8080", string username = "", string password = "");

        /// <summary>
        /// Checks if calibredb is available on the system
        /// </summary>
        /// <returns>True if calibredb is found, false otherwise</returns>
        Task<bool> IsCalibreInstalledAsync();

        /// <summary>
        /// Gets the last error message from the most recent operation
        /// </summary>
        string LastError { get; }
    }
}
