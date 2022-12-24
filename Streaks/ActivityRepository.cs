namespace Streaks;

internal interface IActivityRepository
{
    ValueTask<IEnumerable<Activity>> FindAllAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(Activity activity, CancellationToken cancellationToken);
}

internal sealed record Activity(
    string ActivityId,
    int DesiredAmount,
    string Description,
    int Period,
    bool Active);

internal sealed class ActivityRepository : IActivityRepository
{
    private readonly IEventStore _store;

    public ActivityRepository(IEventStore store)
    {
        _store = store;
    }

    public async ValueTask<IEnumerable<Activity>> FindAllAsync(CancellationToken cancellationToken)
    {
        var events = await _store.GetAllEventsAsync(cancellationToken);

        var activities = new Dictionary<string, Activity>();
        foreach (var @event in events)
        {
            if (@event.Type == ActivityEventType.Started)
            {
                if (activities.ContainsKey(@event.ActivityId))
                    activities.Remove(@event.ActivityId);

                activities.Add(@event.ActivityId, new Activity(
                    @event.ActivityId,
                    @event.ActivityStartedDesiredAmount,
                    @event.Description,
                    @event.PeriodDays,
                    true));
            }

            if (@event.Type == ActivityEventType.Stopped)
            {
                var existingActivity = activities[@event.ActivityId];
                var updatedActivity = existingActivity with { Active = false };

                activities.Remove(@event.ActivityId);
                activities.Add(@event.ActivityId, updatedActivity);
            }
        }

        return activities.Values;
    }

    public ValueTask SaveAsync(Activity activity, CancellationToken cancellationToken)
    {
        return _store.AddEventAsync(new ActivityEvent(
            activity.ActivityId,
            ActivityEventType.Started,
            DateTimeOffset.MinValue,
            activity.DesiredAmount,
            activity.Description,
            0,
            activity.Period), cancellationToken);
    }
}
