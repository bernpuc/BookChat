using System;
using System.Threading.Tasks;
using BookChat.Models;

namespace BookChat.Services.Interfaces
{
    /// <summary>
    /// Service for handling DCC file transfers
    /// </summary>
    public interface IDccService
    {
        /// <summary>
        /// Event raised when a file is offered via DCC SEND
        /// </summary>
        event EventHandler<FileOffer>? FileOffered;

        /// <summary>
        /// Event raised when download progress is updated
        /// </summary>
        event EventHandler<DownloadProgress>? ProgressUpdated;

        /// <summary>
        /// Event raised when a download completes
        /// </summary>
        event EventHandler<DownloadProgress>? DownloadCompleted;

        /// <summary>
        /// Parses a raw IRC message to detect DCC SEND offers
        /// </summary>
        /// <param name="rawMessage">The raw IRC message</param>
        /// <param name="senderNickname">The nickname of the sender</param>
        /// <returns>A FileOffer if DCC SEND was detected, null otherwise</returns>
        FileOffer? ParseDccOffer(string rawMessage, string senderNickname);

        /// <summary>
        /// Accepts a file offer and begins downloading
        /// </summary>
        /// <param name="fileOffer">The file offer to accept</param>
        /// <param name="downloadDirectory">Directory to save the file</param>
        /// <returns>Task representing the download operation</returns>
        Task<DownloadProgress> AcceptFileAsync(FileOffer fileOffer, string downloadDirectory);

        /// <summary>
        /// Cancels an ongoing download
        /// </summary>
        /// <param name="fileOffer">The file offer to cancel</param>
        void CancelDownload(FileOffer fileOffer);
    }
}
