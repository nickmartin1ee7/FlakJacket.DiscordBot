using Remora.Rest.Core;

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FlakJacket.DiscordBot.WorkerService.Models;

public static class ShortTermMemory
{
    public static IDictionary<Snowflake, string?> KnownGuilds { get; } = new ConcurrentDictionary<Snowflake, string?>();
}