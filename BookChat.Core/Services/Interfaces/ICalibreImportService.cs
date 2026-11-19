using BookChat.Core.Models;

namespace BookChat.Core.Services.Interfaces;

public interface ICalibreImportService
{
    Task<bool> AddBookToLibraryAsync(string bookFilePath, CalibreSettings settings);
    Task<bool> IsCalibreInstalledAsync();
    string LastError { get; }
}
