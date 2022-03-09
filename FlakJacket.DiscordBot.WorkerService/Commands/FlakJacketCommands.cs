﻿using Microsoft.Extensions.Logging;
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
using System.Linq;
using System.Threading.Tasks;
using FlakJacket.DiscordBot.WorkerService.Models;
using Remora.Rest.Results;

namespace FlakJacket.DiscordBot.WorkerService.Commands;

public class FlakJacketCommands : LoggedCommandGroup<FlakJacketCommands>
{
    private readonly FeedbackService _feedbackService;
    private readonly DiscordSettings _settings;
    private readonly FlakEmitterService _flakEmitterService;

    public FlakJacketCommands(ILogger<FlakJacketCommands> logger,
        FeedbackService feedbackService,
        ICommandContext ctx,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestChannelAPI channelApi,
        DiscordSettings settings,
        FlakEmitterService flakEmitterService)
        : base(ctx, logger, guildApi, channelApi)
    {
        _feedbackService = feedbackService;
        _settings = settings;
        _flakEmitterService = flakEmitterService;
    }

    [Command("setup")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Ephemeral]
    [Description("Setup a new channel to follow events between Ukraine and Russia")]
    public async Task<IResult> SetupAsync()
    {
        await LogCommandUsageAsync(typeof(FlakJacketCommands).GetMethod(nameof(SetupAsync)));

        var channels = await _guildApi.GetGuildChannelsAsync(_ctx.GuildID.Value);
        var feedChannel = channels.Entity.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

        if (feedChannel is not null)
        {
            var result = await _feedbackService.SendContextualErrorAsync($"The channel <#{feedChannel.ID}> already exists!");
            return Result.FromError(result);
        }

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
                $"Channel #{createResult.Entity.Name.Value} setup successfully",
                Description:
                $"The channel <#{createResult.Entity.ID}> is created. This bot will now make regular posts when an update is available. **Do not rename this channel. Images used may be NSFW.**"),
            ct: CancellationToken);

        await _flakEmitterService.EmitToAsync(_ctx.GuildID.Value);

        return reply.IsSuccess
            ? Result.FromSuccess()
            : Result.FromError(reply);
    }
}