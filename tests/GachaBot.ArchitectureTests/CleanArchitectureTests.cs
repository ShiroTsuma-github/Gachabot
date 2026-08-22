using System.Reflection;
using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;
using GachaBot.Infrastructure.Database;

namespace GachaBot.ArchitectureTests;

public sealed class CleanArchitectureTests
{
    [Fact]
    public void Domain_HasNoReferencesToOuterLayers()
    {
        var references = ReferenceNames(typeof(ContentItem).Assembly);

        Assert.DoesNotContain("GachaBot.Application", references);
        Assert.DoesNotContain("GachaBot.Infrastructure", references);
        Assert.DoesNotContain("GachaBot.Web", references);
    }

    [Fact]
    public void Application_HasNoReferencesToInfrastructureOrWeb()
    {
        var references = ReferenceNames(typeof(IngestionCoordinator).Assembly);

        Assert.DoesNotContain("GachaBot.Infrastructure", references);
        Assert.DoesNotContain("GachaBot.Web", references);
    }

    [Fact]
    public void Infrastructure_HasNoReferenceToWeb()
    {
        var references = ReferenceNames(typeof(AppDbContext).Assembly);

        Assert.DoesNotContain("GachaBot.Web", references);
    }

    private static string[] ReferenceNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
}
