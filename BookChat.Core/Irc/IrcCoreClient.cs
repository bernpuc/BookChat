using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BookChat.Core.Models;

namespace BookChat.Core.Irc;

/// <summary>
/// Minimal hand-rolled IRC client: no external IRC library.
///
/// This replaces Meebey.SmartIrc4net (used by the original WPF app), which doesn't fit an
/// ASP.NET Core hosted service well — it owns a blocking Thread + a classic .NET Framework-era
/// threading model, and was already hitting PlatformNotSupportedException workarounds under
/// .NET 9. BookChatAndroid proved a raw-socket client is perfectly viable for this protocol
/// (IRCHighway's #ebooks server), including its own reconnect-with-backoff, which this class
/// follows directly rather than the WPF app's manual-only reconnect.
/// </summary>
public sealed class IrcCoreClient : IAsyncDisposable
{
    private const int InitialBackoffMs = 2000;
    private const int MaxBackoffMs = 15000;

    private readonly IrcSettings _settings;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private Stream? _stream;
    private TcpClient? _tcpClient;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action<IrcStateChange>? StateChanged;
    public event Action<string>? RawLineReceived;

    public IrcConnectionState State { get; private set; } = IrcConnectionState.Disconnected;

    public IrcCoreClient(IrcSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        if (_runTask is { IsCompleted: false })
            return;

        _runCts = new CancellationTokenSource();
        _runTask = ConnectLoopAsync(_runCts.Token);
    }

    public async Task StopAsync()
    {
        _runCts?.Cancel();
        if (_runTask != null)
        {
            try { await _runTask; } catch (OperationCanceledException) { }
        }
        CloseSocket();
        SetState(IrcConnectionState.Disconnected);
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(IrcConnectionState.Connecting, attempt);
                await ConnectOnceAsync(ct);
                attempt = 0; // reset backoff after a clean connect
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                SetState(IrcConnectionState.Connecting, attempt, ex.Message);
            }

            CloseSocket();
            SetState(IrcConnectionState.Disconnected);

            if (ct.IsCancellationRequested)
                break;

            var delay = Math.Min(InitialBackoffMs * (int)Math.Pow(2, Math.Max(0, attempt - 1)), MaxBackoffMs);
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(_settings.Server, _settings.Port, ct);

        Stream stream = _tcpClient.GetStream();
        if (_settings.UseSsl)
        {
            // Public e-book IRC networks typically present self-signed certs; SSL here is for
            // bypassing carrier/ISP port blocking, not for authenticating the server.
            var ssl = new SslStream(stream, false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(_settings.Server);
            stream = ssl;
        }
        _stream = stream;

        await SendRawAsync($"NICK {_settings.Nickname}", ct);
        await SendRawAsync($"USER bookchat 0 * :BookChat Web", ct);

        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        var loggedIn = false;

        while (!ct.IsCancellationRequested)
        {
            var readTask = reader.ReadLineAsync(ct).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromMinutes(5), ct));
            if (completed != readTask)
                throw new TimeoutException("No data received from IRC server for 5 minutes.");

            var line = await readTask;
            if (line == null)
                throw new IOException("Connection closed by server.");

            RawLineReceived?.Invoke(line);

            if (line.StartsWith("PING ", StringComparison.OrdinalIgnoreCase))
            {
                await SendRawAsync($"PONG {line[5..]}", ct);
                continue;
            }

            if (!loggedIn && line.Contains(" 001 "))
            {
                loggedIn = true;
                if (!string.IsNullOrEmpty(_settings.NickServPassword))
                {
                    await SendRawAsync($"PRIVMSG NickServ :IDENTIFY {_settings.NickServPassword}", ct);
                }
                await SendRawAsync($"JOIN {_settings.Channel}", ct);
                SetState(IrcConnectionState.Connected);
            }
        }
    }

    public Task SendMessageAsync(string target, string message, CancellationToken ct = default) =>
        SendRawAsync($"PRIVMSG {target} :{message}", ct);

    private async Task SendRawAsync(string line, CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected.");

        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(bytes, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void SetState(IrcConnectionState state, int attempt = 0, string? lastError = null)
    {
        State = state;
        StateChanged?.Invoke(new IrcStateChange(state, attempt, lastError));
    }

    private void CloseSocket()
    {
        try { _stream?.Dispose(); } catch { /* already gone */ }
        try { _tcpClient?.Dispose(); } catch { /* already gone */ }
        _stream = null;
        _tcpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _writeLock.Dispose();
    }
}
