using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BookChat.Models;
using BookChat.Services.Interfaces;

namespace BookChat.Services
{
    /// <summary>
    /// Service for parsing search results from SearchBot
    /// </summary>
    public class SearchResultParser : ISearchResultParser
    {
        /// <summary>
        /// Extracts and parses search results from a zip file
        /// </summary>
        public async Task<List<SearchResult>> ParseSearchResultsFromZipAsync(string zipFilePath)
        {
            var results = new List<SearchResult>();

            if (!File.Exists(zipFilePath))
                return results;

            try
            {
                using var archive = ZipFile.OpenRead(zipFilePath);

                // Find the first .txt file in the zip
                var txtEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

                if (txtEntry == null)
                    return results;

                // Read the text file
                using var stream = txtEntry.Open();
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var result = ParseSearchResultLine(line);
                    if (result != null)
                    {
                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing zip file: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Parses a single search result line
        /// Handles multiple bot formats:
        /// - !BotName hash - Author - Title [lang] (format) size - [tags] (Ashurbanipal)
        /// - !BotName filename ::INFO:: size (Bsk, Oatmeal)
        /// - !BotName hash | filename ::INFO:: size (Dumbledoo, GER-Borg, TrainFiles)
        /// - !BotName %hash% filename ::INFO:: size (Firebound, FWServer)
        /// - !BotName filename ::INFO:: size ::HASH:: hash (Ook)
        /// - !BotName filename (Dumbledore, Horla, Wench)
        /// </summary>
        public SearchResult? ParseSearchResultLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            // Skip header lines and non-result lines
            if (!line.StartsWith("!") || line.Contains("Search results from") || line.Contains("Searched"))
                return null;

            try
            {
                var result = new SearchResult { RawLine = line };

                // Extract bot name (always starts with !)
                var botNameMatch = Regex.Match(line, @"^!(\w+(?:-\w+)*)");
                if (!botNameMatch.Success)
                    return null;

                result.BotName = botNameMatch.Groups[1].Value;
                var remainder = line.Substring(botNameMatch.Length).TrimStart();

                // Try to extract hash and identify format
                string filename = "";

                // Format 1: %HEX% style hash (Firebound, FWServer)
                var percentHashMatch = Regex.Match(remainder, @"^%([0-9A-Fa-f]+)%\s+(.+)");
                if (percentHashMatch.Success)
                {
                    result.FileHash = percentHashMatch.Groups[1].Value;
                    remainder = percentHashMatch.Groups[2].Value;
                }
                // Format 2: hash | filename (Dumbledoo, GER-Borg, TrainFiles)
                else
                {
                    var pipeHashMatch = Regex.Match(remainder, @"^([0-9a-f]+)\s*\|\s*(.+)");
                    if (pipeHashMatch.Success)
                    {
                        result.FileHash = pipeHashMatch.Groups[1].Value;
                        remainder = pipeHashMatch.Groups[2].Value;
                    }
                    // Format 3: ::HASH:: at the end (Ook)
                    else if (remainder.Contains("::HASH::"))
                    {
                        var endHashMatch = Regex.Match(remainder, @"::HASH::\s*([0-9a-f]+)");
                        if (endHashMatch.Success)
                        {
                            result.FileHash = endHashMatch.Groups[1].Value;
                            remainder = remainder.Replace(endHashMatch.Value, "").Trim();
                        }
                    }
                    // Format 4: Ashurbanipal style (hash - Author - Title)
                    else if (remainder.Contains(" - "))
                    {
                        var ashurMatch = Regex.Match(remainder, @"^([^\s-]+)\s+-\s+(.+)");
                        if (ashurMatch.Success)
                        {
                            result.FileHash = ashurMatch.Groups[1].Value;
                            remainder = ashurMatch.Groups[2].Value;

                            // Parse Ashurbanipal's detailed format
                            return ParseAshurbanipalFormat(result, remainder);
                        }
                    }
                }

                // Extract file size if present
                var sizeMatch = Regex.Match(remainder, @"::INFO::\s*(\d+\.?\d*\s*(?:KB|MB|GB|KiB|MiB|GiB))", RegexOptions.IgnoreCase);
                if (sizeMatch.Success)
                {
                    result.FileSize = sizeMatch.Groups[1].Value;
                    remainder = remainder.Replace(sizeMatch.Value, "").Trim();
                }
                else
                {
                    // Try without ::INFO::
                    sizeMatch = Regex.Match(remainder, @"(\d+\.?\d*\s*(?:KB|MB|GB|KiB|MiB|GiB))(?:\s|$)", RegexOptions.IgnoreCase);
                    if (sizeMatch.Success)
                    {
                        result.FileSize = sizeMatch.Groups[1].Value;
                    }
                }

                // Clean up remainder to get filename
                filename = remainder.Trim();

                // Parse filename to extract author, title, and format
                ParseFilename(filename, result);

                // If no hash was found, use filename as identifier
                if (string.IsNullOrEmpty(result.FileHash))
                {
                    result.FileHash = filename;
                }

                // Only return if we have at least bot name and some content
                if (!string.IsNullOrEmpty(result.BotName) &&
                    (!string.IsNullOrEmpty(result.Title) || !string.IsNullOrEmpty(result.Author)))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing line '{line}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses Ashurbanipal's detailed format: Author - Title [lang] - Series (format) size - [tags]
        /// </summary>
        private SearchResult ParseAshurbanipalFormat(SearchResult result, string remainder)
        {
            var parts = remainder.Split(new[] { " - " }, StringSplitOptions.None);

            if (parts.Length >= 1)
            {
                result.Author = parts[0].Trim();
            }

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
                {
                    result.Format = formatMatch.Groups[1].Value.ToUpper();
                }

                var sizeMatch = Regex.Match(seriesPart, @"(\d+\.?\d*\s*(?:KB|MB|GB))", RegexOptions.IgnoreCase);
                if (sizeMatch.Success)
                {
                    result.FileSize = sizeMatch.Groups[1].Value;
                }

                var seriesName = seriesPart;
                if (formatMatch.Success)
                    seriesName = seriesName.Replace(formatMatch.Value, "");
                if (sizeMatch.Success)
                    seriesName = seriesName.Replace(sizeMatch.Value, "");
                result.Series = seriesName.Trim();
            }

            if (parts.Length >= 4)
            {
                var tagsPart = parts[3].Trim();
                var tagsMatch = Regex.Match(tagsPart, @"\[([^\]]+)\]");
                if (tagsMatch.Success)
                {
                    result.Tags = tagsMatch.Groups[1].Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Parses a filename to extract author, title, and format
        /// Typical patterns: "Author - Title.ext", "Author - Title (format).ext", "Title.ext"
        /// </summary>
        private void ParseFilename(string filename, SearchResult result)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return;

            // Extract format from file extension
            var extensionMatch = Regex.Match(filename, @"\.(epub|mobi|azw3|pdf|rar|zip|html)$", RegexOptions.IgnoreCase);
            if (extensionMatch.Success)
            {
                result.Format = extensionMatch.Groups[1].Value.ToUpper();
                filename = filename.Substring(0, filename.Length - extensionMatch.Length);
            }

            // Extract format from parentheses like (epub), (retail), (v5.0), etc.
            var formatInParens = Regex.Match(filename, @"\(([^)]*(?:epub|mobi|azw3|pdf|html)[^)]*)\)", RegexOptions.IgnoreCase);
            if (formatInParens.Success)
            {
                var formatText = formatInParens.Groups[1].Value;
                // Extract just the format part
                var formatMatch = Regex.Match(formatText, @"(epub|mobi|azw3|pdf|html)", RegexOptions.IgnoreCase);
                if (formatMatch.Success && string.IsNullOrEmpty(result.Format))
                {
                    result.Format = formatMatch.Groups[1].Value.ToUpper();
                }
                filename = filename.Replace(formatInParens.Value, "").Trim();
            }

            // Remove common suffixes like (retail), (v5.0), (wrong cover), etc.
            filename = Regex.Replace(filename, @"\s*\([^)]*\)", "").Trim();
            filename = Regex.Replace(filename, @"\s*\[[^\]]*\]", "").Trim();

            // Split by " - " to get Author and Title
            if (filename.Contains(" - "))
            {
                var dashParts = filename.Split(new[] { " - " }, 2, StringSplitOptions.None);
                if (dashParts.Length == 2)
                {
                    result.Author = dashParts[0].Trim();
                    result.Title = dashParts[1].Trim();
                    return;
                }
            }

            // If no " - " separator, treat entire filename as title
            result.Title = filename.Trim();
        }

        /// <summary>
        /// Ranks search results by relevance to the query
        /// </summary>
        public List<SearchResult> RankResults(List<SearchResult> results, string authorQuery, string titleQuery)
        {
            foreach (var result in results)
            {
                int score = 0;

                // Prefer EPUB format
                if (result.Format.Equals("EPUB", StringComparison.OrdinalIgnoreCase))
                    score += 10;
                else if (result.Format.Equals("MOBI", StringComparison.OrdinalIgnoreCase) ||
                         result.Format.Equals("AZW3", StringComparison.OrdinalIgnoreCase))
                    score += 5;

                // Match author
                if (!string.IsNullOrWhiteSpace(authorQuery) && !string.IsNullOrWhiteSpace(result.Author))
                {
                    if (result.Author.Contains(authorQuery, StringComparison.OrdinalIgnoreCase))
                        score += 20;
                }

                // Match title
                if (!string.IsNullOrWhiteSpace(titleQuery) && !string.IsNullOrWhiteSpace(result.Title))
                {
                    if (result.Title.Contains(titleQuery, StringComparison.OrdinalIgnoreCase))
                        score += 20;
                }

                // Prefer English
                if (result.Language.Equals("eng", StringComparison.OrdinalIgnoreCase) ||
                    result.Language.Equals("en", StringComparison.OrdinalIgnoreCase))
                    score += 5;

                result.RelevanceScore = score;
            }

            return results.OrderByDescending(r => r.RelevanceScore).ToList();
        }
    }
}
