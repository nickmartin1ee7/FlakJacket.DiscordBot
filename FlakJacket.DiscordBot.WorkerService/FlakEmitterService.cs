using System;
using System.Collections.Generic;
using System.Linq;
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
        var channels = await _guildApi.GetGuildChannelsAsync(guildId);
        var feedChannel = channels.Entity.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

        if (feedChannel is null) return;
        var lastMessages = await _channelApi.GetChannelMessagesAsync(feedChannel.ID);

        foreach (var post in _lastReport.Posts.Reverse())
        {
            if (lastMessages.Entity
                    .FirstOrDefault(m => m.Embeds
                        .FirstOrDefault(e => e.Title.Value.ToUniformHashCode() == post.GetHashCode()) is not null)
                is not null)
            {
                _logger.LogTrace("Post {existingPost} already present on channel {channel}", post.GetHashCode(), feedChannel);
                return;
            }

            var result = await _channelApi.CreateMessageAsync(feedChannel.ID, embeds: new Optional<IReadOnlyList<IEmbed>>(new List<IEmbed> { CreateEmbedFrom(post) }));

            _logger.LogTrace("Broadcast post {post} to {guildId}: {result}", post.GetHashCode(), guildId, result.Entity.ID);
        }
    }

    private async Task UpdateJob()
    {
        int? lastPostHash = null;
        var lastCallFaulted = false;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _logger.LogTrace("Downloading latest content...");

                _lastReport = await _ds.GetAsync(_settings.SourceUri);
                DateTime? lastUpdate = DateTime.Now;

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
                    _logger.LogInformation("Got initial content @ {lastUpdate}", lastUpdate);
                    await BroadcastPostsAsync(_lastReport?.Posts[..GetIndexUpTo(_lastReport?.Posts, _settings.MaxBroadcastPosts)]);
                }
                else if (latestPostHash != lastPostHash)
                {
                    lastPostHash = latestPostHash;
                    _logger.LogInformation("New update @ {lastUpdate}", lastUpdate);
                    await BroadcastPostsAsync(_lastReport?.Posts[..GetIndexUpTo(_lastReport?.Posts, _settings.MaxBroadcastPosts)]);
                }
                else
                {
                    _logger.LogTrace("No new content @ {lastUpdate}", lastUpdate);
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

    private static int GetIndexUpTo<T>(T[] arr, int maxIndex)
    {
        return arr.Length > maxIndex ? maxIndex : arr.Length - 1;
    }

    private async Task BroadcastPostsAsync(params Post?[] posts)
    {
        if (!posts.Any()) return;
        if (!ShortTermMemory.KnownGuilds.Any()) return;

        foreach (var post in posts.Reverse())
        {
            if (post is null)
                continue;

            var embed = CreateEmbedFrom(post);

            foreach (var knownGuild in ShortTermMemory.KnownGuilds)
            {
                var channels = await _guildApi.GetGuildChannelsAsync(knownGuild);
                var feedChannel = channels.Entity.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

                if (feedChannel is null) continue;

                var lastMessages = await _channelApi.GetChannelMessagesAsync(feedChannel.ID);

                if (HasPostBeenEmitted(post, lastMessages))
                {
                    _logger.LogTrace("Post {existingPost} already present on channel {channel}", post.GetHashCode(), feedChannel);
                    continue;
                }

                var result = await _channelApi.CreateMessageAsync(feedChannel.ID, embeds: new Optional<IReadOnlyList<IEmbed>>(new List<IEmbed> { embed }));

                if (result.IsSuccess)
                {
                    _logger.LogTrace("Broadcast post {post} to {guildId}: {result}", post.GetHashCode(), knownGuild,
                        result.Entity.ID);
                }
                else
                {
                    _logger.LogError("Failed to Broadcast post {post} to {guildId}", post.GetHashCode(), knownGuild); 
                }
            }
        }
    }

    private static bool HasPostBeenEmitted(Post? post, Result<IReadOnlyList<IMessage>> messages)
    {
        // This will keep us from spamming servers if any issue finding previous posts
        if (post?.Title is null || messages.IsSuccess && !messages.Entity.Any())
            return true;

        var postHashCode = post.GetHashCode();

        return messages.Entity
                .Any(m => m.Embeds
                    .Any(e =>
                        e.Title.Value.ToUniformHashCode() == postHashCode));
    }

    private static Embed CreateEmbedFrom(Post? post)
    {
        return new Embed(
            Title: post.Title,
            Description: @$"Reported **{post.TimeAgo}** for the following location: **{post.Location}**

Find out more at: {post.Source}",
            Thumbnail: post.ImageUri is null ? new Optional<IEmbedThumbnail>() : new Optional<IEmbedThumbnail>(new EmbedThumbnail(post.ImageUri)),
            Footer: post.Id is null ? new Optional<IEmbedFooter>() : new Optional<IEmbedFooter>(
                new EmbedFooter(post.Id)));
    }

    public void Dispose()
    {
        _ds?.Dispose();
    }
}