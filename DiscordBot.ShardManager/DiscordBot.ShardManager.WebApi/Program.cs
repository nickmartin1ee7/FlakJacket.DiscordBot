using DiscordBot.ShardManager.Models;
using DiscordBot.ShardManager.WebApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Configuration.AddEnvironmentVariables();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

var shardManagerOptions = app.Configuration
    .GetSection(nameof(ShardManagerOptions))
    .Get<ShardManagerOptions>();

var shardManager = new ShardManager(shardManagerOptions.MaxShards, shardManagerOptions.InternalShards);

app.MapGet("/requestShardGroup", () =>
    {
        try
        {
            return Results.Ok(shardManager.RequestShardGroup());
        }
        catch (OutOfAvailableShardsException)
        {
            return Results.Conflict();
        }
    })
.WithName("GetRequestShardGroup");

app.MapGet("/shardGroups", () => shardManager.GetShardGroups())
    .WithName("GetShardGroups");

app.MapGet("/unassignShardGroup", (int groupId) => shardManager.UnassignShardGroup(groupId))
    .WithName("GetUnassignShardGroup");

app.MapPost("/unassignAllShardGroups", () => shardManager.UnassignAllShardGroups())
    .WithName("PostUnassignAllShardGroups");

app.MapGet("/maxShards", () => shardManager.GetMaxShards())
    .WithName("GetMaxShards");

app.MapPost("/maxShards", (int newMaxShardCount) => shardManager.SetMaxShards(newMaxShardCount))
    .WithName("SetMaxShards");

app.MapGet("/internalShards", () => shardManager.GetInternalShards())
    .WithName("GetInternalShards");

app.MapPost("/internalShards", (int newInternalShardCount) => shardManager.SetInternalShards(newInternalShardCount))
    .WithName("SetInternalShards");

app.Run();