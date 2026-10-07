# The dashboard: built to static files and served by nginx. Build from the repository root:
#   docker build -f DiscordBot.Web/Dockerfile -t discordbot-dashboard .

# ---- Build: the full SDK, thrown away once the site is published
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY DiscordBot.Domain/DiscordBot.Domain.csproj DiscordBot.Domain/
COPY DiscordBot.Contracts/DiscordBot.Contracts.csproj DiscordBot.Contracts/
COPY DiscordBot.Web/DiscordBot.Web.csproj DiscordBot.Web/
RUN dotnet restore DiscordBot.Web/DiscordBot.Web.csproj

COPY DiscordBot.Domain/ DiscordBot.Domain/
COPY DiscordBot.Contracts/ DiscordBot.Contracts/
COPY DiscordBot.Web/ DiscordBot.Web/
RUN dotnet publish DiscordBot.Web/DiscordBot.Web.csproj --configuration Release --no-restore --output /site

# ---- Run: nginx serving the files. The unprivileged image runs as a normal user on port 8080.
FROM nginxinc/nginx-unprivileged:1.28-alpine AS run
COPY DiscordBot.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --chown=nginx:nginx --from=build /site/wwwroot /usr/share/nginx/html

# Writes the API address into appsettings.json from the environment when the container starts,
# so one image works for any address (the files are static, so this is the only place the setting can change)
COPY --chmod=755 DiscordBot.Web/docker-entrypoint.d/40-dashboard-settings.sh /docker-entrypoint.d/40-dashboard-settings.sh

EXPOSE 8080
