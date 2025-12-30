using System;

namespace BookChat.Models
{
    /// <summary>
    /// Represents a file offered via DCC SEND
    /// </summary>
    public class FileOffer
    {
        public string FileName { get; set; } = string.Empty;
        public string SenderNickname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public long FileSize { get; set; }
        public DateTime OfferTime { get; set; }
        public bool IsAccepted { get; set; }
        public string? LocalFilePath { get; set; }

        /// <summary>
        /// Gets the file size in a human-readable format
        /// </summary>
        public string FileSizeFormatted
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = FileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        public override string ToString()
        {
            return $"{FileName} ({FileSizeFormatted}) from {SenderNickname}";
        }
    }
}
