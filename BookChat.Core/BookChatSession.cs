using System.Collections.Concurrent;
using BookChat.Core.Irc;
using BookChat.Core.Models;
using BookChat.Core.Services.Interfaces;

namespace BookChat.Core;

/// <summary>
/// The "IRC/DCC core" for BookChat: one IRC identity, a job queue for in-flight
/// searches/downloads, and the glue between them. This is the piece neither the WPF app nor
/// BookChatAndroid factored out on their own (see WEB_PORT_PLAN.md §1.3) — both hand-rolled the
/// same connect/search/DCC-receive/rank sequence independently. BookChat.Web's hosted service
/// owns exactly one instance of this for the whole household.
///
/// Unlike either existing app, jobs are correlated by a stable <see cref="DownloadJob.Id"/>
/// (matched FIFO per kind against incoming DCC offers) rather than a single "current" reference —
/// necessary once more than one browser tab/person can trigger searches/downloads concurrently
/// against the one shared IRC identity.
/// </summary>
public class BookChatSession : IAsyncDisposable
{
    private readonly IDccReceiver _dccReceiver;
    private readonly ISearchResultParser _searchResultParser;
    private readonly ICalibreImportService _calibreImportService;
    private readonly string _downloadDirectory;

    private IrcCoreClient? _ircClient;
    private IrcSettings _ircSettings = new();
    private CalibreSettings _calibreSettings = new();

    private readonly ConcurrentQueue<DownloadJob> _jobs = new();
    private readonly object _jobsLock = new();

    public event Action<string>? MessageLogged;
    public event Action<IrcStateChange>? ConnectionStateChanged;
    public event Action? JobsChanged;
    public event Action<DownloadJob, List<SearchResult>>? SearchResultsReady;

    public IrcConnectionState ConnectionState => _ircClient?.State ?? IrcConnectionState.Disconnected;

    /// <summary>
    /// Last time a search/download was requested or a job's state changed — what the idle-
    /// disconnect policy (see <c>IrcHostedService.RunIdleMonitorAsync</c>) measures against.
    /// </summary>
    public DateTimeOffset LastActivityUtc { get; private set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<DownloadJob> Jobs
    {
        get { lock (_jobsLock) { return _jobs.ToList(); } }
    }

    /// <summary>True while any job is still in flight — the idle monitor never disconnects while this holds.</summary>
    public bool HasActiveJobs => Jobs.Any(j => j.State is DownloadJobState.Queued or DownloadJobState.RequestSent
        or DownloadJobState.OfferReceived or DownloadJobState.Downloading or DownloadJobState.Importing);

    public BookChatSession(
        IDccReceiver dccReceiver,
        ISearchResultParser searchResultParser,
        ICalibreImportService calibreImportService,
        string downloadDirectory)
    {
        _dccReceiver = dccReceiver;
        _searchResultParser = searchResultParser;
        _calibreImportService = calibreImportService;
        _downloadDirectory = downloadDirectory;
        Directory.CreateDirectory(_downloadDirectory);
    }

    public void Configure(IrcSettings ircSettings, CalibreSettings calibreSettings)
    {
        _ircSettings = ircSettings;
        _calibreSettings = calibreSettings;
    }

    public void Connect()
    {
        _ircClient?.StopAsync(); // fire-and-forget teardown of any previous client

        var client = new IrcCoreClient(_ircSettings);
        client.StateChanged += state =>
        {
            ConnectionStateChanged?.Invoke(state);
            Log(state.State switch
            {
                IrcConnectionState.Connecting when state.Attempt == 0 => $"Connecting to {_ircSettings.Server}:{_ircSettings.Port}...",
                IrcConnectionState.Connecting => $"Reconnecting (attempt {state.Attempt}): {state.LastError}",
                IrcConnectionState.Connected => $"Connected and joined {_ircSettings.Channel}",
                IrcConnectionState.Disconnected => "Disconnected",
                _ => "Unknown state"
            });
        };
        client.RawLineReceived += OnRawLine;

        _ircClient = client;
        client.Start();
    }

    public async Task DisconnectAsync()
    {
        if (_ircClient != null)
            await _ircClient.StopAsync();
    }

    /// <summary>
    /// Connects if not already connected (or joins an in-progress connect) and waits up to
    /// <paramref name="timeout"/> for it to reach <see cref="IrcConnectionState.Connected"/>.
    /// This is what makes the connection "on demand": nothing calls <see cref="Connect"/> at
    /// startup any more (see <c>IrcHostedService</c>) — the first search or download is what
    /// brings the connection up, and the idle monitor is what takes it back down. Respectful-use
    /// motivation (not just resource use): an idle connection that's present for days is one
    /// thing on IRCHighway's #ebooks, but a connection that's only up while actually in use is
    /// strictly less to object to.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        LastActivityUtc = DateTimeOffset.UtcNow;

        if (ConnectionState == IrcConnectionState.Connected)
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(IrcStateChange change)
        {
            if (change.State == IrcConnectionState.Connected)
                tcs.TrySetResult(true);
        }

        ConnectionStateChanged += Handler;
        try
        {
            if (ConnectionState == IrcConnectionState.Disconnected)
                Connect();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var registration = cts.Token.Register(() => tcs.TrySetResult(false));
            return await tcs.Task;
        }
        finally
        {
            ConnectionStateChanged -= Handler;
        }
    }

