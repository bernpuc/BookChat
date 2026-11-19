namespace BookChat.Core.Irc;

public enum IrcConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

public record IrcStateChange(IrcConnectionState State, int Attempt = 0, string? LastError = null);
