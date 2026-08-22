using GachaBot.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace GachaBot.Infrastructure.Sources;

public sealed class PlaywrightRenderedPageClient(
    IOptions<BrowserAutomationOptions> options) : IRenderedPageClient, IAsyncDisposable
{
    private const string HideWebDriverScript =
        "Object.defineProperty(Navigator.prototype, 'webdriver', " +
        "{ get: () => undefined, configurable: true });";

    private readonly BrowserAutomationOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowserContext? _context;

    public async Task<string> GetContentAsync(
        RenderedPageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
            var page = context.Pages.Count > 0
                ? context.Pages[0]
                : await context.NewPageAsync().ConfigureAwait(false);
            await page.GotoAsync(
                request.Url.AbsoluteUri,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = (float)request.NavigationTimeout.TotalMilliseconds,
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            await page.Locator(request.ReadySelector).First.WaitForAsync(
                new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = (float)request.ReadyTimeout.TotalMilliseconds,
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            return await page.ContentAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_context is not null)
            {
                await _context.DisposeAsync().ConfigureAwait(false);
            }

            _playwright?.Dispose();
            _context = null;
            _playwright = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<IBrowserContext> GetContextAsync(CancellationToken cancellationToken)
    {
        if (_context is not null)
        {
            return _context;
        }

        Directory.CreateDirectory(_options.ProfilePath);
        _playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        _context = await _playwright.Chromium.LaunchPersistentContextAsync(
            _options.ProfilePath,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = _options.Headless,
                Locale = _options.Locale,
                TimezoneId = _options.TimezoneId,
                ChromiumSandbox = true,
                Args = ["--disable-blink-features=AutomationControlled"],
            }).WaitAsync(cancellationToken).ConfigureAwait(false);
        await _context.AddInitScriptAsync(HideWebDriverScript)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return _context;
    }
}