    public async Task<DownloadJob> SearchAsync(string author, string title)
    {
        if (!await EnsureConnectedAsync(TimeSpan.FromSeconds(15)))
            throw new InvalidOperationException("Could not connect to IRC — try again in a moment.");

        var query = $"@search {author} {title}".Trim();
        var job = new DownloadJob { Kind = "search", Query = query };
        EnqueueJob(job);

        await RequireClient().SendMessageAsync(_ircSettings.Channel, query);
        job.State = DownloadJobState.RequestSent;
        Log($"[{_ircSettings.Nickname}] {query}");
        NotifyJobsChanged();
        return job;
    }

    public async Task<DownloadJob> DownloadAsync(SearchResult result)
    {
        if (!await EnsureConnectedAsync(TimeSpan.FromSeconds(15)))
            throw new InvalidOperationException("Could not connect to IRC — try again in a moment.");

        var job = new DownloadJob { Kind = "download", Query = result.DownloadCommand, SearchResult = result };
        EnqueueJob(job);

        await RequireClient().SendMessageAsync(_ircSettings.Channel, result.DownloadCommand);
        job.State = DownloadJobState.RequestSent;
        Log($"[{_ircSettings.Nickname}] {result.DownloadCommand}");
        NotifyJobsChanged();
        return job;
    }

    private IrcCoreClient RequireClient() =>
        _ircClient ?? throw new InvalidOperationException("Not connected.");

    private void EnqueueJob(DownloadJob job)
    {
        _jobs.Enqueue(job);
        NotifyJobsChanged();
    }

    private void NotifyJobsChanged()
    {
        LastActivityUtc = DateTimeOffset.UtcNow;
        JobsChanged?.Invoke();
    }

    private void OnRawLine(string rawLine)
    {
        if (!rawLine.Contains("DCC SEND", StringComparison.OrdinalIgnoreCase))
            return;

        var offer = DccParser.TryParse(rawLine);
        if (offer == null)
            return;

        var isSearchResults = offer.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                               offer.SenderNickname.Contains("Search", StringComparison.OrdinalIgnoreCase);

        var kind = isSearchResults ? "search" : "download";
        var job = _jobs.FirstOrDefault(j => j.Kind == kind && j.State == DownloadJobState.RequestSent);
        if (job == null)
        {
            Log($"*** Unmatched DCC offer: {offer} (no pending {kind} job)");
            return;
        }

        job.Offer = offer;
        job.State = DownloadJobState.OfferReceived;
        NotifyJobsChanged();
        Log($"*** FILE OFFER: {offer}");

        _ = isSearchResults ? ReceiveSearchResultsAsync(job, offer) : ReceiveBookAsync(job, offer);
    }

