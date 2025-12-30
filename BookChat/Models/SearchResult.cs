using System;

namespace BookChat.Models
{
    /// <summary>
    /// Represents a single book search result from SearchBot
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

        /// <summary>
        /// Gets the command to send to download this book
        /// </summary>
        public string DownloadCommand => $"!{BotName} {FileHash}";

        /// <summary>
        /// Gets a display-friendly description of the book
        /// </summary>
        public string DisplayText => $"{Author} - {Title} ({Format}) {FileSize}";

        /// <summary>
        /// Gets a relevance score for ranking (higher is better)
        /// </summary>
        public int RelevanceScore { get; set; }

        /// <summary>
        /// Gets the file size in bytes for proper sorting
        /// </summary>
        public long FileSizeInBytes
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FileSize))
                    return 0;

                // Parse size like "2.7MB", "481.58 KB", "1.16MB", "23.59MB", etc.
                var match = System.Text.RegularExpressions.Regex.Match(FileSize, @"(\d+\.?\d*)\s*(KB|MB|GB|KiB|MiB|GiB)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    double value = double.Parse(match.Groups[1].Value);
                    string unit = match.Groups[2].Value.ToUpper();

                    // Convert to bytes
                    return unit switch
                    {
                        "KB" => (long)(value * 1024),
                        "KIB" => (long)(value * 1024),
                        "MB" => (long)(value * 1024 * 1024),
                        "MIB" => (long)(value * 1024 * 1024),
                        "GB" => (long)(value * 1024 * 1024 * 1024),
                        "GIB" => (long)(value * 1024 * 1024 * 1024),
                        _ => 0
                    };
                }

                return 0;
            }
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}
