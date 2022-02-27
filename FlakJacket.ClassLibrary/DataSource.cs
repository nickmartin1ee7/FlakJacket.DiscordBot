using HtmlAgilityPack;

namespace FlakJacket.ClassLibrary;

public class DataSource : IDisposable
{
    private const string SOURCE_URL = "https://liveuamap.com";

    private readonly HttpClient _client = new();

    public async Task<FeedReport> GetAsync()
    {
        var result = await _client.GetAsync(SOURCE_URL);
        var html = await result.Content.ReadAsStringAsync();

        if (!result.IsSuccessStatusCode || string.IsNullOrWhiteSpace(html))
        {
            throw new Exception($"No content received. Error ({result.StatusCode}) {result.ReasonPhrase}");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return new FeedReport(doc.GetElementbyId("feedler"));
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}