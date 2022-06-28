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
using System.Linq;
using System.Threading.Tasks;
using FlakJacket.DiscordBot.WorkerService.Models;
using Remora.Rest.Results;
using FlakJacket.DiscordBot.WorkerService.Extensions;
using System.Collections.Generic;
using FlakJacket.DiscordBot.WorkerService.Services;

namespace FlakJacket.DiscordBot.WorkerService.Commands;

public class FlakJacketCommands : LoggedCommandGroup<FlakJacketCommands>
{
    private readonly FeedbackService _feedbackService;
    private readonly DiscordSettings _settings;
    private readonly FlakEmitterService _flakEmitterService;
    private readonly Optional<Snowflake> _adminSnowflake;

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
        _adminSnowflake = _settings.AdminSnowflake.ToSnowflake();
    }

    [Command("setup")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Ephemeral]
    [Description("Setup a new channel to follow events between Ukraine and Russia")]
    public async Task<IResult> SetupAsync([Description("This optional field is for internal use only")] string guildId = "")
    {
        await LogCommandUsageAsync(typeof(FlakJacketCommands).GetMethod(nameof(SetupAsync)));

        Snowflake targetGuildId;

        if (!string.IsNullOrWhiteSpace(guildId))
        {
            if (_adminSnowflake.HasValue && _ctx.User.ID == _adminSnowflake.Value   // Admin check
                && Snowflake.TryParse(guildId, out var guildSnowflake))
            {
                targetGuildId = guildSnowflake.Value;
            }
            else
            {
                var result = await _feedbackService.SendContextualErrorAsync("Insufficient permission for internal controls. Try again **without** optional field.");
                return Result.FromError(result);
            }
        }
        else
        {
            targetGuildId = _ctx.GuildID.Value; // Regular usage
        }

        var channels = await _guildApi.GetGuildChannelsAsync(targetGuildId);

        if (!channels.IsSuccess)
        {
            var result = await _feedbackService.SendContextualErrorAsync((channels.Error as RestResultError<RestError>)?.Error.Message ?? "Failed to setup new channel!");
            return Result.FromError(result);
        }

        var feedChannel = channels.Entity.FirstOrDefault(c => c.Name.Value == _settings.SetupChannelName);

        if (feedChannel is not null)
        {
            var result = await _feedbackService.SendContextualErrorAsync($"The channel <#{feedChannel.ID}> already exists!");
            return Result.FromError(result);
        }

        var createResult = await _guildApi.CreateGuildChannelAsync(
            targetGuildId,
            _settings.SetupChannelName,
            new Optional<ChannelType>(ChannelType.GuildText),
            new Optional<string>(_settings.SetupChannelDescription),
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

        await _flakEmitterService.EmitToAsync(targetGuildId);

        return reply.IsSuccess
            ? Result.FromSuccess()
            : Result.FromError(reply);
    }
}