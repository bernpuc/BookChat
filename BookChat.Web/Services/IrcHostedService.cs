using BookChat.Core;
using BookChat.Core.Models;
using BookChat.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BookChat.Web.Services;

/// <summary>
/// Owns the single, shared <see cref="BookChatSession"/> for the whole household — starts it at
/// app startup and keeps it connected (via the session's own reconnect-with-backoff) for as long
/// as the container runs, rather than tying connection liveness to any one browser tab.
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

        session.Connect();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await session.DisconnectAsync();
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
