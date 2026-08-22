using Discord.Rest;
using GachaBot.Application.Content;
using GachaBot.Application.Ingestion;
using GachaBot.Application.Media;
using GachaBot.Application.Publishing;
using GachaBot.Infrastructure.Configuration;
using GachaBot.Infrastructure.Content;
using GachaBot.Infrastructure.Database;
using GachaBot.Infrastructure.Discord;
using GachaBot.Infrastructure.Media;
using GachaBot.Infrastructure.Sources;
using GachaBot.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GachaBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGachaBotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddOptions<SourcePollingOptions>()
            .Bind(configuration.GetSection(SourcePollingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        var discordOptions = services.AddOptions<DiscordOptions>()
            .Bind(configuration.GetSection(DiscordOptions.SectionName));
        if (configuration.GetValue<bool>("Discord:Enabled"))
        {
            discordOptions
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
        var mediaArchiveOptions = configuration
            .GetSection(MediaArchiveOptions.SectionName)
            .Get<MediaArchiveOptions>() ?? new MediaArchiveOptions();
        services.AddOptions<MediaArchiveOptions>()
            .Bind(configuration.GetSection(MediaArchiveOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.TotalRequestTimeoutSeconds >= options.AttemptTimeoutSeconds,
                "MediaArchive total request timeout must be at least the attempt timeout.")
            .ValidateOnStart();
        services.AddOptions<S3MediaOptions>()
            .Bind(configuration.GetSection(S3MediaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<GameBannerOptions>()
            .Bind(configuration.GetSection(GameBannerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<BrowserAutomationOptions>()
            .Bind(configuration.GetSection(BrowserAutomationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DatabaseStorageOptions>()
            .Bind(configuration.GetSection(DatabaseStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var sourceDefinitions = SourceDefinitionCatalog.FromConfiguration(configuration)
            .Select(definition => definition with
            {
                Trust = ResolveTrust(configuration, definition.Key, definition.Trust),
            })
            .ToArray();
        services.AddSingleton(new SourceMediaPublicationPolicy(sourceDefinitions.ToDictionary(
            definition => definition.Key,
            definition => definition.Trust,
            StringComparer.Ordinal)));
        services.AddSingleton<IContentRetentionPolicy>(new OfficialContentRetentionPolicy(sourceDefinitions));
        var databaseOptions = configuration.GetSection(DatabaseStorageOptions.SectionName)
            .Get<DatabaseStorageOptions>() ?? new DatabaseStorageOptions();
        services.AddSingleton<ISourceDatabaseFactory>(new SourceDatabaseFactory(
            databaseOptions.ConnectionString,
            sourceDefinitions.Select(definition => definition.Key)));
        services.AddSingleton<IGuildConfigurationDatabaseFactory>(new GuildConfigurationDatabaseFactory(
            databaseOptions.ConnectionString));
        services.AddSingleton<IGuildDestinationStore, GuildDestinationStore>();
        services.AddScoped<IGuildPublicationHistoryStore, GuildPublicationHistoryStore>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<CompositeContentStore>();
        services.AddScoped<IIngestionSink, ArchivingIngestionSink>();
        services.AddScoped<ISourceStateStore>(provider => provider.GetRequiredService<CompositeContentStore>());
        services.AddScoped<ISourceStateQuery>(provider => provider.GetRequiredService<CompositeContentStore>());
        services.AddScoped<ISourceContentLookup>(provider => provider.GetRequiredService<CompositeContentStore>());
        services.AddScoped<IContentScheduleStore>(provider => provider.GetRequiredService<CompositeContentStore>());
        services.AddScoped<IContentManagementStore>(provider => provider.GetRequiredService<CompositeContentStore>());
        services.AddScoped<IEventPublicationScheduleStore>(provider => provider.GetRequiredService<CompositeContentStore>());
        services.AddScoped<IContentDeletionService, ContentDeletionService>();
        services.AddScoped<IPublicationQueueStore, CompositePublicationQueueStore>();
        services.AddSingleton<IPublicationPreviewRenderer, DiscordPublicationPreviewRenderer>();
        services.AddSingleton(provider => new DiscordMediaMessagePlanner(
            provider.GetRequiredService<MediaAssetRegistry>(),
            provider.GetRequiredService<IMediaObjectStore>(),
            provider.GetRequiredService<GameBannerStore>(),
            provider.GetRequiredService<SourceMediaPublicationPolicy>()));
        services.AddScoped<IngestionCoordinator>();
        services.AddScoped<MediaArchiveMigration>();
        services.AddSingleton<MediaAssetRegistry>();

        services.AddSingleton<IHostAddressResolver, HostAddressResolver>();
        services.AddSingleton<MediaArchiveCatalog>();
        services.AddSingleton<IMediaObjectStore, S3MediaObjectStore>();
        services.AddSingleton<GameBannerStore>();
        services.AddScoped<GameBannerSeeder>();
        services.AddSingleton<IMediaGarbageCollector, MediaGarbageCollector>();
        services.AddHttpClient<IMediaArchive, SafeRemoteMediaArchive>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(
                    mediaArchiveOptions.AttemptTimeoutSeconds);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(
                    mediaArchiveOptions.TotalRequestTimeoutSeconds);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                    mediaArchiveOptions.AttemptTimeoutSeconds * 2L);
            });

        services.AddHttpClient("ConfiguredSources")
            .AddStandardResilienceHandler();
        services.AddHttpClient("GameBanners")
            .AddStandardResilienceHandler();
        services.AddSingleton<IRenderedPageClient, PlaywrightRenderedPageClient>();
        services.AddScoped<ISourceHandler>(provider => new JsonArticleFeedHandler(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("ConfiguredSources"),
            provider.GetRequiredService<ISourceContentLookup>()));
        services.AddScoped<ISourceHandler>(provider => new PagedHtmlArticleFeedHandler(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("ConfiguredSources"),
            provider.GetRequiredService<ISourceContentLookup>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PagedHtmlArticleFeedHandler>>()));
        services.AddScoped<ISourceHandler>(provider => new RenderedHtmlCodeHandler(
            provider.GetRequiredService<IRenderedPageClient>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ISourceHandler>(provider => new WutheringWavesTimelineHandler(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("ConfiguredSources")));
        services.AddScoped<ISourceHandler>(provider => new Game8EventCalendarHandler(
            provider.GetRequiredService<IRenderedPageClient>()));
        foreach (var configuredDefinition in sourceDefinitions)
        {
            services.AddScoped<IGameContentSource>(provider => new ConfiguredGameContentSource(
                configuredDefinition,
                new SourceHandlerResolver(provider.GetServices<ISourceHandler>())));
        }
        services.AddScoped<ISourceOperations, SourceOperations>();

        var workersEnabled = configuration.GetValue("Workers:Enabled", true);
        if (workersEnabled)
        {
            services.AddHostedService<SourcePollingWorker>();
            services.AddHostedService<ArchiveWorker>();
            services.AddHostedService<MediaGarbageCollectionWorker>();
        }

        if (workersEnabled && configuration.GetValue<bool>("Discord:Enabled"))
        {
            services.AddSingleton<DiscordRestClient>();
            services.AddSingleton<IDiscordPublisher, DiscordChannelPublisher>();
            services.AddHostedService<DiscordGuildSetupWorker>();
            services.AddHostedService<PublicationDispatcher>();
        }

        return services;
    }

    private static SourceTrust ResolveTrust(
        IConfiguration configuration,
        string sourceKey,
        SourceTrust defaultTrust) =>
        SourceTrustConfiguration.Resolve(configuration, sourceKey, defaultTrust);
}
