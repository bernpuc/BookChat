using BookChat.Core.Models;

namespace BookChat.Core.Services.Interfaces;

/// <summary>
/// Receives one DCC SEND transfer (connects out as the TCP client, per classic/active DCC —
/// neither the WPF app nor BookChatAndroid implement passive/reverse DCC, and this doesn't either).
/// </summary>
public interface IDccReceiver
{
    Task ReceiveAsync(FileOffer offer, Stream destination, IProgress<long>? progress, CancellationToken ct = default);
}
