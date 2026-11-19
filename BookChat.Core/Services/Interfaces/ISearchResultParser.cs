using BookChat.Core.Models;

namespace BookChat.Core.Services.Interfaces;

public interface ISearchResultParser
{
    Task<List<SearchResult>> ParseSearchResultsFromZipAsync(Stream zipStream);
    SearchResult? ParseSearchResultLine(string line);
    List<SearchResult> RankResults(List<SearchResult> results, string authorQuery, string titleQuery);
}
