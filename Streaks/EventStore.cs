using System.Text.Json;

namespace Streaks;

internal enum ActivityEventType
{
    Started = 1,
    Performed = 2,
    Stopped = 3
}

internal sealed record ActivityEvent(
    string ActivityId,
    ActivityEventType Type,
    DateTimeOffset HappenedAt,
    int ActivityStartedDesiredAmount,
    string Description,
    int Amount,
    int PeriodDays);


internal sealed class Database
{
    public List<ActivityEvent> Events { get; set; } = new List<ActivityEvent>();
}

internal interface IEventStore
{
    ValueTask<IEnumerable<ActivityEvent>> GetAllEventsAsync(CancellationToken cancellationToken);
    ValueTask AddEventAsync(ActivityEvent @event, CancellationToken cancellationToken);
}

internal sealed class EventStore : IEventStore
{
    private readonly string _filePath = "db";
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public async ValueTask AddEventAsync(ActivityEvent @event, CancellationToken cancellationToken)
    {
        using var @lock = await _lock.LockAsync(cancellationToken);

        var database = await ReadDatabaseAsync(cancellationToken);

        database.Events.Add(@event);

        await SaveDatabaseAsync(database, cancellationToken);
    }

    public async ValueTask<IEnumerable<ActivityEvent>> GetAllEventsAsync(CancellationToken cancellationToken)
    {
        using var @lock = await _lock.LockAsync(cancellationToken);

        var database = await ReadDatabaseAsync(cancellationToken);

        return database.Events;
    }

    private async ValueTask CreateDatabaseIfNotExists(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            await File.WriteAllTextAsync(_filePath, "{}", cancellationToken);
    }

    private async ValueTask<Database> ReadDatabaseAsync(CancellationToken cancellationToken)
    {
        await CreateDatabaseIfNotExists(cancellationToken);

        var serialized = await File.ReadAllTextAsync(_filePath, cancellationToken);
        return JsonSerializer.Deserialize<Database>(serialized)
            ?? throw new InvalidOperationException("Could not deserialize the database.");
    }

    private async ValueTask SaveDatabaseAsync(Database database, CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(database);

        await File.WriteAllTextAsync(_filePath, serialized, cancellationToken);
    }
}

internal sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _lock;

    public AsyncLock(SemaphoreSlim @lock)
    {
        _lock = @lock;
    }

    public void Dispose()
    {
        _lock.Release();
    }
}

internal static class AsyncLockExtensions
{
    public static async ValueTask<AsyncLock> LockAsync(this SemaphoreSlim @lock, CancellationToken cancellationToken)
    {
        var wrapper = new AsyncLock(@lock);

        await @lock.WaitAsync(cancellationToken);

        return wrapper;
    }
}

internal sealed class CachedEventStore : IEventStore
{
    private readonly IEventStore _eventStore;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private IEnumerable<ActivityEvent>? _events;

    public CachedEventStore(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async ValueTask AddEventAsync(ActivityEvent @event, CancellationToken cancellationToken)
    {
        using var @lock = await _lock.LockAsync(cancellationToken);

        _events = null;
        await _eventStore.AddEventAsync(@event, cancellationToken);
    }

    public async ValueTask<IEnumerable<ActivityEvent>> GetAllEventsAsync(CancellationToken cancellationToken)
    {
        using var @lock = await _lock.LockAsync(cancellationToken);

        if (_events == null)
            await ReloadCacheAsync(cancellationToken);

        return _events;
    }

    private async ValueTask ReloadCacheAsync(CancellationToken cancellationToken)
    {
        _events = await _eventStore.GetAllEventsAsync(cancellationToken);
    }
}

internal static class CachedEventStoreExtensions
{
    public static IEventStore AddCache(this IEventStore eventStore)
    {
        return new CachedEventStore(eventStore);
    }
}
