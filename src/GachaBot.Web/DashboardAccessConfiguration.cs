namespace GachaBot.Web;

public static class DashboardAccessConfiguration
{
    public static void Validate(
        bool developmentAccess,
        bool oauthConfigured,
        IReadOnlyCollection<string> administratorIds)
    {
        ArgumentNullException.ThrowIfNull(administratorIds);
        if (developmentAccess)
        {
            return;
        }

        if (!oauthConfigured)
        {
            throw new InvalidOperationException(
                "Discord OAuth must be configured outside anonymous development mode. " +
                "For local Rider runs select the 'GachaBot.Web: Development' launch profile, " +
                "or set ASPNETCORE_ENVIRONMENT=Development in the run configuration.");
        }

    }
}
