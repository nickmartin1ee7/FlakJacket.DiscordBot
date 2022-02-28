using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Rest.Core;
using Remora.Results;
using System.ComponentModel;
using System.Threading.Tasks;
using FlakJacket.DiscordBot.WorkerService.Models;
using Remora.Rest.Results;

namespace FlakJacket.DiscordBot.WorkerService.Commands;

public class FlakJacketCommands : LoggedCommandGroup<FlakJacketCommands>
{
    private readonly FeedbackService _feedbackService;
    private readonly DiscordSettings _settings;

    public FlakJacketCommands(ILogger<FlakJacketCommands> logger,
        FeedbackService feedbackService,
        ICommandContext ctx,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestChannelAPI channelApi,
        DiscordSettings settings)
        : base(ctx, logger, guildApi, channelApi)
    {
        _feedbackService = feedbackService;
        _settings = settings;
    }

    [Command("setup")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Ephemeral]
    [Description("Setup a new channel to follow events between Ukraine and Russia")]
    public async Task<IResult> SetupAsync()
    {
        await LogCommandUsageAsync(typeof(FlakJacketCommands).GetMethod(nameof(SetupAsync)));

        var createResult = await _guildApi.CreateGuildChannelAsync(
            _ctx.GuildID.Value,
            _settings.SetupChannelName,
            new Optional<ChannelType>(ChannelType.GuildText),
            new Optional<string>("Feed for updates on the war between Ukraine and Russia"),
            isNsfw: true);

        if (!createResult.IsSuccess)
        {
            var result = await _feedbackService.SendContextualErrorAsync((createResult.Error as RestResultError<RestError>)?.Error.Message ?? "Failed to setup new channel!");
            return Result.FromError(result);
        }

        var reply = await _feedbackService.SendContextualEmbedAsync(new Embed(
                $"Channel {_settings.SetupChannelName} setup successfully",
                Description:
                $"The channel {_settings.SetupChannelName} is created. This bot will now make regular posts when an update is available. Images used may be NSFW!"),
            ct: CancellationToken);

        return reply.IsSuccess
            ? Result.FromSuccess()
            : Result.FromError(reply);
    }
}