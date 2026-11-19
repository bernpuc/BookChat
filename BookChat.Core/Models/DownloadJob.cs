namespace BookChat.Core.Models;

/// <summary>
/// State machine for one search or download request.
///
/// A stable <see cref="Id"/> is the correlation key between the IRC request we sent
/// (search query / "!bot hash" download command) and the DCC offer that eventually
/// arrives for it — neither the WPF app nor the Android app had this (both correlate
/// loosely, by a single "current" reference or a sender-nick heuristic), which only
/// matters once more than one job can be in flight against the shared IRC identity at once.
/// </summary>
public class DownloadJob
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Kind { get; init; } // "search" or "download"
    public required string Query { get; init; } // search terms, or the SearchResult.DownloadCommand
    public SearchResult? SearchResult { get; init; }
    public DownloadJobState State { get; set; } = DownloadJobState.Queued;
    public FileOffer? Offer { get; set; }
    public long BytesDownloaded { get; set; }
    public string? LocalFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public double PercentComplete =>
        Offer is { FileSize: > 0 } ? (double)BytesDownloaded / Offer.FileSize * 100 : 0;
}

public enum DownloadJobState
{
    Queued,
    RequestSent,
    OfferReceived,
    Downloading,
    Importing,
    Done,
    Failed,
    Cancelled
}
