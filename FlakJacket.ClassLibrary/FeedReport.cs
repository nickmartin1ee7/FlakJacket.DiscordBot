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
            
            var headerNode = postNode.FirstChild;
            var imgUri = headerNode.SelectSingleNode("img")?.Attributes
                .FirstOrDefault(a => a.Name == "src")?.Value;
            var timeAgo = headerNode.SelectSingleNode("span")?.InnerText;
            var location = headerNode.SelectSingleNode("div")?.InnerText;
            var source = headerNode.SelectSingleNode("div")?.FirstChild.Attributes
                .FirstOrDefault(a => a.Name == "href")?.Value;

            var title = postNode.ChildNodes[1].InnerText;

            Posts[i] = new Post
            {
                Id = id,
                ImageUri = imgUri,
                TimeAgo = timeAgo,
                Location = location,
                Title = title,
                Source = source
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
    public string? Id { get; set; }
    public string? ImageUri { get; set; }
    public string? TimeAgo { get; set; }
    public string? Location { get; set; }
    public string? Title { get; set; }
    public string? Source { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Post Id: {Id}");
        sb.AppendLine($"Post Age: {TimeAgo}");
        sb.AppendLine($"Post Location: {Location}");
        sb.AppendLine($"Post Title: {Title}");
        sb.AppendLine($"Post Image URL: {ImageUri}");
        sb.AppendLine($"Post Source: {Source}");

        return sb.ToString();
    }
}