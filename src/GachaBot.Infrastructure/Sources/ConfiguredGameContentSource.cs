using GachaBot.Application.Ingestion;

namespace GachaBot.Infrastructure.Sources;

public interface ISourceHandler
{
    string Key { get; }

    IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
        SourceDefinition definition,
        CancellationToken cancellationToken);
}

public sealed class SourceHandlerResolver(IEnumerable<ISourceHandler> handlers)
{
    private readonly Dictionary<string, ISourceHandler> _handlers = handlers
        .ToDictionary(handler => handler.Key, StringComparer.Ordinal);

    public ISourceHandler Resolve(string key) =>
        _handlers.TryGetValue(key, out var handler)
            ? handler
            : throw new InvalidOperationException($"Source handler '{key}' is not registered.");
}

public sealed class ConfiguredGameContentSource(
    SourceDefinition definition,
    SourceHandlerResolver resolver) : IGameContentSource
{
    private readonly ISourceHandler _handler = resolver.Resolve(definition.Handler);

    public string Key => definition.Key;

    public SourceTrust Trust => definition.Trust;

    public bool SchedulesUpcomingEvents => definition.Timeline is not null || definition.EventCalendar is not null;

    public IAsyncEnumerable<SourceContentSnapshot> FetchAsync(CancellationToken cancellationToken) =>
        _handler.FetchAsync(definition, cancellationToken);
}
