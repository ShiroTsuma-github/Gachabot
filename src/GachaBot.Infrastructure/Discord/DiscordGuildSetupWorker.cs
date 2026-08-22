using Discord;
using Discord.WebSocket;
using GachaBot.Application.Publishing;
using GachaBot.Domain.Games;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.Discord;

public sealed partial class DiscordGuildSetupWorker(
    IOptions<DiscordOptions> options,
    IGuildDestinationStore destinations,
    IServiceScopeFactory scopeFactory,
    ILogger<DiscordGuildSetupWorker> logger) : BackgroundService
{
    private const string ConfigureCommand = "gachabot-configure";
    private const string EnableCommand = "gachabot-enable";
    private const string DisableCommand = "gachabot-disable";
    private const string StatusCommand = "gachabot-status";
    private const string EventScheduleCommand = "gachabot-event-schedule";
    private const string WutheringWavesOption = "wuthering-waves";
    private const string NevernessToEvernessOption = "neverness-to-everness";
    private const string StartBeforeHoursOption = "start-before-hours";
    private const string EndBeforeHoursOption = "end-before-hours";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
        });
        client.Ready += () => InitializeDiscordProfileAsync(client);
        client.SlashCommandExecuted += HandleSlashCommandAsync;
        client.Log += message =>
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogDiscordGateway(logger, message);
            }
            return Task.CompletedTask;
        };

        await client.LoginAsync(TokenType.Bot, options.Value.BotToken).ConfigureAwait(false);
        await client.StartAsync().ConfigureAwait(false);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await client.StopAsync().ConfigureAwait(false);
            await client.LogoutAsync().ConfigureAwait(false);
        }
    }

    private async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        try
        {
            var configure = new SlashCommandBuilder()
                .WithName(ConfigureCommand)
                .WithDescription("Configure the channel and games posted by GachaBot")
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .AddOption("channel", ApplicationCommandOptionType.Channel,
                    "New text channel; omit it to keep the current one", isRequired: false)
                .AddOption(WutheringWavesOption, ApplicationCommandOptionType.Boolean,
                    "Post Wuthering Waves", isRequired: false)
                .AddOption(NevernessToEvernessOption, ApplicationCommandOptionType.Boolean,
                    "Post Neverness to Everness", isRequired: false)
                .Build();
            var enable = new SlashCommandBuilder()
                .WithName(EnableCommand)
                .WithDescription("Enable GachaBot publications on this server")
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .Build();
            var disable = new SlashCommandBuilder()
                .WithName(DisableCommand)
                .WithDescription("Pause GachaBot publications on this server")
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .Build();
            var status = new SlashCommandBuilder()
                .WithName(StatusCommand)
                .WithDescription("Show this server's GachaBot configuration")
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .Build();
            var eventSchedule = new SlashCommandBuilder()
                .WithName(EventScheduleCommand)
                .WithDescription("Set event posts before start and end (0 to 72 hours)")
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .AddOption(StartBeforeHoursOption, ApplicationCommandOptionType.Number,
                    "Hours before event start; 0 means exactly at start", isRequired: true)
                .AddOption(EndBeforeHoursOption, ApplicationCommandOptionType.Number,
                    "Hours before event end; 0 means exactly at end", isRequired: true)
                .Build();
            await client.BulkOverwriteGlobalApplicationCommandsAsync([configure, enable, disable, status, eventSchedule])
                .ConfigureAwait(false);
            LogCommandsRegistered(logger);
        }
        catch (Exception exception)
        {
            LogCommandRegistrationFailure(logger, exception);
        }
    }

    private async Task InitializeDiscordProfileAsync(DiscordSocketClient client)
    {
        await client.SetGameAsync(
                string.IsNullOrWhiteSpace(options.Value.ActivityName)
                    ? "Schmidley"
                    : options.Value.ActivityName.Trim(),
                type: ActivityType.Playing)
            .ConfigureAwait(false);
        await RegisterCommandsAsync(client).ConfigureAwait(false);
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.GuildId is null || command.User is not SocketGuildUser member || !member.GuildPermissions.ManageGuild)
        {
            await command.RespondAsync("This command requires **Manage Server** on a server.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            switch (command.Data.Name)
            {
                case ConfigureCommand:
                    await ConfigureAsync(command, member).ConfigureAwait(false);
                    break;
                case EnableCommand:
                    await SetEnabledAsync(command, true).ConfigureAwait(false);
                    break;
                case DisableCommand:
                    await SetEnabledAsync(command, false).ConfigureAwait(false);
                    break;
                case StatusCommand:
                    await RespondStatusAsync(command).ConfigureAwait(false);
                    break;
                case EventScheduleCommand:
                    await ConfigureEventScheduleAsync(command).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception exception)
        {
            LogCommandFailure(logger, command.Data.Name, command.GuildId.Value, exception);
            if (!command.HasResponded)
            {
                await command.RespondAsync("GachaBot could not save this configuration. Check the bot permissions and try again.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ConfigureAsync(SocketSlashCommand command, SocketGuildUser member)
    {
        var current = (await destinations.ListAsync(CancellationToken.None).ConfigureAwait(false))
            .SingleOrDefault(item => item.GuildId == command.GuildId!.Value);
        var specifiedChannel = command.Data.Options.SingleOrDefault(option => option.Name == "channel")?.Value as SocketTextChannel;
        if (specifiedChannel is not null && specifiedChannel.Guild.Id != command.GuildId)
        {
            await command.RespondAsync("Choose a text channel from this server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (specifiedChannel is null && current is null)
        {
            await command.RespondAsync(
                "Choose a text channel the first time, for example `/gachabot-configure channel:#updates`.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (specifiedChannel is not null)
        {
            var permissions = specifiedChannel.Guild.CurrentUser.GetPermissions(specifiedChannel);
            if (!permissions.ViewChannel ||
                !permissions.SendMessages ||
                !permissions.EmbedLinks ||
                !permissions.AttachFiles ||
                !permissions.ReadMessageHistory)
            {
                await command.RespondAsync(
                    "I need **View Channel**, **Send Messages**, **Embed Links**, **Attach Files**, and **Read Message History** in that channel.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }
        }

        var games = ReadGames(command) ?? current?.Games ?? GuildDestinationGames.All;
        if (games.Count == 0)
        {
            await command.RespondAsync(
                $"Choose at least one game, or use `/{DisableCommand}` to pause all publications.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var channelId = specifiedChannel?.Id ?? current!.ChannelId;
        await destinations.ConfigureAsync(
                member.Guild.Id,
                member.Guild.Name,
                channelId,
                specifiedChannel?.Name ?? current!.ChannelName ?? channelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                member.Id,
                games,
                CancellationToken.None)
            .ConfigureAwait(false);
        await ReconcileEventPublicationsAsync(member.Guild.Id).ConfigureAwait(false);
        var channelText = specifiedChannel?.Mention ?? $"<#{channelId}>";
        await command.RespondAsync(
            $"GachaBot will publish **{FormatGames(games)}** in {channelText}. Use `/{DisableCommand}` to pause it.",
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task SetEnabledAsync(SocketSlashCommand command, bool enabled)
    {
        var configured = (await destinations.ListAsync(CancellationToken.None).ConfigureAwait(false))
            .Any(item => item.GuildId == command.GuildId!.Value);
        if (!configured)
        {
            await command.RespondAsync(
                $"This server is not configured. Use `/{ConfigureCommand}` and choose a text channel first.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        await destinations.SetEnabledAsync(command.GuildId!.Value, enabled, CancellationToken.None).ConfigureAwait(false);
        if (enabled)
        {
            await ReconcileEventPublicationsAsync(command.GuildId.Value).ConfigureAwait(false);
        }
        await command.RespondAsync(
            enabled
                ? $"GachaBot publications are enabled. Use `/{ConfigureCommand}` to change channel or games."
                : $"GachaBot publications are paused. Pending posts that no longer match this server are cancelled.",
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task RespondStatusAsync(SocketSlashCommand command)
    {
        var destination = (await destinations.ListAsync(CancellationToken.None).ConfigureAwait(false))
            .SingleOrDefault(item => item.GuildId == command.GuildId!.Value);
        var response = destination is null
            ? $"This server is not configured. Use `/{ConfigureCommand}` and choose a text channel."
            : $"**GachaBot configuration**\n" +
              $"**Channel:** <#{destination.ChannelId}>\n" +
              $"**Games:** {FormatGames(destination.Games)}\n" +
              $"**Status:** {(destination.IsEnabled ? "enabled" : "paused")}\n" +
              $"**Event start post:** {FormatStartOffset(destination.EventStartOffsetHours)}\n" +
              $"**Ending reminder:** {FormatEndOffset(destination.EventEndOffsetHours)}";
        await command.RespondAsync(response, ephemeral: true).ConfigureAwait(false);
    }

    private async Task ConfigureEventScheduleAsync(SocketSlashCommand command)
    {
        var guildId = command.GuildId ?? throw new InvalidOperationException("Event schedules require a server.");
        var destination = (await destinations.ListAsync(CancellationToken.None).ConfigureAwait(false))
            .SingleOrDefault(item => item.GuildId == guildId);
        if (destination is null)
        {
            await command.RespondAsync(
                $"This server is not configured. Use `/{ConfigureCommand}` and choose a text channel first.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!TryReadOffset(command, StartBeforeHoursOption, out var startOffsetHours) ||
            !TryReadOffset(command, EndBeforeHoursOption, out var endOffsetHours))
        {
            await command.RespondAsync(
                "Both offsets must be numbers from **0** to **72** hours. Decimals are supported, for example `0.5`.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        await destinations.SetEventNotificationOffsetsAsync(
                guildId,
                startOffsetHours,
                endOffsetHours,
                CancellationToken.None)
            .ConfigureAwait(false);
        await ReconcileEventPublicationsAsync(guildId).ConfigureAwait(false);
        await command.RespondAsync(
            $"**Event timing saved**\n" +
            $"**Event start post:** {FormatStartOffset(startOffsetHours)}\n" +
            $"**Ending reminder:** {FormatEndOffset(endOffsetHours)}\n" +
            "Pending event posts were recalculated.",
            ephemeral: true).ConfigureAwait(false);
    }

    private static HashSet<GameKey>? ReadGames(SocketSlashCommand command)
    {
        var gameOptions = command.Data.Options
            .Where(option => option.Name is WutheringWavesOption or NevernessToEvernessOption)
            .ToArray();
        if (gameOptions.Length == 0)
        {
            return null;
        }

        var games = new HashSet<GameKey>();
        if (ReadBooleanOption(gameOptions, WutheringWavesOption))
        {
            games.Add(GameKey.WutheringWaves);
        }

        if (ReadBooleanOption(gameOptions, NevernessToEvernessOption))
        {
            games.Add(GameKey.NevernessToEverness);
        }

        return games;
    }

    private async Task ReconcileEventPublicationsAsync(ulong guildId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<IEventPublicationScheduleStore>();
        await schedule.ReconcileForGuildAsync(guildId, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool ReadBooleanOption(
        IReadOnlyCollection<SocketSlashCommandDataOption> options,
        string name) => options.SingleOrDefault(option => option.Name == name)?.Value is true;

    private static bool TryReadOffset(SocketSlashCommand command, string name, out double value)
    {
        value = 0;
        var raw = command.Data.Options.SingleOrDefault(option => option.Name == name)?.Value;
        if (raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }

        return !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0 and <= 72;
    }

    private static string FormatStartOffset(double value) => value == 0
        ? "at event start"
        : $"{FormatHours(value)} before event start";

    private static string FormatEndOffset(double value) => value == 0
        ? "at event end"
        : $"{FormatHours(value)} before event end";

    private static string FormatHours(double value) =>
        $"{value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} h";

    private static string FormatGames(IEnumerable<GameKey> games) => string.Join(
        " + ",
        games.OrderBy(game => game).Select(game => game switch
        {
            GameKey.WutheringWaves => "Wuthering Waves",
            GameKey.NevernessToEverness => "Neverness to Everness",
            _ => game.ToString(),
        }));

    [LoggerMessage(EventId = 2201, Level = LogLevel.Information, Message = "Discord application commands registered.")]
    private static partial void LogCommandsRegistered(ILogger logger);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Could not register Discord application commands.")]
    private static partial void LogCommandRegistrationFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "Discord command {Command} failed for guild {GuildId}.")]
    private static partial void LogCommandFailure(ILogger logger, string command, ulong guildId, Exception exception);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Debug, Message = "Discord gateway: {Message}")]
    private static partial void LogDiscordGateway(ILogger logger, LogMessage message);
}
