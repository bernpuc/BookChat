using System.Collections.Generic;
using System.Threading.Tasks;
using BookChat.Models;

namespace BookChat.Services.Interfaces
{
    /// <summary>
    /// Service for parsing search results from SearchBot
    /// </summary>
    public interface ISearchResultParser
    {
        /// <summary>
        /// Extracts and parses search results from a zip file
        /// </summary>
        /// <param name="zipFilePath">Path to the zip file containing search results</param>
        /// <returns>List of parsed search results</returns>
        Task<List<SearchResult>> ParseSearchResultsFromZipAsync(string zipFilePath);

        /// <summary>
        /// Parses a single search result line
        /// </summary>
        /// <param name="line">The raw line from the search results file</param>
        /// <returns>Parsed SearchResult or null if line is invalid</returns>
        SearchResult? ParseSearchResultLine(string line);

        /// <summary>
        /// Ranks search results by relevance to the query
        /// </summary>
        /// <param name="results">List of search results</param>
        /// <param name="authorQuery">Author name query</param>
        /// <param name="titleQuery">Title query</param>
        /// <returns>Ranked list of search results</returns>
        List<SearchResult> RankResults(List<SearchResult> results, string authorQuery, string titleQuery);
    }
}
