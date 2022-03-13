using HtmlAgilityPack;

namespace FlakJacket.ClassLibrary;

public class DataSource : IDisposable
{
    private readonly HttpClient _client = new();
    private readonly Func<Post, bool>[] _validators;

    public DataSource(params Func<Post, bool>[] validators)
    {
        _validators = validators;
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

        await File.WriteAllTextAsync("last-report.html", targetFeedNode.WriteTo());

        return new FeedReport(targetFeedNode, _validators);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}