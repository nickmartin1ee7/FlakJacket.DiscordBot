FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["FlakJacket.DiscordBot.WorkerService/FlakJacket.DiscordBot.WorkerService.csproj", "FlakJacket.DiscordBot.WorkerService/"]
RUN dotnet restore "FlakJacket.DiscordBot.WorkerService/FlakJacket.DiscordBot.WorkerService.csproj"
COPY . .
WORKDIR "/src/FlakJacket.DiscordBot.WorkerService"
RUN dotnet build "FlakJacket.DiscordBot.WorkerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FlakJacket.DiscordBot.WorkerService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlakJacket.DiscordBot.WorkerService.dll"]