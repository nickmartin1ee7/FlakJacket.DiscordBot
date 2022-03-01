using System.Text;
using HtmlAgilityPack;

namespace FlakJacket.ClassLibrary;

public class FeedReport
{
    public FeedReport(HtmlNode feedNode)
    {
        Posts = new Post[feedNode.ChildNodes.Count];
        ParseFeedNode(feedNode);
    }

    private void ParseFeedNode(HtmlNode feedNode)
    {
        for (var i = 0; i < feedNode.ChildNodes.Count; i++)
        {
            var postNode = feedNode.ChildNodes[i];
            var id = postNode.Id;
            
            Posts[i] = new Post(
                postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//div[contains(@class, 'title')]")!.InnerText,
                postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//a[contains(@class, 'comment-link')]").Attributes.First(a => a.Name == "href").Value)
            {
                Id = id,
                ImageUri = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//img[contains(@class, 'bs64')]")?.Attributes.FirstOrDefault(a => a.Name == "src")?.Value,
                VideoUri = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//div[contains(@class, 'video')]//a")?.Attributes.FirstOrDefault(a => a.Name == "href")?.Value,
                TimeAgo = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//span[contains(@class, 'date_add')]").InnerText,
                Location = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//a[contains(@class, 'comment-link')]").InnerText
            };
        }
    }

    public Post[] Posts { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        foreach (var post in Posts)
        {
            sb.AppendLine(post.ToString());
        }

        return sb.ToString();
    }
}

public class Post
{
    public string Id { get; set; }
    public string Title { get; }
    public string Source { get; }
    public string? TimeAgo { get; set; }
    public string? Location { get; set; }
    public string? ImageUri { get; set; }
    public string? VideoUri { get; set; }

    public Post(string title, string source)
    {
        Title = title;
        Source = source;
    }

    public override int GetHashCode() => Source.ToUniformHashCode();

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Post Id: {Id}");
        sb.AppendLine($"Post Age: {TimeAgo}");
        sb.AppendLine($"Post Location: {Location}");
        sb.AppendLine($"Post Title: {Title}");
        sb.AppendLine($"Post Image URL: {ImageUri}");
        sb.AppendLine($"Post Video URL: {VideoUri}");
        sb.AppendLine($"Post Source: {Source}");

        return sb.ToString();
    }
}