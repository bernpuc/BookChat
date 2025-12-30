using System;

namespace BookChat.Models
{
    /// <summary>
    /// Represents the progress of a file download
    /// </summary>
    public class DownloadProgress
    {
        public FileOffer FileOffer { get; set; } = null!;
        public long BytesDownloaded { get; set; }
        public DownloadStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets the download progress percentage (0-100)
        /// </summary>
        public double PercentComplete
        {
            get
            {
                if (FileOffer.FileSize == 0) return 0;
                return (double)BytesDownloaded / FileOffer.FileSize * 100;
            }
        }

        /// <summary>
        /// Gets the elapsed time for the download
        /// </summary>
        public TimeSpan ElapsedTime
        {
            get
            {
                var endTime = EndTime ?? DateTime.Now;
                return endTime - StartTime;
            }
        }

        /// <summary>
        /// Gets the download speed in bytes per second
        /// </summary>
        public double BytesPerSecond
        {
            get
            {
                var elapsed = ElapsedTime.TotalSeconds;
                if (elapsed <= 0) return 0;
                return BytesDownloaded / elapsed;
            }
        }

        /// <summary>
        /// Gets the download speed in a human-readable format
        /// </summary>
        public string SpeedFormatted
        {
            get
            {
                string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
                double speed = BytesPerSecond;
                int order = 0;
                while (speed >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    speed = speed / 1024;
                }
                return $"{speed:0.##} {sizes[order]}";
            }
        }
    }

    /// <summary>
    /// Download status enumeration
    /// </summary>
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Completed,
        Failed,
        Cancelled
    }
}
