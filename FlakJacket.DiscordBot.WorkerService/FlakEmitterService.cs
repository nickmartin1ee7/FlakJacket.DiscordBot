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
using Remora.Rest.Results;
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

        await BroadcastPostsAsync(guildId);
    }

    private async Task UpdateJob()
    {
        var lastCallFaulted = false;

        while (!_cts!.IsCancellationRequested)
        {
            if (ShortTermMemory.KnownGuilds.Any())
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

                    if (_lastReport.Posts.Any())
                    {
                        await BroadcastPostsAsync(ShortTermMemory.KnownGuilds.ToArray());
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
            }
            else
            {
                _logger.LogInformation("No guilds to broadcast to");
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
        var rangeOfPosts = _lastReport!.Posts[..GetIndexUpTo(_lastReport.Posts, _settings.MaxBroadcastPosts)]
            .Where(p => p?.TimeAgo is not null)
            .OrderBy(l => l.TimeAgo!.Contains("hour"))
            .ThenBy(l => int.Parse(l.TimeAgo.Split(' ')[0]))
            .ToArray();

        if (!rangeOfPosts.Any() || !targetGuilds.Any())
        {
            return Task.CompletedTask;
        }

        var groupedPostsAndEmbeds = rangeOfPosts.Zip(rangeOfPosts
            .Select(CreateEmbedFrom),
            (post, embed) => new Tuple<Post, Embed>(post, embed));
        
        _ = Parallel.ForEach(targetGuilds, async knownGuild =>
        {
            var guildChannels = await _guildApi.GetGuildChannelsAsync(knownGuild);
            var feedChannel = guildChannels.Entity?.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

            if (feedChannel is null)
            {
                _logger.LogTrace("Guild {guildId} has not been set up", knownGuild);
                return;
            }

            var lastMessages = await _channelApi.GetChannelMessagesAsync(feedChannel.ID);

            var newEmbeds = groupedPostsAndEmbeds
                .Where(g => !HasPostBeenEmitted(g.Item1.CalculateIdentifier(), lastMessages))
                .Select(g => g.Item2)
                .ToArray();

            if (newEmbeds.Any())
            {
                var result = await _channelApi.CreateMessageAsync(feedChannel.ID,
                    embeds: new Optional<IReadOnlyList<IEmbed>>(newEmbeds));

                if (result.IsSuccess)
                {
                    _logger.LogTrace("Broadcast {postCount} posts to guild {guildId}", result.Entity.Embeds.Count,
                        knownGuild);
                }
                else
                {
                    _logger.LogError("Failed to Broadcast {postCount} posts to guild {guildId}: {error}", newEmbeds.Length,
                        knownGuild, (result.Error as RestResultError<RestError>)?.Error.Message);
                }
            }
            else
            {
                _logger.LogTrace("Guild {guildId} is already up to date", knownGuild);
            }
        });

        return Task.CompletedTask;
    }

    private static bool HasPostBeenEmitted(string newPostIdentifier, Result<IReadOnlyList<IMessage>> messages)
    {
        if (messages.IsSuccess
            && messages.IsDefined()
            && !messages.Entity.Any()
            || !messages.Entity.Any(e => e.Embeds.Any()))
            return false;

        var existingMessageIdentifiers = messages.Entity
                .SelectMany(m => m.Embeds)
                .Where(e => e.Footer.HasValue)
                .Select(e => e.Footer.Value.Text);
        
        return existingMessageIdentifiers
            .Any(existingIdentifier =>
                existingIdentifier == newPostIdentifier);
    }

    private static Embed CreateEmbedFrom(Post post)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Reported **{post.TimeAgo}** for the following location: **{post.Location}**");
        sb.AppendLine();
        sb.AppendLine($"Find out more at: {post.Source}");

        if (post.VideoUri is not null)
            sb.AppendLine($"Video: {post.VideoUri}");

        return new Embed(
            Author: new Optional<IEmbedAuthor>(new EmbedAuthor(post.Id)),
            Title: post.Title,
            Description: sb.ToString(),
            Thumbnail: post.ImageUri is null ? new Optional<IEmbedThumbnail>() : new EmbedThumbnail(post.ImageUri),
            Footer: new EmbedFooter(post.CalculateIdentifier()));
    }

    public void Dispose()
    {
        _ds?.Dispose();
    }
}