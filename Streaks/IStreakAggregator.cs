namespace Streaks;

internal interface IStreakCalculator
{
    StreakInfo Calculate(
        string activityId,
        IEnumerable<Cycle> cycles,
        int currentDayNumber);
}

internal sealed class StreakCalculator : IStreakCalculator
{
    public StreakInfo Calculate(
        string activityId,
        IEnumerable<Cycle> cycles,
        int currentDayNumber)
    {
        var lastStreakCycles = new List<Cycle>();
        foreach (var cycle in cycles)
        {
            if (cycle.CycleOrder == 1)
                lastStreakCycles = new List<Cycle>();

            lastStreakCycles.Add(cycle);
        }

        var lastCycle = lastStreakCycles.Last();

        var streakInfo = new StreakInfo(
            activityId,
            lastCycle.CycleOrder,
            lastCycle.NeedToDo,
            lastCycle.NextCycleFirstDayNumber - currentDayNumber,
            lastCycle.NextCycleFirstDayNumber + lastCycle.CycleLength - currentDayNumber);

        return streakInfo;
    }
}

internal record StreakInfo(
    string ActivityId,
    int SuccessfulCycles,
    int NeedToDoInCurrentCycle,
    int CurrentCycleDeadLineInDays,
    int NextCycleDeadLineInDays)
{
    public bool CurrentCycleIsDone => NeedToDoInCurrentCycle == 0;
    public string NeedToDoInTime
    {
        get
        {
            var nowDayNumber = DateOnly.FromDateTime(DateTimeOffset.Now.UtcDateTime.AddHours(-3)).DayNumber;
            var deadlineDayNumber = nowDayNumber + CurrentCycleDeadLineInDays;
            var deadlineExactTime = DateOnly.FromDayNumber(deadlineDayNumber).ToDateTime(new TimeOnly(3, 0), DateTimeKind.Utc);

            var span = deadlineExactTime - DateTimeOffset.UtcNow;

            return $"{span.Days} days, {span.Hours} hours";
        }
    }
}

internal record Cycle(
    int FromDayNumber,
    int CycleLength,
    int CycleOrder,
    int DesiredAmount,
    int AccumulatedAmount)
{
    public int NextCycleFirstDayNumber => FromDayNumber + CycleLength;
    public int NeedToDo => DesiredAmount - AccumulatedAmount > 0
        ? DesiredAmount - AccumulatedAmount : 0;
}

internal interface ICycleCalculator
{
    IEnumerable<Cycle> CalculateCycles(IEnumerable<ActivityEvent> activityEvents, int currentDayNumber);
}

internal sealed class CycleCalculator : ICycleCalculator
{
    public IEnumerable<Cycle> CalculateCycles(IEnumerable<ActivityEvent> activityEvents, int currentDayNumber)
    {
        var startedAt = DateTimeOffset.MinValue;
        var desiredAmount = 0;
        var cycleLengthDays = 0;

        var startOfCycle = 0;
        var accumulatedAmount = 0;
        var cycleOrder = 1;

        foreach (var @event in activityEvents.OrderBy(x => x.HappenedAt))
        {
            if (@event.Type == ActivityEventType.Started)
            {
                startedAt = @event.HappenedAt;
                desiredAmount = @event.ActivityStartedDesiredAmount;
                cycleLengthDays = @event.PeriodDays;
                continue;
            }

            if (@event.Type == ActivityEventType.Stopped)
            {
                startedAt = DateTimeOffset.MinValue;
                continue;
            }

            if (startedAt == DateTimeOffset.MinValue)
                continue; // If activity is not currently active.

            if (@event.Type != ActivityEventType.Performed)
                continue; // Unknown event type.

            var currentEventDayNumber = GetDayNumber(@event.HappenedAt);

            // First cycle in a streak.
            if (startOfCycle == 0)
            {
                startOfCycle = currentEventDayNumber;
                accumulatedAmount = @event.Amount;
                continue;
            }

            if (currentEventDayNumber - startOfCycle >= cycleLengthDays)
            {
                // Trigger cycle creation for accumulated data.
                var cycle = new Cycle(startOfCycle, cycleLengthDays, cycleOrder, desiredAmount, accumulatedAmount);
                cycleOrder++;

                yield return cycle;

                startOfCycle = cycle.NextCycleFirstDayNumber;
                accumulatedAmount = @event.Amount;

                if (cycle.DesiredAmount > cycle.AccumulatedAmount
                    || currentEventDayNumber - startOfCycle >= cycleLengthDays)
                {
                    // Streak was broken.
                    startOfCycle = currentEventDayNumber;
                    cycleOrder = 1;
                }

                continue;
            }

            accumulatedAmount += @event.Amount;
        }

        var lastCycle = new Cycle(startOfCycle, cycleLengthDays, cycleOrder, desiredAmount, accumulatedAmount);
        yield return lastCycle;

        if (currentDayNumber - startOfCycle >= cycleLengthDays)
        {
            cycleOrder++;
            if (lastCycle.NeedToDo > 0) cycleOrder = 1;


            // TODO: test "+cycle_length" part.
            // TODO: Test sign >= and not just >.
            if (currentDayNumber >= lastCycle.NextCycleFirstDayNumber + lastCycle.CycleLength) cycleOrder = 1;

            yield return new Cycle(currentDayNumber, cycleLengthDays, cycleOrder, desiredAmount, 0);
        }
    }

    private int GetDayNumber(DateTimeOffset timespan)
        => GetDay(timespan).DayNumber;

    private DateOnly GetDay(DateTimeOffset timespan)
    {
        // 6am is the start of the next day.
        return DateOnly.FromDateTime(timespan.UtcDateTime.AddHours(-3).Date);
    }
}

internal interface IStreakAggregator
{
    ValueTask<IEnumerable<StreakInfo>> CalculateCurrentStreaksAsync(CancellationToken cancellationToken);
}

internal sealed record StreakDay(
    DateOnly Day,
    int Amount);

internal sealed class StreakAggregator : IStreakAggregator
{
    private readonly IEventStore _eventStore;
    private readonly IStreakCalculator _streakCalculator;
    private readonly ICycleCalculator _cycleCalculator;

    public StreakAggregator(
        IEventStore eventStore,
        IStreakCalculator streakCalculator,
        ICycleCalculator cycleCalculator)
    {
        _eventStore = eventStore;
        _streakCalculator = streakCalculator;
        _cycleCalculator = cycleCalculator;
    }

    public async ValueTask<IEnumerable<StreakInfo>> CalculateCurrentStreaksAsync(CancellationToken cancellationToken)
    {
        var events = await _eventStore.GetAllEventsAsync(cancellationToken);

        var streaks = new List<StreakInfo>();
        foreach (var activityEvents in events.GroupBy(x => x.ActivityId))
        {
            var activityId = activityEvents.Key;
            var currentDayNumber = DateOnly.FromDateTime(DateTimeOffset.Now.UtcDateTime.AddHours(-3)).DayNumber;
            var cycles = _cycleCalculator.CalculateCycles(activityEvents, currentDayNumber);
            var streakInfo = _streakCalculator.Calculate(activityId, cycles, currentDayNumber);
            if (streakInfo != null)
                streaks.Add(streakInfo);
        }

        return streaks;
    }
}
