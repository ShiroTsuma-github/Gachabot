namespace GachaBot.Infrastructure.IntegrationTests;

internal static class TestSourceFixture
{
    internal static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "test-sources", .. path]));
}