    private async Task ReceiveSearchResultsAsync(DownloadJob job, FileOffer offer)
    {
        try
        {
            job.State = DownloadJobState.Downloading;
            NotifyJobsChanged();

            using var buffer = new MemoryStream();
            await _dccReceiver.ReceiveAsync(offer, buffer, progress: null);
            buffer.Position = 0;

            var results = await _searchResultParser.ParseSearchResultsFromZipAsync(buffer);
            var (author, title) = SplitSearchQuery(job.Query);
            results = _searchResultParser.RankResults(results, author, title);

            job.State = DownloadJobState.Done;
            job.CompletedAt = DateTimeOffset.UtcNow;
            SearchResultsReady?.Invoke(job, results);
            Log($"*** Search complete: {results.Count} results");
        }
        catch (Exception ex)
        {
            job.State = DownloadJobState.Failed;
            job.ErrorMessage = ex.Message;
            Log($"*** ERROR receiving search results: {ex.Message}");
        }
        finally
        {
            NotifyJobsChanged();
        }
    }

    private async Task ReceiveBookAsync(DownloadJob job, FileOffer offer)
    {
        try
        {
            job.State = DownloadJobState.Downloading;
            NotifyJobsChanged();

            var localPath = GetUniqueFilePath(Path.Combine(_downloadDirectory, BuildDestinationFileName(job, offer)));
            job.LocalFilePath = localPath;

            var progress = new Progress<long>(bytes =>
            {
                job.BytesDownloaded = bytes;
                NotifyJobsChanged();
            });

            await using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                await _dccReceiver.ReceiveAsync(offer, fileStream, progress);
            }

            if (_calibreSettings.AutoImport)
            {
                job.State = DownloadJobState.Importing;
                NotifyJobsChanged();
                var imported = await _calibreImportService.AddBookToLibraryAsync(localPath, _calibreSettings);
                if (!imported)
                    Log($"*** Calibre import failed: {_calibreImportService.LastError}");
            }

            job.State = DownloadJobState.Done;
            job.CompletedAt = DateTimeOffset.UtcNow;
            Log($"*** Download complete: {offer.FileName}");
        }
        catch (Exception ex)
        {
            job.State = DownloadJobState.Failed;
            job.ErrorMessage = ex.Message;
            Log($"*** ERROR downloading {offer.FileName}: {ex.Message}");
        }
        finally
        {
            NotifyJobsChanged();
        }
    }

    /// <summary>
    /// The destination directory on this deployment is Calibre's own "Automatically add to
    /// library" watch folder, not a generic drop folder — and that AutoAdder silently skips
    /// reprocessing any exact filename it has seen before, even across restarts and even if an
    /// earlier attempt under that name failed (see the AutoAdder gotcha in KassandraWiki's
    /// calibre.md). A same-named book re-downloaded later (retry, or just grabbing it again)
    /// would otherwise vanish with no error and no import. So every attempt gets a short,
    /// per-job uniqueifier baked into the filename — cheap to do since Calibre reads the book's
    /// *embedded* metadata for its library entry, not this on-disk filename, which it deletes
    /// once consumed anyway.
    /// </summary>
    private static string BuildDestinationFileName(DownloadJob job, FileOffer offer)
    {
        var safeName = Path.GetFileName(offer.FileName); // defensive: strip any path components
        var ext = Path.GetExtension(safeName);
        var nameOnly = Path.GetFileNameWithoutExtension(safeName);
        var uniqueSuffix = job.Id.ToString("N")[..8];
        return $"{nameOnly} [{uniqueSuffix}]{ext}";
    }

    private static (string author, string title) SplitSearchQuery(string query)
    {
        var trimmed = query.Replace("@search", "", StringComparison.OrdinalIgnoreCase).Trim();
        return (trimmed, trimmed);
    }

    private static string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var nameOnly = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var count = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{nameOnly} ({count}){extension}");
            count++;
        } while (File.Exists(candidate));

        return candidate;
    }

    private void Log(string message) => MessageLogged?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        if (_ircClient != null)
            await _ircClient.DisposeAsync();
    }
}
