using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FlakJacket.DiscordBot.WorkerService.Extensions;
using FlakJacket.DiscordBot.WorkerService.Models;

using Microsoft.Extensions.Logging;

using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway.Responders;
using Remora.Rest.Core;
using Remora.Results;

namespace FlakJacket.DiscordBot.WorkerService.Responders;

public class GuildJoinedResponder : IResponder<IGuildCreate>
{
    private readonly ILogger<GuildJoinedResponder> _logger;
    private readonly DiscordSettings _settings;
    private readonly IDiscordRestGuildAPI _guildApi;

    public GuildJoinedResponder(ILogger<GuildJoinedResponder> logger,
        DiscordSettings settings,
        IDiscordRestGuildAPI guildApi)
    {
        _logger = logger;
        _settings = settings;
        _guildApi = guildApi;
    }

    public async Task<Result> RespondAsync(IGuildCreate gatewayEvent, CancellationToken ct = new())
    {
        if (ShortTermMemory.KnownGuilds.ContainsKey(gatewayEvent.ID))
            return Result.FromSuccess();

        var joinedAt = gatewayEvent.JoinedAt.HasValue ? gatewayEvent.JoinedAt.Value : DateTimeOffset.MinValue;

        if (joinedAt >= DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            if (gatewayEvent.MemberCount.HasValue)
                _logger.LogInformation("Joined new guild: {guildName} ({guildId}) with {userCount} users.",
                    gatewayEvent.Name,
                    gatewayEvent.ID,
                    gatewayEvent.MemberCount.Value);
            else
                _logger.LogInformation("Joined new guild: {guildName} ({guildId})",
                    gatewayEvent.Name,
                    gatewayEvent.ID);
            
            _logger.LogGuildCount();

            await TryCreateFlakJacketChannelAsync(gatewayEvent);
        }

        ShortTermMemory.KnownGuilds.TryAdd(gatewayEvent.ID, gatewayEvent.Name);
        return Result.FromSuccess();
    }

    private async Task TryCreateFlakJacketChannelAsync(IGuildCreate gatewayEvent)
    {
        var feedChannel = gatewayEvent.Channels.Value.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

        if (feedChannel is not null) return;

        var createResult = await _guildApi.CreateGuildChannelAsync(
            gatewayEvent.ID,
            _settings.SetupChannelName,
            new Optional<ChannelType>(ChannelType.GuildText),
            new Optional<string>(_settings.SetupChannelDescription),
            isNsfw: true);

        if (!createResult.IsSuccess)
        {
            _logger.LogError("Failed to automatically create new channel for new guild ({guildId})", gatewayEvent.ID);
            return;
        }
    }
}