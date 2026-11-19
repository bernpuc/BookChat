using Microsoft.EntityFrameworkCore;

namespace BookChat.Web.Data;

public class BookChatDbContext(DbContextOptions<BookChatDbContext> options) : DbContext(options)
{
    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();
    public DbSet<DownloadHistoryEntity> DownloadHistory => Set<DownloadHistoryEntity>();
}

/// <summary>
/// Single-row settings table. Non-secret only: the NickServ password comes from an environment
/// variable (see docker-compose .env, per WEB_PORT_PLAN.md §4.3) and is never written here.
///
/// No Calibre fields: this deployment's delivery mechanism is BookChat__DownloadDirectory
/// pointing at Calibre's intake watch folder, not a direct calibredb/content-server integration —
/// see IrcHostedService and bookchat.md in KassandraWiki. (A deployed database may still have
/// legacy Calibre* columns on its Settings table from before this was removed — EF's
/// EnsureCreatedAsync only creates tables when none exist yet, so it won't touch them on an
/// existing install; they're simply unused from here on.)
/// </summary>
public class SettingsEntity
{
    public int Id { get; set; } = 1;
    public string IrcServer { get; set; } = "irc.irchighway.net";
    public int IrcPort { get; set; } = 6667;
    public bool IrcUseSsl { get; set; }
    public string IrcNickname { get; set; } = "ircuser";
    public string IrcChannel { get; set; } = "#ebooks";
}

/// <summary>
/// Persisted record of a completed/failed job — fixes the gap both existing apps have (neither
/// persists its job queue; BookChatAndroid's is lost on process death, see WEB_PORT_PLAN.md §1.2).
/// </summary>
public class DownloadHistoryEntity
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? LocalFilePath { get; set; }
    public string State { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long BytesDownloaded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
