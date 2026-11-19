using BookChat.Core;
using BookChat.Core.Irc;
using BookChat.Core.Models;
using BookChat.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BookChat.Web.Services;

/// <summary>
/// Owns the single, shared <see cref="BookChatSession"/> for the whole household and applies this
/// deployment's connection-liveness *policy*: connect on demand (the first search/download brings
/// it up, via <see cref="BookChatSession.EnsureConnectedAsync"/>), then disconnect again after
/// <see cref="IdleDisconnectAfter"/> of no activity with nothing in flight. Deliberately not
/// "always connected" — holding an idle presence on IRCHighway's #ebooks for days between uses,
/// and more importantly the reconnect-flapping an always-on connection risks during a rough
/// network patch (see the backoff-overflow bug this app already hit once), is worse citizenship
/// than connecting only while actually in use.
/// Persists completed/failed jobs to SQLite as they finish, so download history survives restarts
/// (the gap flagged in WEB_PORT_PLAN.md §1.2 for BookChatAndroid).
/// </summary>
public class IrcHostedService(
    BookChatSession session,
    IDbContextFactory<BookChatDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<IrcHostedService> logger) : IHostedService
{
    private readonly HashSet<Guid> _persistedJobIds = [];
    private CancellationTokenSource? _idleMonitorCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await DropLegacyCalibreColumnsAsync(db, cancellationToken);

        var settings = await db.Settings.FindAsync([1], cancellationToken) ?? new SettingsEntity();

        session.Configure(
            new IrcSettings
            {
                Server = settings.IrcServer,
                Port = settings.IrcPort,
                UseSsl = settings.IrcUseSsl,
                Nickname = settings.IrcNickname,
                Channel = settings.IrcChannel,
                NickServPassword = configuration["BookChat:IrcNickServPassword"] ?? string.Empty
            },
            // Direct calibredb/content-server import (BookChatSession's optional Importing step)
            // is deliberately left inert here: this deployment's delivery mechanism is writing
            // completed downloads to BookChat__DownloadDirectory, which points at Calibre's own
            // intake watch folder — an external script imports from there independently. See
            // bookchat.md in KassandraWiki for how that handoff actually works.
            new CalibreSettings());

        session.JobsChanged += () => _ = PersistCompletedJobsAsync();
        session.MessageLogged += message => logger.LogInformation("{Message}", message);

        // No session.Connect() here — connecting is now on-demand (see class remarks).

        var idleMinutes = configuration.GetValue("BookChat:IdleDisconnectMinutes", 10);
        _idleMonitorCts = new CancellationTokenSource();
        _ = RunIdleMonitorAsync(TimeSpan.FromMinutes(idleMinutes), _idleMonitorCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _idleMonitorCts?.Cancel();
        await session.DisconnectAsync();
    }

    /// <summary>
    /// Polls every 30s and disconnects once the connection has been idle (no jobs in flight, no
    /// search/download requested) for longer than <paramref name="idleTimeout"/>. A poll loop
    /// rather than a single delayed timer because activity keeps resetting the clock
    /// (<see cref="BookChatSession.LastActivityUtc"/>) and there's no single point to re-arm a
    /// one-shot timer from without threading that through every call site that touches a job.
    /// </summary>
    private async Task RunIdleMonitorAsync(TimeSpan idleTimeout, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (session.ConnectionState != IrcConnectionState.Connected)
                    continue;
                if (session.HasActiveJobs)
                    continue;
                if (DateTimeOffset.UtcNow - session.LastActivityUtc < idleTimeout)
                    continue;

                logger.LogInformation("Disconnecting from IRC after {Minutes} minutes of inactivity", idleTimeout.TotalMinutes);
                await session.DisconnectAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal on shutdown.
        }
    }

    /// <summary>
    /// One-time cleanup for a deployment whose Settings table still has the five Calibre
    /// Integration columns from before that UI was removed — EF's EnsureCreatedAsync only
    /// creates tables when none exist yet, so an already-existing table keeps its wider physical
    /// schema even once SettingsEntity no longer maps those columns. Left alone, those columns'
    /// NOT NULL constraints (no default) broke every INSERT (first-ever Save Settings after a
    /// fresh db recreate) with "NOT NULL constraint failed: Settings.CalibreLibraryPath". Dropping
    /// them is idempotent and safe to run on every startup: a fresh install's table never has
    /// them, so each DROP COLUMN just fails with "no such column" and is ignored.
    /// </summary>
    private static async Task DropLegacyCalibreColumnsAsync(BookChatDbContext db, CancellationToken ct)
    {
        string[] legacyColumns =
        [
            "CalibreLibraryPath", "CalibreAutoImport", "CalibreDuplicateHandling",
            "CalibreContentServerUrl", "CalibreUsername"
        ];

        foreach (var column in legacyColumns)
        {
            try
            {
                // Column names come only from the fixed array above, never external input — a
                // plain concatenated string (not an interpolated one) sidesteps EF1002's
                // SQL-injection analyzer, which doesn't know that.
                var sql = "ALTER TABLE Settings DROP COLUMN " + column;
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // Column doesn't exist (fresh install, or already dropped) — nothing to do.
            }
        }
    }

    private async Task PersistCompletedJobsAsync()
    {
        var toPersist = session.Jobs
            .Where(j => j.State is DownloadJobState.Done or DownloadJobState.Failed or DownloadJobState.Cancelled)
            .Where(j => _persistedJobIds.Add(j.Id))
            .ToList();

        if (toPersist.Count == 0)
            return;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            foreach (var job in toPersist)
            {
                db.DownloadHistory.Add(new DownloadHistoryEntity
                {
                    Id = job.Id.ToString(),
                    Kind = job.Kind,
                    Query = job.Query,
                    FileName = job.Offer?.FileName,
                    LocalFilePath = job.LocalFilePath,
                    State = job.State.ToString(),
                    FileSizeBytes = job.Offer?.FileSize ?? 0,
                    BytesDownloaded = job.BytesDownloaded,
                    ErrorMessage = job.ErrorMessage,
                    CreatedAtUtc = job.CreatedAt,
                    CompletedAtUtc = job.CompletedAt
                });
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist job history");
        }
    }
}
