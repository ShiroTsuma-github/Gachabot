# Signal Desk

Signal Desk is a self-hosted Discord bot and dashboard for publishing updates and in-game events from gacha games. It currently supports Wuthering Waves and Neverness to Everness, keeps a publication history for every guild, and lets each guild choose its own channel, games, and event reminders.

The project is not affiliated with Discord, Kuro Games, Hotta Studio, Game8, or WuWa Tracker.

## What it does

- imports configured official announcements and event calendars;
- publishes posts independently to each configured Discord guild;
- shows imported content, scheduled posts, sent messages, sources, and guild configuration in a web dashboard;
- deduplicates source updates, retains revisions, and retries failed Discord deliveries;
- supports per-guild start and end-event reminder offsets;
- stores data in PostgreSQL and media in an S3-compatible bucket.

## Requirements

- Docker Engine with Docker Compose, recommended for a permanent deployment;
- PostgreSQL available to the bot;
- an S3-compatible object store such as Garage or MinIO, with a bucket for media;
- a Discord application with a bot and OAuth2 credentials;
- a public HTTPS address if the dashboard is used outside localhost.

To run from source instead, install .NET SDK 10.0.302 (or a compatible 10.0.x SDK). The rendered Game8 sources also require Chromium through Playwright.

## Start with Docker Compose

The compose file starts Signal Desk only. PostgreSQL and S3 storage are deliberately external services, so provide their addresses in `.env`.

```powershell
Copy-Item .env.example .env
# Edit .env and replace every replace-with-* value.
docker compose up --build -d
docker compose logs -f gachabot
```

The dashboard is then available on `http://localhost:8791` and the health endpoint on `http://localhost:8791/health`.

Keep the named `gachabot-data` volume: it contains ASP.NET data-protection keys and the browser profile. Back it up together with PostgreSQL and the S3 bucket.

### Required configuration

`.env.example` documents every setting. At minimum, configure the following values before a production start:

| Setting | Purpose |
| --- | --- |
| `DatabaseStorage__ConnectionString` | PostgreSQL connection string for all content, guild settings, and publication state. |
| `S3Media__Endpoint`, `__AccessKey`, `__SecretKey` | Address and credentials of the private S3-compatible object store. |
| `S3Media__Bucket` | Existing bucket used for stored media, e.g. `gachabot-media`. |
| `Discord__BotToken` | Bot token from the Discord Developer Portal. |
| `Discord__OAuth__ClientId`, `Discord__OAuth__ClientSecret` | OAuth2 credentials used to sign in to the dashboard. |

Use a dedicated database user and a dedicated S3 access key restricted to this bucket. Never commit `.env`, database backups, or data-protection keys. Put the dashboard behind an HTTPS reverse proxy when it is public, and set the matching public OAuth redirect URL.

## Create and connect a Discord application

1. In the [Discord Developer Portal](https://discord.com/developers/applications), create an application and add a bot.
2. Copy its bot token to `Discord__BotToken` and keep `Discord__Enabled=true`.
3. In **OAuth2**, add the redirect URL `https://your-domain/signin-discord`. For a localhost-only setup, also add `http://localhost:8791/signin-discord`.
4. Copy the OAuth client ID and client secret into `.env`.
5. Generate an install URL with the `bot` and `applications.commands` scopes, then add the bot to your test guild.
6. Give the bot permissions to view the target channel, send messages, embed links, attach files, and read message history. The person running setup needs Discord's **Manage Server** permission; the bot itself does not need it.

Run `/gachabot-configure` in Discord before opening the dashboard. The person who runs it receives dashboard access; another administrator can run the command again (without changing the channel) to register their own access. `GuildId`, `ChannelId`, and `AdministratorUserIds` are legacy migration settings and are not needed for a new installation.

Set `Discord__ActivityName=Schmidley` if you want to override the default Discord activity text.

## Configure a guild

Run these slash commands in each guild. They are intentionally restricted to members with **Manage Server**.

```text
/gachabot-configure channel:#updates wuthering-waves:true neverness-to-everness:true
/gachabot-event-schedule start-before-hours:0 end-before-hours:48
/gachabot-status
```

`start-before-hours` and `end-before-hours` accept values from `0` to `72`, including fractions such as `0.5`. The defaults publish an event at its start and send an end reminder 48 hours before it finishes. Use `/gachabot-disable` to pause a guild or `/gachabot-enable` to resume it.

When a guild is newly configured or re-enabled, currently active events that have not already been published for that guild are queued. Each guild has separate delivery and retry state, so a failure on one server cannot block another.

## Dashboard and sources

The dashboard is the operational view: use **Guilds** to inspect channels, selected games, queued posts, and delivered messages; use **Events** for the calendar and source filtering; use **Content** to inspect imported blocks and manual posts.

The source registry is [`src/GachaBot.Web/source-definitions.json`](src/GachaBot.Web/source-definitions.json). It contains the source address, parser, and field rules. Enable or disable a source operationally through the `Sources` section of `appsettings.json` or environment variables. Check each source's terms and access rules before enabling it in your own deployment. More source details are in [docs/sources.md](docs/sources.md); architecture and operations are in [docs/architecture.md](docs/architecture.md) and [docs/operations.md](docs/operations.md).

The first scan of official announcement sources is treated as a baseline and does not flood a new channel with old announcements. Event timelines are handled separately and can schedule active or upcoming events according to each guild's configuration.

## Run from source

Configure PostgreSQL and S3 values exactly as for Docker, then run:

```powershell
dotnet restore GachaBot.slnx --configfile NuGet.Config
dotnet build GachaBot.slnx --no-restore
dotnet test GachaBot.slnx --no-build --no-restore

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Discord__Enabled = 'false'
$env:Workers__Enabled = 'false'
dotnet run --no-build --no-restore --project src/GachaBot.Web
```

The development profile allows anonymous dashboard access by default and disables automatic workers. Imported sources can still be refreshed manually in the dashboard. To enable Game8 sources locally after the first build, install the pinned Playwright browser:

```powershell
& 'src/GachaBot.Web/bin/Debug/net10.0/playwright.ps1' install chromium
```

## License

The project is source-available under the [LICENSE](LICENSE). Individuals may run an unmodified instance for personal, non-commercial use; redistribution, commercial use, and distributing modified versions require written permission from ShiroTsuma.
