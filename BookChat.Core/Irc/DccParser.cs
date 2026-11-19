using System.Text.RegularExpressions;
using BookChat.Core.Models;

namespace BookChat.Core.Irc;

/// <summary>
/// Parses CTCP "DCC SEND" offers out of raw IRC lines.
/// Format: :nickname!user@host PRIVMSG target :\x01DCC SEND filename ip port filesize\x01
/// Ported from the WPF app's DccService.ParseDccOffer — pure string/regex logic, no changes needed.
/// </summary>
public static class DccParser
{
    private static readonly Regex DccSendPattern = new(
        @"DCC SEND\s+""?([^""]+)""?\s+(\d+)\s+(\d+)\s+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SenderNickPattern = new(
        @"^:([^!]+)!",
        RegexOptions.Compiled);

    public static FileOffer? TryParse(string rawLine)
    {
        var dccMatch = DccSendPattern.Match(rawLine);
        if (!dccMatch.Success)
            return null;

        var nickMatch = SenderNickPattern.Match(rawLine);
        if (!nickMatch.Success)
            return null;

        try
        {
            var fileName = dccMatch.Groups[1].Value.Trim('"');
            var ipDecimal = long.Parse(dccMatch.Groups[2].Value);
            var port = int.Parse(dccMatch.Groups[3].Value);
            var fileSize = long.Parse(dccMatch.Groups[4].Value);

            return new FileOffer
            {
                FileName = fileName,
                SenderNickname = nickMatch.Groups[1].Value,
                IpAddress = ConvertDecimalToIp(ipDecimal),
                Port = port,
                FileSize = fileSize,
                OfferTime = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ConvertDecimalToIp(long decimalIp)
    {
        var bytes = BitConverter.GetBytes(decimalIp);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        var ipBytes = bytes.Skip(4).Take(4).ToArray();
        return $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
    }
}
