using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using SearchService.Data;
using SearchService.Models;
using Typesense;

namespace SearchService.Controllers;

[ApiController]
[Route("[controller]")]
public partial class SearchController(
    ITypesenseClient client
) : ControllerBase {

    [HttpGet]
    [HttpGet("activities")]
    public async Task<ActionResult<IReadOnlyList<SearchActivity>>> SearchActivityAsync(string query) {
        string? tag      = null;
        Match   tagMatch = TagRegex().Match(query);
        if (tagMatch.Success) {
            tag   = tagMatch.Groups[1].Value;
            query = query.Replace(tagMatch.Value, "").Trim();
        }

        SearchParameters searchParameters = new(query, "title,content");
        if (!string.IsNullOrEmpty(tag)) {
            searchParameters.FilterBy = $"tags:=[{tag}]";
        }
        
        try {
            var results = await client.Search<SearchActivity>(SearchInitializer.ActivityCollectionName, searchParameters);
            return Ok(results.Hits.Select(hit => hit.Document));
        }
        catch (Exception e) {
            return Problem("Typesense search failed: " + e.Message);
        }
    }

    [HttpGet("lives")]
    public async Task<ActionResult<IReadOnlyList<SearchLive>>> SearchLiveAsync(string query) {
        SearchParameters searchParameters = new(query, "title");

        try {
            var results = await client.Search<SearchLive>(SearchInitializer.LiveCollectionName, searchParameters);
            return Ok(results.Hits.Select(hit => hit.Document));
        }
        catch (Exception e) {
            return Problem("Typesense search failed: " + e.Message);
        }
    }

    [GeneratedRegex(@"\[(.*?)\]")]
    private static partial Regex TagRegex();
}
