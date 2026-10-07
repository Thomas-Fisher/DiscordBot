# The bot and API. Build from the repository root:  docker build -f DiscordBot.Api/Dockerfile -t discordbot-api .

# ---- Build: the full SDK, thrown away once the app is published
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so this layer is reused until a dependency changes
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY DiscordBot.Domain/DiscordBot.Domain.csproj DiscordBot.Domain/
COPY DiscordBot.Application/DiscordBot.Application.csproj DiscordBot.Application/
COPY DiscordBot.Infrastructure/DiscordBot.Infrastructure.csproj DiscordBot.Infrastructure/
COPY DiscordBot.Discord/DiscordBot.Discord.csproj DiscordBot.Discord/
COPY DiscordBot.Contracts/DiscordBot.Contracts.csproj DiscordBot.Contracts/
COPY DiscordBot.Api/DiscordBot.Api.csproj DiscordBot.Api/
RUN dotnet restore DiscordBot.Api/DiscordBot.Api.csproj

COPY DiscordBot.Domain/ DiscordBot.Domain/
COPY DiscordBot.Application/ DiscordBot.Application/
COPY DiscordBot.Infrastructure/ DiscordBot.Infrastructure/
COPY DiscordBot.Discord/ DiscordBot.Discord/
COPY DiscordBot.Contracts/ DiscordBot.Contracts/
COPY DiscordBot.Api/ DiscordBot.Api/
RUN dotnet publish DiscordBot.Api/DiscordBot.Api.csproj --configuration Release --no-restore --output /app

# ---- Run: only the ASP.NET runtime and the published files, as an ordinary user
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS run
WORKDIR /app
COPY --from=build /app .

# The image listens on port 8080 by default
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "DiscordBot.Api.dll"]
