FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["DiscordBot.ShardManager.WebApi/DiscordBot.ShardManager.WebApi.csproj", "DiscordBot.ShardManager.WebApi/"]
RUN dotnet restore "DiscordBot.ShardManager.WebApi/DiscordBot.ShardManager.WebApi.csproj"
COPY . .
WORKDIR "/src/DiscordBot.ShardManager.WebApi"
RUN dotnet build "DiscordBot.ShardManager.WebApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DiscordBot.ShardManager.WebApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DiscordBot.ShardManager.WebApi.dll"]