using Remora.Rest.Core;

namespace FlakJacket.DiscordBot.WorkerService.Extensions
{
    public static class StringExtensions
    {
        public static Optional<Snowflake> ToSnowflake(this string id) =>
            Snowflake.TryParse(id, out var snowflake) ? snowflake.Value : new Optional<Snowflake>();
    }
}
