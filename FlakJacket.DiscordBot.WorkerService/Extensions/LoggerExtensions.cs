using Microsoft.Extensions.Logging;
using FlakJacket.DiscordBot.WorkerService.Models;

namespace FlakJacket.DiscordBot.WorkerService.Extensions
{
    public static class LoggerExtensions
    {
        public static void LogGuildCount(this ILogger logger)
        {
            logger.LogInformation("Guild Count: {count}",
                ShortTermMemory.KnownGuilds.Count);
        }
    }
}
