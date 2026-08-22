using System.Security.Claims;
using GachaBot.Application.Publishing;
using Microsoft.AspNetCore.Authorization;

namespace GachaBot.Web;

public sealed class DashboardAdministratorRequirement : IAuthorizationRequirement;

public sealed class DashboardAdministratorAuthorizationHandler(
    IGuildDestinationStore destinations,
    IConfiguration configuration,
    IHostEnvironment environment)
    : AuthorizationHandler<DashboardAdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DashboardAdministratorRequirement requirement)
    {
        if (environment.IsDevelopment() && configuration.GetValue("Dashboard:AllowAnonymousInDevelopment", true) ||
            environment.IsEnvironment("Testing"))
        {
            context.Succeed(requirement);
            return;
        }

        var identity = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ulong.TryParse(identity, out var userId) || userId == 0)
        {
            return;
        }

        var legacyAdministrators = configuration
            .GetSection("Discord:AdministratorUserIds")
            .Get<ulong[]>() ?? [];
        if (legacyAdministrators.Contains(userId) ||
            await destinations.IsAdministratorAsync(userId, CancellationToken.None).ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
    }
}
