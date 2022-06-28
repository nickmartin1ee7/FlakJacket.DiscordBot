using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway.Responders;
using Remora.Results;
using System.Threading;
using System.Threading.Tasks;
using FlakJacket.DiscordBot.WorkerService.Extensions;
using FlakJacket.DiscordBot.WorkerService.Models;

namespace FlakJacket.DiscordBot.WorkerService.Responders;

public class GuildLeftResponder : IResponder<IGuildDelete>
{
    private readonly ILogger<GuildLeftResponder> _logger;

    public GuildLeftResponder(ILogger<GuildLeftResponder> logger)
    {
        _logger = logger;
    }

    public Task<Result> RespondAsync(IGuildDelete gatewayEvent, CancellationToken ct = new())
    {
        if (!ShortTermMemory.KnownGuilds.TryGetValue(gatewayEvent.ID, out var guildName))
            return Task.FromResult(Result.FromSuccess());

        _logger.LogInformation("Left guild: {guildName} ({guildId})",
            guildName ?? "N/A",
            gatewayEvent.ID);

        ShortTermMemory.KnownGuilds.Remove(gatewayEvent.ID);

        _logger.LogGuildCount();

        return Task.FromResult(Result.FromSuccess());
    }
}