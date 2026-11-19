namespace BookChat.Core.Models;

/// <summary>
/// A file offered to us via a CTCP DCC SEND request.
/// </summary>
public class FileOffer
{
    public string FileName { get; set; } = string.Empty;
    public string SenderNickname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset OfferTime { get; set; }

    public string FileSizeFormatted => FormatBytes(FileSize);

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public override string ToString() => $"{FileName} ({FileSizeFormatted}) from {SenderNickname}";
}
