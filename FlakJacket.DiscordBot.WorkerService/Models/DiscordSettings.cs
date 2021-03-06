namespace FlakJacket.DiscordBot.WorkerService.Models;

public class DiscordSettings
{
    public ulong? DebugServerId { get; set; }
    public string Status { get; set; }
    public string Token { get; set; }
    public string ShardManagerUri { get; set; }
    public string SetupChannelName { get; set; }
    public string SetupChannelDescription { get; set; }
    public string UpdateDelay { get; set; }
    public int MaxBroadcastPosts { get; set; }
    public string SourceUri { get; set; }
    public string AdminSnowflake { get; set; }
}