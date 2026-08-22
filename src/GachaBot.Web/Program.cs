using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using GachaBot.Infrastructure;
using GachaBot.Infrastructure.Database;
using GachaBot.Web;
using GachaBot.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var mediaMigrationRequested = args.Contains("--migrate-media", StringComparer.Ordinal);
var mediaGarbageCollectionRequested = args.Contains("--collect-media-garbage", StringComparer.Ordinal);
var gameBannerSeedRequested = args.Contains("--seed-game-banners", StringComparer.Ordinal);
var databaseInitializationRequested = args.Contains("--initialize-database", StringComparer.Ordinal);
var databaseDiagnosticsRequested = args.Contains("--diagnose-database", StringComparer.Ordinal);
var maintenanceRequested = mediaMigrationRequested ||
    mediaGarbageCollectionRequested ||
    gameBannerSeedRequested ||
    databaseInitializationRequested ||
    databaseDiagnosticsRequested;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("source-definitions.json", optional: false, reloadOnChange: false);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
}
else
{
    var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
    Directory.CreateDirectory(dataDirectory);
    var keyDirectory = Path.Combine(dataDirectory, "keys");
    Directory.CreateDirectory(keyDirectory);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
        .SetApplicationName("GachaBot.Dashboard");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddGachaBotInfrastructure(builder.Configuration);
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    DashboardAdministratorAuthorizationHandler>();

var discordClientId = builder.Configuration["Discord:OAuth:ClientId"];
var discordClientSecret = builder.Configuration["Discord:OAuth:ClientSecret"];
var oauthConfigured = !string.IsNullOrWhiteSpace(discordClientId) &&
    !string.IsNullOrWhiteSpace(discordClientSecret);
var developmentAccess = builder.Environment.IsDevelopment() &&
    builder.Configuration.GetValue("Dashboard:AllowAnonymousInDevelopment", true) ||
    builder.Environment.IsEnvironment("Testing");

var authentication = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "GachaBot.Dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.LoginPath = "/auth/login";
    });
if (oauthConfigured)
{
    authentication.AddDiscord(DiscordAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = discordClientId!;
        options.ClientSecret = discordClientSecret!;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Scope.Add("identify");
    });
}

var administratorIds = builder.Configuration
    .GetSection("Discord:AdministratorUserIds")
    .Get<string[]>() ?? [];
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new DashboardAdministratorRequirement());
    });
});

if (!maintenanceRequested)
{
    DashboardAccessConfiguration.Validate(developmentAccess, oauthConfigured, administratorIds);
}

var app = builder.Build();

if (maintenanceRequested)
{
    var apply = args.Contains("--apply", StringComparer.Ordinal);
    var deleteLegacy = args.Contains("--delete-legacy", StringComparer.Ordinal);
    var sourceKey = args
        .SingleOrDefault(argument => argument.StartsWith("--source-key=", StringComparison.Ordinal))?
        ["--source-key=".Length..];
    await using var migrationScope = app.Services.CreateAsyncScope();
    await migrationScope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(CancellationToken.None);
    if (databaseInitializationRequested || databaseDiagnosticsRequested)
    {
        if (databaseDiagnosticsRequested)
        {
            var columns = await migrationScope.ServiceProvider
                .GetRequiredService<DatabaseInitializer>()
                .GetDateColumnDiagnosticsAsync(CancellationToken.None);
            foreach (var column in columns)
            {
                Console.WriteLine(column);
            }
        }

        return;
    }
    else if (mediaMigrationRequested)
    {
        await migrationScope.ServiceProvider.GetRequiredService<GachaBot.Infrastructure.Media.MediaArchiveMigration>()
            .RunAsync(apply, deleteLegacy, sourceKey, CancellationToken.None);
    }
    else if (mediaGarbageCollectionRequested)
    {
        await migrationScope.ServiceProvider.GetRequiredService<GachaBot.Application.Media.IMediaGarbageCollector>()
            .CollectAsync(apply, CancellationToken.None);
    }
    else
    {
        await migrationScope.ServiceProvider.GetRequiredService<GachaBot.Infrastructure.Media.GameBannerSeeder>()
            .SeedAsync(CancellationToken.None);
    }
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/auth/login", (HttpContext context) =>
{
    if (!oauthConfigured)
    {
        return Results.Redirect("/");
    }

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/" },
        [DiscordAuthenticationDefaults.AuthenticationScheme]);
}).AllowAnonymous();
app.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireAuthorization("Administrator");

var components = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
if (!developmentAccess)
{
    components.RequireAuthorization("Administrator");
}

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

public partial class Program;
