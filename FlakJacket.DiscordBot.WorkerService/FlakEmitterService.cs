using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlakJacket.ClassLibrary;
using FlakJacket.DiscordBot.WorkerService.Models;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Rest.Core;
using Remora.Results;

namespace FlakJacket.DiscordBot.WorkerService;

public class FlakEmitterService : IDisposable
{
    private const int MAX_EMBED_LENGTH = 256;
    
    private readonly ILogger<FlakEmitterService> _logger;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly DiscordSettings _settings;
    private readonly DataSource _ds;
    private readonly TimeSpan _delayTime;

    private CancellationTokenSource? _cts;
    private FeedReport? _lastReport;

    public FlakEmitterService(ILogger<FlakEmitterService> logger,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestChannelAPI channelApi,
        DiscordSettings settings,
        DataSource ds)
    {
        _logger = logger;
        _guildApi = guildApi;
        _channelApi = channelApi;
        _settings = settings;
        _ds = ds;
        _delayTime = TimeSpan.Parse(settings.UpdateDelay);
    }
    
    public void Start()
    {
        if (_cts is not null && !_cts.IsCancellationRequested)
            return;

        // Must be cancelled and not running to spawn another job
        _cts = new CancellationTokenSource();
        _ = Task.Run(UpdateJob);
    }

    public async Task EmitToAsync(Snowflake guildId)
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;

        if (_lastReport is null || !_lastReport.Posts.Any())
            return;

        await BroadcastPostsAsync(guildId);
    }

    private async Task UpdateJob()
    {
        int? lastPostHash = null;
        var lastCallFaulted = false;

        while (!_cts!.IsCancellationRequested)
        {
            try
            {
                _logger.LogTrace("Downloading latest content...");

                _lastReport = await _ds.GetAsync(_settings.SourceUri);

                if (lastCallFaulted)
                {
                    lastCallFaulted = false;
                    _logger.LogTrace("Connection re-established");
                }

                var latestPostHash = _lastReport?.Posts.First().GetHashCode();
                _logger.LogTrace("Old Post: {lastPostHash} | New Post: {latestPostHash}", lastPostHash, latestPostHash);

                if (lastPostHash == default)
                {
                    lastPostHash = latestPostHash;
                    _logger.LogInformation("Got initial content");
                    await BroadcastPostsAsync(ShortTermMemory.KnownGuilds.ToArray());
                }
                else if (latestPostHash != lastPostHash)
                {
                    lastPostHash = latestPostHash;
                    _logger.LogInformation("New update");
                    await BroadcastPostsAsync(ShortTermMemory.KnownGuilds.ToArray());
                }
                else
                {
                    _logger.LogTrace("No new content");
                }
            }
            catch (Exception e)
            {
                lastCallFaulted = true;

                _logger.LogError(new EventId(e.HResult), e, e.Message);
                _logger.LogTrace("Retrying in {_delayTime}...", _delayTime);

                await Task.Delay(_delayTime);
                continue;
            }

            _logger.LogTrace("Updating in {_delayTime} @ {nextUpdate}", _delayTime, DateTime.Now.Add(_delayTime));

            await Task.Delay(_delayTime);
        }
    }

    private static int GetIndexUpTo<T>(IReadOnlyCollection<T> arr, int maxIndex)
    {
        return arr.Count > maxIndex ? maxIndex : arr.Count - 1;
    }

    private Task BroadcastPostsAsync(params Snowflake[] targetGuilds)
    {
        var posts = _lastReport?.Posts[..GetIndexUpTo(_lastReport?.Posts, _settings.MaxBroadcastPosts)];

        if (posts is null || !posts.Any()) return Task.CompletedTask;
        if (!targetGuilds.Any()) return Task.CompletedTask;

        foreach (var post in posts.Reverse())
        {
            if (post is null || post.Title.Length > MAX_EMBED_LENGTH)
                continue;

            var embed = CreateEmbedFrom(post);

            _ = Parallel.ForEach(targetGuilds, async knownGuild =>
            {
                var channels = await _guildApi.GetGuildChannelsAsync(knownGuild);
                var feedChannel = channels.Entity?.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

                if (feedChannel is null) return;

                var lastMessages = await _channelApi.GetChannelMessagesAsync(feedChannel.ID);

                if (HasPostBeenEmitted(post, lastMessages))
                {
                    _logger.LogTrace("Post {existingPost} already present on channel {channel}", post.GetHashCode(), feedChannel);
                    return;
                }

                var result = await _channelApi.CreateMessageAsync(feedChannel.ID, embeds: new Optional<IReadOnlyList<IEmbed>>(new List<IEmbed> { embed }));

                if (result.IsSuccess)
                {
                    _logger.LogTrace("Broadcast post {post} to {guildId}: {result}", post.GetHashCode(), knownGuild,
                        result.Entity.ID);
                }
                else
                {
                    _logger.LogError("Failed to Broadcast post {post} to {guildId} due to {error}", post.GetHashCode(), knownGuild, result.Error?.Message);
                }
            });

            // If last post to go out
            if (post.GetHashCode() == posts.First().GetHashCode())
            {
                _logger.LogInformation("Finished broadcasting");
            }
        }

        return Task.CompletedTask;
    }

    private static bool HasPostBeenEmitted(Post post, Result<IReadOnlyList<IMessage>> messages)
    {
        if (messages.IsSuccess && !messages.Entity.Any())
            return false;

        var postHashCode = post.GetHashCode().ToString();

        return messages.Entity
                .Any(m => m.Embeds
                    .Any(e => e.Footer.HasValue && e.Footer.Value.Text == postHashCode));
    }

    private static Embed CreateEmbedFrom(Post post)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Reported **{post.TimeAgo}** for the following location: **{post.Location}**");
        sb.AppendLine();
        sb.AppendLine($"Find out more at: {post.Source} ({post.Id})");
        
        if (post.VideoUri is not null)
            sb.AppendLine($"Video: {post.VideoUri}");

        return new Embed(
            Title: post.Title,
            Description: sb.ToString(),
            Video: post.VideoUri is null ? new Optional<IEmbedVideo>() : new EmbedVideo(post.VideoUri),
            Thumbnail: post.ImageUri is null ? new Optional<IEmbedThumbnail>() : new EmbedThumbnail(post.ImageUri),
            Footer: new EmbedFooter(post.GetHashCode().ToString()));
    }

    public void Dispose()
    {
        _ds?.Dispose();
    }
}