using Remora.Discord.API.Abstractions.Objects;

namespace FlakJacket.DiscordBot.WorkerService.Extensions
{
    public static class UserExtensions
    {
        public static string ToFullUsername(this IUser user) => $"{user.Username}#{user.Discriminator}";
    }
}
