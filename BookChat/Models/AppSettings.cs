namespace BookChat.Models
{
    /// <summary>
    /// Model for persisting application settings (IRC and Calibre)
    /// </summary>
    public class AppSettings
    {
        // IRC Settings
        public string IrcServer { get; set; } = "irc.irchighway.net";
        public int IrcPort { get; set; } = 6667;
        public string IrcNickname { get; set; } = "ircuser";
        public string IrcChannel { get; set; } = "#ebooks";

        // Calibre Settings
        public string LibraryPath { get; set; } = string.Empty;
        public bool AutoImport { get; set; } = false;
        public string DuplicateHandling { get; set; } = "ignore";
        public string ContentServerUrl { get; set; } = "http://localhost:8080";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
