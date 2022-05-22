using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;

namespace FlakJacket.ClassLibrary;

public class FeedReport
{
    public FeedReport(HtmlNode? feedNode, Func<Post, bool>[] validators)
    {
        if (feedNode is null)
        {
            Posts = Array.Empty<Post>();
        }
        else
        {
            Posts = new Post[feedNode.ChildNodes.Count];
            ParseFeedNode(feedNode, validators);
        }
    }

    private void ParseFeedNode(HtmlNode feedNode, Func<Post, bool>[] validators)
    {
        for (var i = 0; i < feedNode.ChildNodes.Count; i++)
        {
            var postNode = feedNode.ChildNodes[i];
            var id = postNode.Id;

            var newPost = new Post(
                postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//div[contains(@class, 'title')]")!.InnerText,
                postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//a[contains(@class, 'comment-link')]")!.Attributes.First(a => a.Name == "href").Value)
            {
                Id = id,
                ImageUri = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//img[contains(@class, 'bs64')]")?.Attributes.FirstOrDefault(a => a.Name == "src")?.Value,
                VideoUri = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//div[contains(@class, 'video')]//a")?.Attributes.FirstOrDefault(a => a.Name == "href")?.Value,
                TimeAgo = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//span[contains(@class, 'date_add')]").InnerText,
                Location = postNode.SelectSingleNode($"//div[contains(@id, '{id}')]//a[contains(@class, 'comment-link')]").InnerText
            };

            if (validators.All(v => v(newPost)))
            {
                Posts[i] = newPost;
            }
        }
    }

    public Post[] Posts { get; set; }
}

public record Post
{
    private const int MAX_TITLE_LENGTH = 256;

    public string Id { get; set; }
    public string Title { get; }
    public string Source { get; }
    public string? TimeAgo { get; set; }
    public string? Location { get; set; }
    public string? ImageUri { get; set; }
    public string? VideoUri { get; set; }

    public Post(string title, string source)
    {
        Source = source;

        if (title.Length > MAX_TITLE_LENGTH)
        {
            title = title[..(MAX_TITLE_LENGTH - 3)] + "...";
        }

        Title = title;
    }

    public string CalculateIdentifier()
    {
        return Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(Source)));
    }
}