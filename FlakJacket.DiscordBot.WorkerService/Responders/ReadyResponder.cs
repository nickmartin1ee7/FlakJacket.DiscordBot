using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Services;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Responders;
using Remora.Rest.Core;
using Remora.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlakJacket.DiscordBot.WorkerService.Extensions;
using FlakJacket.DiscordBot.WorkerService.Models;
using FlakJacket.DiscordBot.WorkerService.Services;
using System.Linq;

namespace FlakJacket.DiscordBot.WorkerService.Responders;

public class ReadyResponder : IResponder<IReady>
{
    private readonly ILogger<ReadyResponder> _logger;
    private readonly DiscordGatewayClient _discordGatewayClient;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly DiscordSettings _settings;
    private readonly SlashService _slashService;
    private readonly FlakEmitterService _flakEmitterService;

    public ReadyResponder(ILogger<ReadyResponder> logger,
        DiscordGatewayClient discordGatewayClient,
        IDiscordRestGuildAPI guildApi,
        DiscordSettings settings,
        SlashService slashService,
        FlakEmitterService flakEmitterService)
    {
        _logger = logger;
        _discordGatewayClient = discordGatewayClient;
        _guildApi = guildApi;
        _settings = settings;
        _slashService = slashService;
        _flakEmitterService = flakEmitterService;
    }

    public async Task<Result> RespondAsync(IReady gatewayEvent, CancellationToken ct = new())
    {
        void UpdatePresence()
        {
            var updateCommand = new UpdatePresence(ClientStatus.Online, false, null, new IActivity[]
            {
                new Activity(_settings.Status, ActivityType.Watching)
            });

            _discordGatewayClient.SubmitCommand(updateCommand);
        }

        async Task UpdateGlobalSlashCommands()
        {
            var updateResult = await _slashService.UpdateSlashCommandsAsync(ct: ct);

            if (!updateResult.IsSuccess)
            {
                _logger.LogWarning("Failed to update application commands globally");
            }
        }

        async Task LogClientDetailsAsync()
        {
            var shardUserCount = 0;
            var shardGuilds = new Dictionary<Snowflake, string?>(gatewayEvent.Guilds.Count);

            foreach (var guild in gatewayEvent.Guilds)
            {
                ShortTermMemory.KnownGuilds.TryAdd(guild.ID, null);

                var guildResult = await _guildApi.GetGuildAsync(guild.ID, ct: ct);

                if (!guildResult.IsSuccess)
                    continue;

                shardUserCount += guildResult.Entity.ApproximateMemberCount.HasValue
                    ? guildResult.Entity.ApproximateMemberCount.Value
                    : 0;

                var guildName = guildResult.IsSuccess ? guildResult.Entity.Name : null;
                
                shardGuilds.Add(guild.ID, guildName);
                
                ShortTermMemory.KnownGuilds.Remove(guild.ID);
                ShortTermMemory.KnownGuilds.TryAdd(guild.ID, guildName);
            }

            _logger.LogGuildCount();

            _logger.LogInformation(
                "{botUser} is online for {shardGuildCount} guilds and {shardUserCount} users. Guilds: {guilds}",
                gatewayEvent.User.ToFullUsername(),
                gatewayEvent.Guilds.Count,
                shardUserCount,
                shardGuilds.Select(g => $"{g.Value} ({g.Key})"));
        }

        if (gatewayEvent.Shard.HasValue)
        {
            _logger.LogInformation("Shard Id (#{shardId}) ready ({shardIndex} of {shardCount}).",
                gatewayEvent.Shard.Value.ShardID,
                gatewayEvent.Shard.Value.ShardID + 1,
                gatewayEvent.Shard.Value.ShardCount);
        }


        if (gatewayEvent.Shard.HasValue && gatewayEvent.Shard.Value.ShardID == 0)
        {
            UpdatePresence();
            await UpdateGlobalSlashCommands();
        }

        await LogClientDetailsAsync();

        _flakEmitterService.Start();

        return Result.FromSuccess();
    }
}