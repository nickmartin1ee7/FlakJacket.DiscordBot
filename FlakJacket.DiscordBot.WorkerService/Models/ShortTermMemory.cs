using Remora.Rest.Core;
using System.Collections.Generic;

namespace FlakJacket.DiscordBot.WorkerService.Models;

public static class ShortTermMemory
{
    public static HashSet<Snowflake> KnownGuilds { get; } = new();
}