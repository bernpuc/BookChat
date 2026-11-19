using System.Text.RegularExpressions;

namespace BookChat.Core.Models;

/// <summary>
/// A single book search result parsed from a SearchBot results zip.
/// Example: !Ashurbanipal mM43v4QB0mLu6KmIh4zXhQ - Isaac Asimov - Foundation [eng] - Foundation (EPUB) 247.7 KB - [Science Fiction]
/// </summary>
public class SearchResult
{
    public string BotName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string RawLine { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int RelevanceScore { get; set; }

    public string DownloadCommand => $"!{BotName} {FileHash}";
    public string DisplayText => $"{Author} - {Title} ({Format}) {FileSize}";

    public long FileSizeInBytes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileSize))
                return 0;

            var match = Regex.Match(FileSize, @"(\d+\.?\d*)\s*(KB|MB|GB|KiB|MiB|GiB)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0;

            var value = double.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value.ToUpperInvariant();
            return unit switch
            {
                "KB" or "KIB" => (long)(value * 1024),
                "MB" or "MIB" => (long)(value * 1024 * 1024),
                "GB" or "GIB" => (long)(value * 1024 * 1024 * 1024),
                _ => 0
            };
        }
    }

    public override string ToString() => DisplayText;
}
