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

namespace FlakJacket.DiscordBot.WorkerService;

public class FlakEmitterService : IDisposable
{
    private readonly ILogger<FlakEmitterService> _logger;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly DiscordSettings _settings;
    private readonly DataSource _ds;
    private readonly TimeSpan _delayTime;

    private CancellationTokenSource _cts;
    private Task _updateJobTask;
    private FeedReport? lastReport;

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
        _updateJobTask = Task.Run(UpdateJob);
    }

    public async Task EmitToAsync(Snowflake guildId)
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;

        if (lastReport is null || !lastReport.Posts.Any())
            return;

        var lastPost = lastReport.Posts.FirstOrDefault();

        if (lastPost is null)
            return;

        var channels = await _guildApi.GetGuildChannelsAsync(guildId);
        var feedChannel = channels.Entity.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

        if (feedChannel is null) return;

        var lastMessages = await _channelApi.GetChannelMessagesAsync(feedChannel.ID);
        if (lastMessages.Entity
                .FirstOrDefault(m => m.Embeds
                    .FirstOrDefault(e => e.Title.Value.GetHashCode() == lastPost.Title?.GetHashCode()) is not null)
            is not null)
        {
            _logger.LogTrace("Last post already present on channel {channel}", feedChannel);
            return;
        }

        var result = await _channelApi.CreateMessageAsync(feedChannel.ID, embeds: new Optional<IReadOnlyList<IEmbed>>(new List<IEmbed> { CreateEmbedFromLatestPost(lastPost) }));

        _logger.LogTrace("Broadcast to {guildId}: {result}", guildId, result.Entity.ID);
    }

    private async Task UpdateJob()
    {
        int? lastPostHash = null;
        DateTime? lastUpdate;
        bool lastCallFaulted = false;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _logger.LogTrace("Downloading latest content...");

                lastReport = await _ds.GetAsync();
                lastUpdate = DateTime.Now;

                if (lastCallFaulted)
                {
                    lastCallFaulted = false;
                    _logger.LogTrace("Connection re-established");
                }

                var latestPostHash = lastReport.Posts.First().Title?.GetHashCode();
                _logger.LogTrace("Old Post: {lastPostHash} | New Post: {latestPostHash}", lastPostHash, latestPostHash);

                if (lastPostHash == default)
                {
                    lastPostHash = latestPostHash;
                    _logger.LogInformation("Got initial content @ {lastUpdate}", lastUpdate);
                    await BroadcastLatestPostAsync(lastReport.Posts.FirstOrDefault());
                }
                else if (latestPostHash != lastPostHash)
                {
                    lastPostHash = latestPostHash;
                    _logger.LogInformation("New update @ {lastUpdate}", lastUpdate);
                    await BroadcastLatestPostAsync(lastReport.Posts.FirstOrDefault());
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

    private async Task BroadcastLatestPostAsync(Post? latestPost)
    {
        if (latestPost is null) return;
        if (!ShortTermMemory.KnownGuilds.Any()) return;

        var embed = CreateEmbedFromLatestPost(latestPost);

        foreach (var knownGuild in ShortTermMemory.KnownGuilds)
        {
            var channels = await _guildApi.GetGuildChannelsAsync(knownGuild);
            var feedChannel = channels.Entity.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

            if (feedChannel is null) continue;

            var lastMessages = await _channelApi.GetChannelMessagesAsync(feedChannel.ID);
            if (lastMessages.Entity
                    .FirstOrDefault(m => m.Embeds
                        .FirstOrDefault(e => e.Title.Value.GetHashCode() == latestPost.Title.GetHashCode()) is not null)
                is not null)
            {
                _logger.LogTrace("Last post already present on channel {channel}", feedChannel);
                continue;
            }

            var result = await _channelApi.CreateMessageAsync(feedChannel.ID, embeds: new Optional<IReadOnlyList<IEmbed>>(new List<IEmbed> { embed }));

            _logger.LogTrace("Broadcast to {guildId}: {result}", knownGuild, result.Entity.ID);
        }
    }

    private static Embed CreateEmbedFromLatestPost(Post? latestPost)
    {
        return new Embed(
            Title: latestPost.Title,
            Description: @$"Reported **{latestPost.TimeAgo}** for the following location: **{latestPost.Location}**

Find out more at: {latestPost.Source}",
            Thumbnail: latestPost.ImageUri is null ? null : new Optional<IEmbedThumbnail>(new EmbedThumbnail(latestPost.ImageUri)),
            Footer: latestPost.Id is null ? null : new Optional<IEmbedFooter>(
                new EmbedFooter(latestPost.Id)));
    }

    public void Dispose()
    {
        _ds?.Dispose();
    }
}