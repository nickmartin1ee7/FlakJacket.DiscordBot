﻿using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace FlakJacket.ClassLibrary;

public class DataSource : IDisposable
{
    private readonly ILogger<DataSource> _logger;
    private readonly HttpClient _client = new();

    public DataSource(ILogger<DataSource> logger)
    {
        _logger = logger;
    }

    public async Task<FeedReport> GetAsync(string uri)
    {
        var result = await _client.GetAsync(uri);
        var html = await result.Content.ReadAsStringAsync();

        if (!result.IsSuccessStatusCode || string.IsNullOrWhiteSpace(html))
        {
            throw new Exception($"No content received. Error ({result.StatusCode}) {result.ReasonPhrase}");
        }
        
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var targetFeedNode = doc.GetElementbyId("feedler");

        _logger.LogTrace("Data from {uri}: {feedHtml}", uri, targetFeedNode);
        
        return new FeedReport(targetFeedNode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}