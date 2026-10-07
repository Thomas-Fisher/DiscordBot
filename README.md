# Discord Bot + Admin Dashboard

A multi-guild Discord bot (scheduled events with class/spec signups, message and activity logging) with an ASP.NET Core API and a Blazor WebAssembly admin dashboard. The bot and the API run in one process.

## Quick start

1. Create the Discord application and invite the bot ([section 2](#2-create-the-discord-application)).
2. Copy `.env.example` to `.env` and fill in the four values ([section 3](#3-configure)).
3. Start PostgreSQL and Kafka: `docker compose up -d`
4. Run the bot and API: `dotnet run --project DiscordBot.Api --urls http://localhost:5001`
5. Run the dashboard, in a second terminal: `dotnet watch run --project DiscordBot.Web`, then open http://localhost:5000.

## 1. Requirements

- **.NET 10 SDK** (`dotnet --version` should show 10.0.x)
- **Docker** (Docker Desktop, or Docker Engine with the Compose plugin), running. It provides PostgreSQL and Kafka, so you do not install them or the `dotnet ef` tool; migrations are applied when the API starts.
- A **Discord account** that can create applications and manage a test server.

## 2. Create the Discord application

Everything is at https://discord.com/developers/applications. Click **New Application**, then:

**Bot token** (*Bot* tab): click **Reset Token** and copy it straight away; Discord shows it only once. Under **Privileged Gateway Intents** switch **Message Content Intent** and **Server Members Intent** on, then **Save Changes**. If either is off Discord refuses the connection and the log says which one.

**Client ID and secret** (*OAuth2* tab): copy the **Client ID**, click **Reset Secret** and copy it (also shown once). Under **Redirects** add exactly `http://localhost:5000/callback` and save. It must match `Discord:RedirectUri` in `DiscordBot.Api/appsettings.json`; add your own address too if you host the dashboard elsewhere.

**Invite the bot** (*OAuth2 > URL Generator*): tick the scopes `bot` and `applications.commands`, and the permissions *View Channels, Send Messages, Embed Links, Add Reactions, Read Message History, Manage Messages, Use External Emojis, Mention Everyone, View Audit Log*. (*Mention Everyone* lets the bot ping any role chosen for an event; *View Audit Log* is optional and lets the activity log say who deleted a message.) Open the generated URL and authorise it for a server where you have *Manage Server*.

## 3. Configure

Everything you fill in lives in one git-ignored file, `.env`, in the repository root. Create it from the template and replace the `REPLACE-WITH-...` placeholders:

```bash
cp .env.example .env        # Windows PowerShell: Copy-Item .env.example .env
```

| Setting | Where it comes from |
|---|---|
| `Discord__BotToken` | The bot token |
| `Discord__ClientId`, `Discord__ClientSecret` | The OAuth2 page |
| `Jwt__Secret` | You invent it: any random string of 32+ characters. It signs the dashboard's login tokens. |

`dotnet run` reads the file in Development (the default launch profile) and `docker compose` passes it to the containers. A variable already set in your environment wins over the file. If a value is missing or still a placeholder, the API stops at start-up and names it. The dashboard needs nothing of its own: it asks the API which Discord application to use. Its only setting is `ApiUrl` in `DiscordBot.Web/wwwroot/appsettings.json`.

Other settings have defaults in `DiscordBot.Api/appsettings.json` and can be overridden as environment variables (double underscore for nesting, for example `Discord__CommandScope`):

- `Discord:Enabled`: `false` runs the API without connecting the bot to Discord.
- `Discord:CommandScope`: `Global` (default) registers slash commands everywhere, and Discord can take up to an hour to show changes; `Guild` shows them at once, which suits development.
- `Retention`, `EventReminders`, `Recurrence`, `RateLimiting`, `Cors` and `ForwardedHeaders`: limits and timings; `Cors:AllowedOrigins` must list the address the dashboard is served from, and `ForwardedHeaders` should be enabled when the API runs behind a reverse proxy.
- `EventBus:Kafka`: broker address, topic prefix, partitions and retry settings. Reactions and activity-log entries go through an outbox in the database before they are published, so a broker that is down delays a signup instead of losing it.
- `Serilog`: log levels. `OpenTelemetry:Endpoint` is where traces and metrics are sent; set it empty to switch that off.

Times are stored in UTC. Each server has a time zone (UTC until changed, with `/timezone` or the dashboard's Settings page): people type event times in it, the dashboard shows times in it, and recurring events keep the same clock time through daylight saving changes.

## 4. Start PostgreSQL and Kafka

```bash
docker compose up -d
docker compose ps
```

This starts PostgreSQL (`localhost:5432`, user `discord_bot`, password `discord_bot_dev`), Kafka (`localhost:9092`), Kafka UI (http://localhost:8080, for looking at topics and the dead-letter topic) and the Aspire telemetry dashboard (http://localhost:18888, for traces, metrics and logs; no sign-in, local use only). `postgres` and `kafka` should show `(healthy)`; Kafka takes 20-30 seconds on first start. Topics are created when the app first starts.

`docker compose stop` stops them, `docker compose down` removes the containers and keeps the data, and `docker compose down -v` also **deletes the database and Kafka data**.

## 5. Run

**Bot and API:**

```bash
dotnet run --project DiscordBot.Api --urls http://localhost:5001
```

On first start it applies the migrations, connects the bot, and registers the slash commands in every server the bot is in. Swagger is at http://localhost:5001/swagger in Development, and http://localhost:5001/health reports the database, Kafka and Discord connection (`/health/live` only says the process is up).

**Dashboard**, in a second terminal:

```bash
dotnet watch run --project DiscordBot.Web
```

Open http://localhost:5000 and sign in with Discord. You can manage any server where you are the owner or have the *Administrator* or *Manage Server* permission, and where the bot is present.

**Or run everything in containers** with the same `.env` file:

```bash
docker compose --profile app up -d --build
```

This serves the backend on http://localhost:5001 and the dashboard on http://localhost:5000. It uses the same ports as the commands above, so stop any `dotnet run` first. If a value is missing, `docker compose logs backend` names it.

## 6. Check that it works

In your Discord server, `/ping` should answer and `/help` lists the commands: `/timezone`, `/createevent`, `/createrecurringevent`, `/editevent`, `/moveevent`, `/moveeventhere`, `/cancelevent`, `/cancelrecurringevent`, `/deleteevent`, `/listevents`, `/ping`, `/about`. Slash commands can take a minute to appear; if they do not, restart Discord (Ctrl+R).

- `/createevent` opens a form (title, date and time, description), then offers **Add duration & roles** or **Create event**. The bot posts the event message and pings the chosen roles. React with a class emote and pick a spec in the DM to sign up.
- An event can have a **reminder**: that many minutes before the start, the bot pings members of the event's roles who have not responded.
- On the dashboard an event's page lets you edit it, **Cancel Event** (one-off events; it stays on record), **Delete Event** (it disappears from the dashboard and its announcement is removed from Discord) or **Cancel Series** (recurring events), **Add player**, change anyone's status, class or spec, build the **raid roster** by dragging people into groups of five (WoW events), and **Ping people** (the roster, standby, or those who have not responded).

## 7. Class and spec emotes

Event messages use one custom emote per WoW class and spec. They are **application emotes** belonging to your Discord application, and the bot creates them itself: each time it connects it uploads any that are missing from the images in `DiscordBot.Discord/Icons`, so there is nothing to set up. The first start uploads about 45 images and takes a minute or two; until then new event messages show plain class names and WoW events have no class reactions to sign up with. Generic events do not use the emotes. The dashboard shows the same icons.

## 8. Tests

```bash
dotnet test
```

The tests run against PostgreSQL and Kafka. They start both in containers themselves (Testcontainers), so **Docker has to be running** but nothing else needs starting. The first run downloads the images. Each test gets its own database or topic prefix, so your development data is never touched. CI (`.github/workflows/ci.yml`) runs the same tests.

## 9. Troubleshooting

| Problem | Likely cause and fix |
|---|---|
| The API stops at start-up with an `OptionsValidationException` | A setting is missing or still a placeholder; the message names each one. See section 3. |
| `Jwt:Secret must be at least 32 bytes` or `Jwt:Secret is still ...` | Set a `Jwt__Secret` of 32 or more characters in `.env`. A `REPLACE-WITH...` placeholder is always rejected. |
| `Improper token has been passed` | The bot token is wrong or was reset. Reset it again and paste the new one. |
| `password authentication failed` / cannot connect to the database | Docker is not running or the `postgres` container is not healthy (`docker compose ps`), or something else is using port 5432. |
| A container will not start | Another program is using its port (5432, 9092, 8080, 18888 or 4317). Stop it or change the port in `docker-compose.yml`. |
| `Kafka at localhost:9092 is not ready; retrying` | Kafka is still starting. Wait for `kafka` to be `healthy`; the app keeps retrying and events wait until it is up. |
| Messages or reactions are not processed | Check http://localhost:5001/health (the `eventBus` entry) and the `discordbot.dead-letter.v1` topic in Kafka UI; failing events are moved there with the reason in the `error` header. |
| Bot is online but ignores messages | Switch on **Message Content Intent** (section 2) and restart. |
| No class reactions on a WoW event | The bot is still uploading its emotes on first start (the log says `Uploading ... class and spec emotes`), or an upload failed (look for `Failed to upload the emote`). Restart the bot to retry. |
| Slash commands missing | Wait a minute, restart Discord, and check the bot was invited with the `applications.commands` scope. |
| Login fails with `invalid redirect_uri` | The redirect in the Discord portal must be exactly `http://localhost:5000/callback` and match `Discord:RedirectUri`. |
| "The sign-in could not be started" | The dashboard cannot reach the API. Check it is running and that `ApiUrl` in `DiscordBot.Web/wwwroot/appsettings.json` points at it. |
| Dashboard shows no servers | You must be owner/Administrator/Manage Server in a server the bot has joined. |
| Login works then immediately logs out | The dashboard and API must be on the same site (both `localhost` in development). |

## 10. Solution layout

| Project | Role |
|---|---|
| `DiscordBot.Domain` | Entities, business rules and domain events. No dependencies. |
| `DiscordBot.Application` | Use cases and the repository and infrastructure interfaces. |
| `DiscordBot.Infrastructure` | EF Core / PostgreSQL repositories, the outbox and its relay, the Kafka event bus, background workers, JWT and refresh tokens. |
| `DiscordBot.Discord` | Discord.Net adapter: slash commands, gateway listeners, event message embeds and reminders. |
| `DiscordBot.Contracts` | Request/response DTOs shared by the API and the dashboard. |
| `DiscordBot.Api` | HTTP API, auth and the composition root (`Program.cs`). |
| `DiscordBot.Web` | Blazor WebAssembly dashboard. |
| `DiscordBot.Tests` | Unit, controller, and PostgreSQL / Kafka integration tests. |

Dependencies point inwards: Domain <- Application <- Infrastructure / Discord <- Api.

## License

MIT, see `LICENSE`.
