using System.IO.Compression;
using System.Text.RegularExpressions;
using BookChat.Core.Models;
using BookChat.Core.Services.Interfaces;

namespace BookChat.Core.Services;

/// <summary>
/// Parses SearchBot result zips. Ported near-verbatim from the WPF app's SearchResultParser —
/// pure string/regex parsing with no UI coupling, handling the same known XDCC bot result-line
/// formats (Ashurbanipal, pipe-hash, %hash%, ::HASH::/::INFO::, bare-filename).
/// </summary>
public class SearchResultParser : ISearchResultParser
{
    public async Task<List<SearchResult>> ParseSearchResultsFromZipAsync(Stream zipStream)
    {
        var results = new List<SearchResult>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var txtEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (txtEntry == null)
            return results;

        using var stream = txtEntry.Open();
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var result = ParseSearchResultLine(line);
            if (result != null)
                results.Add(result);
        }

        return results;
    }

    public SearchResult? ParseSearchResultLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        if (!line.StartsWith("!") || line.Contains("Search results from") || line.Contains("Searched"))
            return null;

        try
        {
            var result = new SearchResult { RawLine = line };

            var botNameMatch = Regex.Match(line, @"^!(\w+(?:-\w+)*)");
            if (!botNameMatch.Success)
                return null;

            result.BotName = botNameMatch.Groups[1].Value;
            var remainder = line[botNameMatch.Length..].TrimStart();

            var percentHashMatch = Regex.Match(remainder, @"^%([0-9A-Fa-f]+)%\s+(.+)");
            if (percentHashMatch.Success)
            {
                result.FileHash = percentHashMatch.Groups[1].Value;
                remainder = percentHashMatch.Groups[2].Value;
            }
            else
            {
                var pipeHashMatch = Regex.Match(remainder, @"^([0-9a-f]+)\s*\|\s*(.+)");
                if (pipeHashMatch.Success)
                {
                    result.FileHash = pipeHashMatch.Groups[1].Value;
                    remainder = pipeHashMatch.Groups[2].Value;
                }
                else if (remainder.Contains("::HASH::"))
                {
                    var endHashMatch = Regex.Match(remainder, @"::HASH::\s*([0-9a-f]+)");
                    if (endHashMatch.Success)
                    {
                        result.FileHash = endHashMatch.Groups[1].Value;
                        remainder = remainder.Replace(endHashMatch.Value, "").Trim();
                    }
                }
                else if (remainder.Contains(" - "))
                {
                    var ashurMatch = Regex.Match(remainder, @"^([^\s-]+)\s+-\s+(.+)");
                    if (ashurMatch.Success)
                    {
                        result.FileHash = ashurMatch.Groups[1].Value;
                        remainder = ashurMatch.Groups[2].Value;
                        return ParseAshurbanipalFormat(result, remainder);
                    }
                }
            }

            var sizeMatch = Regex.Match(remainder, @"::INFO::\s*(\d+\.?\d*\s*(?:KB|MB|GB|KiB|MiB|GiB))", RegexOptions.IgnoreCase);
            if (sizeMatch.Success)
            {
                result.FileSize = sizeMatch.Groups[1].Value;
                remainder = remainder.Replace(sizeMatch.Value, "").Trim();
            }
            else
            {
                sizeMatch = Regex.Match(remainder, @"(\d+\.?\d*\s*(?:KB|MB|GB|KiB|MiB|GiB))(?:\s|$)", RegexOptions.IgnoreCase);
                if (sizeMatch.Success)
                    result.FileSize = sizeMatch.Groups[1].Value;
            }

            var filename = remainder.Trim();
            ParseFilename(filename, result);
            result.FileName = filename;

            if (string.IsNullOrEmpty(result.FileHash))
                result.FileHash = filename;

            if (!string.IsNullOrEmpty(result.BotName) &&
                (!string.IsNullOrEmpty(result.Title) || !string.IsNullOrEmpty(result.Author)))
            {
                return result;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static SearchResult ParseAshurbanipalFormat(SearchResult result, string remainder)
    {
        var parts = remainder.Split(" - ", StringSplitOptions.None);

        if (parts.Length >= 1)
            result.Author = parts[0].Trim();

        if (parts.Length >= 2)
        {
            var titlePart = parts[1].Trim();
            var langMatch = Regex.Match(titlePart, @"\[([^\]]+)\]");
            if (langMatch.Success)
            {
                result.Language = langMatch.Groups[1].Value;
                result.Title = titlePart.Replace(langMatch.Value, "").Trim();
            }
            else
            {
                result.Title = titlePart;
            }
        }

        if (parts.Length >= 3)
        {
            var seriesPart = parts[2].Trim();

            var formatMatch = Regex.Match(seriesPart, @"\(([^)]+)\)");
            if (formatMatch.Success)
                result.Format = formatMatch.Groups[1].Value.ToUpperInvariant();

            var sizeMatch = Regex.Match(seriesPart, @"(\d+\.?\d*\s*(?:KB|MB|GB))", RegexOptions.IgnoreCase);
            if (sizeMatch.Success)
                result.FileSize = sizeMatch.Groups[1].Value;

            var seriesName = seriesPart;
            if (formatMatch.Success)
                seriesName = seriesName.Replace(formatMatch.Value, "");
            if (sizeMatch.Success)
                seriesName = seriesName.Replace(sizeMatch.Value, "");
            result.Series = seriesName.Trim();
        }

        if (parts.Length >= 4)
        {
            var tagsMatch = Regex.Match(parts[3].Trim(), @"\[([^\]]+)\]");
            if (tagsMatch.Success)
                result.Tags = tagsMatch.Groups[1].Value;
        }

        return result;
    }

    private static void ParseFilename(string filename, SearchResult result)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return;

        var extensionMatch = Regex.Match(filename, @"\.(epub|mobi|azw3|pdf|rar|zip|html)$", RegexOptions.IgnoreCase);
        if (extensionMatch.Success)
        {
            result.Format = extensionMatch.Groups[1].Value.ToUpperInvariant();
            filename = filename[..^extensionMatch.Length];
        }

        var formatInParens = Regex.Match(filename, @"\(([^)]*(?:epub|mobi|azw3|pdf|html)[^)]*)\)", RegexOptions.IgnoreCase);
        if (formatInParens.Success)
        {
            var formatText = formatInParens.Groups[1].Value;
            var formatMatch = Regex.Match(formatText, @"(epub|mobi|azw3|pdf|html)", RegexOptions.IgnoreCase);
            if (formatMatch.Success && string.IsNullOrEmpty(result.Format))
                result.Format = formatMatch.Groups[1].Value.ToUpperInvariant();
            filename = filename.Replace(formatInParens.Value, "").Trim();
        }

        filename = Regex.Replace(filename, @"\s*\([^)]*\)", "").Trim();
        filename = Regex.Replace(filename, @"\s*\[[^\]]*\]", "").Trim();

        if (filename.Contains(" - "))
        {
            var dashParts = filename.Split(" - ", 2, StringSplitOptions.None);
            if (dashParts.Length == 2)
            {
                result.Author = dashParts[0].Trim();
                result.Title = dashParts[1].Trim();
                return;
            }
        }

        result.Title = filename.Trim();
    }

    public List<SearchResult> RankResults(List<SearchResult> results, string authorQuery, string titleQuery)
    {
        foreach (var result in results)
        {
            var score = 0;

            if (result.Format.Equals("EPUB", StringComparison.OrdinalIgnoreCase))
                score += 10;
            else if (result.Format.Equals("MOBI", StringComparison.OrdinalIgnoreCase) ||
                     result.Format.Equals("AZW3", StringComparison.OrdinalIgnoreCase))
                score += 5;

            if (!string.IsNullOrWhiteSpace(authorQuery) && !string.IsNullOrWhiteSpace(result.Author) &&
                result.Author.Contains(authorQuery, StringComparison.OrdinalIgnoreCase))
                score += 20;

            if (!string.IsNullOrWhiteSpace(titleQuery) && !string.IsNullOrWhiteSpace(result.Title) &&
                result.Title.Contains(titleQuery, StringComparison.OrdinalIgnoreCase))
                score += 20;

            if (result.Language.Equals("eng", StringComparison.OrdinalIgnoreCase) ||
                result.Language.Equals("en", StringComparison.OrdinalIgnoreCase))
                score += 5;

            result.RelevanceScore = score;
        }

        return results.OrderByDescending(r => r.RelevanceScore).ToList();
    }
}
