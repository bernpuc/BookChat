using System.Net;
using System.Net.Sockets;
using BookChat.Core.Models;
using BookChat.Core.Services.Interfaces;

namespace BookChat.Core.Services;

/// <summary>
/// The one shared "receive N bytes over a socket with periodic DCC ACKs" primitive.
///
/// Both BookChat (WPF) and BookChatAndroid independently wrote this twice each (once for book
/// files, once more inline for search-result zips) — see WEB_PORT_PLAN.md §1.3/§3. Writing it
/// once against a destination <see cref="Stream"/> (not a file path) lets callers decide whether
/// the bytes land in a file or an in-memory buffer, fixing that duplication.
/// </summary>
public class DccReceiver : IDccReceiver
{
    private const int BufferSize = 8192;
    private const int AckInterval = 4096;

    public async Task ReceiveAsync(FileOffer offer, Stream destination, IProgress<long>? progress, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(offer.IpAddress, offer.Port, ct);
        using var networkStream = client.GetStream();

        var buffer = new byte[BufferSize];
        long totalBytesRead = 0;
        long lastAckBytes = 0;

        while (totalBytesRead < offer.FileSize && !ct.IsCancellationRequested)
        {
            var bytesToRead = (int)Math.Min(BufferSize, offer.FileSize - totalBytesRead);
            var bytesRead = await networkStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            if (bytesRead == 0)
                break; // connection closed early

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytesRead += bytesRead;
            progress?.Report(totalBytesRead);

            if (totalBytesRead - lastAckBytes >= AckInterval || totalBytesRead >= offer.FileSize)
            {
                await SendAckAsync(networkStream, totalBytesRead, ct);
                lastAckBytes = totalBytesRead;
            }
        }

        if (totalBytesRead < offer.FileSize)
            throw new IOException($"DCC transfer ended early: got {totalBytesRead} of {offer.FileSize} bytes.");
    }

    private static async Task SendAckAsync(NetworkStream stream, long bytesReceived, CancellationToken ct)
    {
        var ackBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)bytesReceived));
        await stream.WriteAsync(ackBytes, ct);
        await stream.FlushAsync(ct);
    }
}
