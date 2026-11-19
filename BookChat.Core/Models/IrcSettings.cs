namespace BookChat.Core.Models;

public class IrcSettings
{
    public string Server { get; set; } = "irc.irchighway.net";
    public int Port { get; set; } = 6667;
    public bool UseSsl { get; set; } = false;
    public string Nickname { get; set; } = "ircuser";
    public string Channel { get; set; } = "#ebooks";
    public string NickServPassword { get; set; } = string.Empty;
}

public class CalibreSettings
{
    public string LibraryPath { get; set; } = string.Empty;
    public bool AutoImport { get; set; } = false;
    public string DuplicateHandling { get; set; } = "ignore";
    public string ContentServerUrl { get; set; } = "http://localhost:8080";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
