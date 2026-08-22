using GachaBot.Web;

namespace GachaBot.ArchitectureTests;

public sealed class DashboardAccessConfigurationTests
{
    [Fact]
    public void Validate_WhenOAuthIsMissingOutsideDevelopment_ExplainsHowToSelectRiderProfile()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DashboardAccessConfiguration.Validate(
                developmentAccess: false,
                oauthConfigured: false,
                administratorIds: ["123"]));

        Assert.Contains("GachaBot.Web: Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Development", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenDevelopmentAccessIsEnabled_DoesNotRequireOAuth()
    {
        var exception = Record.Exception(() =>
            DashboardAccessConfiguration.Validate(
                developmentAccess: true,
                oauthConfigured: false,
                administratorIds: []));

        Assert.Null(exception);
    }
}
