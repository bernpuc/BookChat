using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookChat.Models;
using BookChat.Services.Interfaces;

namespace BookChat.Services
{
    /// <summary>
    /// Service for handling DCC file transfers
    /// </summary>
    public class DccService : IDccService
    {
        private const int BufferSize = 8192;
        private const int AckInterval = 4096; // Send ACK every 4KB

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();

        public event EventHandler<FileOffer>? FileOffered;
        public event EventHandler<DownloadProgress>? ProgressUpdated;
        public event EventHandler<DownloadProgress>? DownloadCompleted;

        /// <summary>
        /// Parses a raw IRC message to detect DCC SEND offers
        /// Format: :nickname!user@host PRIVMSG target :\x01DCC SEND filename ip port filesize\x01
        /// </summary>
        public FileOffer? ParseDccOffer(string rawMessage, string senderNickname)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
                return null;

            // Look for DCC SEND in the message (CTCP format uses \x01 delimiters)
            // Pattern: DCC SEND filename ip port filesize
            // Filenames can be quoted if they contain spaces: "Isaac Asimov - Foundation.epub"
            var dccMatch = Regex.Match(rawMessage, @"DCC SEND\s+""?([^""]+)""?\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);

            if (!dccMatch.Success)
                return null;

            try
            {
                var fileName = dccMatch.Groups[1].Value.Trim('"'); // Remove any remaining quotes
                var ipDecimal = long.Parse(dccMatch.Groups[2].Value);
                var port = int.Parse(dccMatch.Groups[3].Value);
                var fileSize = long.Parse(dccMatch.Groups[4].Value);

                // Convert decimal IP to dotted notation
                var ipAddress = ConvertDecimalToIp(ipDecimal);

                var fileOffer = new FileOffer
                {
                    FileName = fileName,
                    SenderNickname = senderNickname,
                    IpAddress = ipAddress,
                    Port = port,
                    FileSize = fileSize,
                    OfferTime = DateTime.Now,
                    IsAccepted = false
                };

                // Raise event
                FileOffered?.Invoke(this, fileOffer);

                return fileOffer;
            }
            catch (Exception)
            {
                // Failed to parse DCC SEND message
                return null;
            }
        }

        /// <summary>
        /// Accepts a file offer and begins downloading
        /// </summary>
        public async Task<DownloadProgress> AcceptFileAsync(FileOffer fileOffer, string downloadDirectory)
        {
            if (fileOffer == null)
                throw new ArgumentNullException(nameof(fileOffer));

            if (string.IsNullOrWhiteSpace(downloadDirectory))
                throw new ArgumentNullException(nameof(downloadDirectory));

            // Ensure download directory exists
            Directory.CreateDirectory(downloadDirectory);

            // Create full file path
            var localFilePath = Path.Combine(downloadDirectory, fileOffer.FileName);

            // Handle file name conflicts
            localFilePath = GetUniqueFilePath(localFilePath);

            fileOffer.LocalFilePath = localFilePath;
            fileOffer.IsAccepted = true;

            var progress = new DownloadProgress
            {
                FileOffer = fileOffer,
                Status = DownloadStatus.Pending,
                StartTime = DateTime.Now
            };

            // Create cancellation token for this download
            var cts = new CancellationTokenSource();
            _activeDownloads[fileOffer.FileName] = cts;

            try
            {
                progress.Status = DownloadStatus.Downloading;
                ProgressUpdated?.Invoke(this, progress);

                await DownloadFileAsync(fileOffer, localFilePath, progress, cts.Token);

                progress.Status = DownloadStatus.Completed;
                progress.EndTime = DateTime.Now;
                ProgressUpdated?.Invoke(this, progress);
                DownloadCompleted?.Invoke(this, progress);
            }
            catch (OperationCanceledException)
            {
                progress.Status = DownloadStatus.Cancelled;
                progress.ErrorMessage = "Download was cancelled";
                progress.EndTime = DateTime.Now;
                ProgressUpdated?.Invoke(this, progress);
            }
            catch (Exception ex)
            {
                progress.Status = DownloadStatus.Failed;
                progress.ErrorMessage = ex.Message;
                progress.EndTime = DateTime.Now;
                ProgressUpdated?.Invoke(this, progress);
            }
            finally
            {
                _activeDownloads.TryRemove(fileOffer.FileName, out _);
            }

            return progress;
        }

        /// <summary>
        /// Cancels an ongoing download
        /// </summary>
        public void CancelDownload(FileOffer fileOffer)
        {
            if (_activeDownloads.TryRemove(fileOffer.FileName, out var cts))
            {
                cts.Cancel();
            }
        }

        /// <summary>
        /// Downloads the file using DCC protocol
        /// </summary>
        private async Task DownloadFileAsync(FileOffer fileOffer, string localFilePath, DownloadProgress progress, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();

            // Connect to the sender
            await client.ConnectAsync(fileOffer.IpAddress, fileOffer.Port, cancellationToken);

            using var networkStream = client.GetStream();
            using var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true);

            var buffer = new byte[BufferSize];
            long totalBytesRead = 0;
            int bytesRead;
            long lastAckBytes = 0;

            while (totalBytesRead < fileOffer.FileSize && !cancellationToken.IsCancellationRequested)
            {
                // Calculate how many bytes to read in this iteration
                var bytesToRead = (int)Math.Min(BufferSize, fileOffer.FileSize - totalBytesRead);

                // Read from network stream
                bytesRead = await networkStream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

                if (bytesRead == 0)
                    break; // Connection closed

                // Write to file
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

                totalBytesRead += bytesRead;
                progress.BytesDownloaded = totalBytesRead;

                // Send acknowledgment (required by DCC protocol)
                // Send ACK every AckInterval bytes or at the end
                if (totalBytesRead - lastAckBytes >= AckInterval || totalBytesRead >= fileOffer.FileSize)
                {
                    await SendAcknowledgmentAsync(networkStream, totalBytesRead, cancellationToken);
                    lastAckBytes = totalBytesRead;
                }

                // Update progress
                ProgressUpdated?.Invoke(this, progress);
            }

            // Ensure final ACK is sent
            if (totalBytesRead >= fileOffer.FileSize)
            {
                await SendAcknowledgmentAsync(networkStream, totalBytesRead, cancellationToken);
            }
        }

        /// <summary>
        /// Sends acknowledgment to the sender (DCC protocol requirement)
        /// </summary>
        private async Task SendAcknowledgmentAsync(NetworkStream stream, long bytesReceived, CancellationToken cancellationToken)
        {
            // DCC acknowledgment is the total bytes received as a 4-byte big-endian integer
            var ackBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)bytesReceived));
            await stream.WriteAsync(ackBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Converts decimal IP address to dotted notation (xxx.xxx.xxx.xxx)
        /// </summary>
        private string ConvertDecimalToIp(long decimalIp)
        {
            var bytes = BitConverter.GetBytes(decimalIp);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            // Take only the last 4 bytes (IPv4)
            var ipBytes = bytes.Skip(4).Take(4).ToArray();
            return $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
        }

        /// <summary>
        /// Gets a unique file path by appending (1), (2), etc. if file already exists
        /// </summary>
        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            int count = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension} ({count}){extension}");
                count++;
            }
            while (File.Exists(newFilePath));

            return newFilePath;
        }
    }
}
