using GachaBot.Infrastructure.Media;
using GachaBot.Infrastructure.Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.IntegrationTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddGachaBotInfrastructure_ConfiguresMediaArchiveForSlowCdnResponses()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DatabaseStorage:ConnectionString"] = "Host=test;Database=test;Username=test;Password=test",
            ["SourceDefinitions:0:Key"] = "test-source",
            ["SourceDefinitions:0:Game"] = "NevernessToEverness",
            ["SourceDefinitions:0:Trust"] = "Official",
            ["SourceDefinitions:0:Handler"] = "paged-html-article-feed",
            ["SourceDefinitions:0:Url"] = "https://example.com/index.html",
            ["SourceDefinitions:0:HtmlArticle:ItemSelector"] = "a",
            ["SourceDefinitions:0:HtmlArticle:ExternalIdPattern"] = @"/(\d+)\.html$",
            ["SourceDefinitions:0:HtmlArticle:TitleSelector"] = ".title",
            ["SourceDefinitions:0:HtmlArticle:DetailContentSelector"] = "main",
            ["SourceDefinitions:0:HtmlArticle:PaginationUrlTemplate"] = "https://example.com/index{page}.html",
            ["MediaArchive:AttemptTimeoutSeconds"] = "75",
            ["MediaArchive:TotalRequestTimeoutSeconds"] = "240",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();

        services.AddGachaBotInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var resilienceOptions = provider
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get("IMediaArchive-standard");
        Assert.Equal(TimeSpan.FromSeconds(75), resilienceOptions.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(240), resilienceOptions.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(150), resilienceOptions.CircuitBreaker.SamplingDuration);
        Assert.True(provider.GetRequiredService<SourceMediaPublicationPolicy>()
            .UsesRemoteImages("test-source"));
    }
}
