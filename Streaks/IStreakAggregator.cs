namespace Streaks;

internal interface IStreakCalculatorNg
{
    StreakInfoNg Calculate(
        string activityId,
        IEnumerable<CycleNg> cycles,
        int currentDayNumber);
}

internal sealed class StreakCalculatorNg : IStreakCalculatorNg
{
    public StreakInfoNg Calculate(
        string activityId,
        IEnumerable<CycleNg> cycles,
        int currentDayNumber)
    {
        var lastStreakCycles = new List<CycleNg>();
        foreach (var cycle in cycles)
        {
            if (cycle.CycleOrder == 1)
                lastStreakCycles = new List<CycleNg>();

            lastStreakCycles.Add(cycle);
        }

        var lastCycle = lastStreakCycles.Last();

        var streakInfo = new StreakInfoNg(
            activityId,
            lastCycle.CycleOrder,
            lastCycle.NeedToDo,
            lastCycle.NextCycleFirstDayNumber - currentDayNumber,
            lastCycle.NextCycleFirstDayNumber + lastCycle.CycleLength - currentDayNumber);

        return streakInfo;
    }
}

internal record StreakInfoNg(
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

internal record CycleNg(
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

internal interface IStreakCalculator
{
    StreakInfo CalculateStreakInfo(IEnumerable<ActivityEvent> activityEvents);
}

internal interface ICycleCalculator
{
    IEnumerable<CycleNg> CalculateCycles(IEnumerable<ActivityEvent> activityEvents, int currentDayNumber);
}

internal sealed class CycleCalculator : ICycleCalculator
{
    public IEnumerable<CycleNg> CalculateCycles(IEnumerable<ActivityEvent> activityEvents, int currentDayNumber)
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
                var cycle = new CycleNg(startOfCycle, cycleLengthDays, cycleOrder, desiredAmount, accumulatedAmount);
                cycleOrder++;

                yield return cycle;

                startOfCycle = cycle.NextCycleFirstDayNumber;
                accumulatedAmount = @event.Amount;

                if (cycle.DesiredAmount > cycle.AccumulatedAmount
                    || currentEventDayNumber - startOfCycle > cycleLengthDays)
                {
                    // Streak was broken.
                    startOfCycle = currentEventDayNumber;
                    cycleOrder = 1;
                }

                continue;
            }

            accumulatedAmount += @event.Amount;
        }

        yield return new CycleNg(startOfCycle, cycleLengthDays, cycleOrder, desiredAmount, accumulatedAmount);

        if (currentDayNumber - startOfCycle >= cycleLengthDays)
        {
            yield return new CycleNg(currentDayNumber, cycleLengthDays, 1, desiredAmount, 0);
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

internal sealed class StreakCalculator : IStreakCalculator
{
    private readonly int startOfDay = 3; // 3 hours in the morning, by GMT timezone (6 hours in the morning GMT+3).

    public StreakInfo? CalculateStreakInfo(IEnumerable<ActivityEvent> activityEvents)
    {
        if (activityEvents.GroupBy(x => x.ActivityId).Count() > 1)
            throw new ArgumentException("Cannot calculate streak from different activities.", nameof(activityEvents));

        var activityId = activityEvents.First().ActivityId;
        var started = false;
        var desiredAmount = 0;
        var cycleLength = 0;
        var lastActivity = DateTimeOffset.MinValue;
        var description = string.Empty;

        var dayAndAmount = new Dictionary<DateOnly, int>();

        DateOnly GetDay(ActivityEvent @event)
        {
            return DateOnly.FromDateTime(@event.HappenedAt.UtcDateTime.AddHours(-startOfDay).Date);
        }

        void AddToDay(DateOnly day, int amount)
        {
            if (dayAndAmount.ContainsKey(day))
                dayAndAmount[day] += amount;
            else
                dayAndAmount[day] = amount;
        }

        void AddEventToDay(ActivityEvent @event, int amount)
        {
            var day = GetDay(@event);

            AddToDay(day, amount);
        }

        foreach (var @event in activityEvents.OrderBy(x => x.HappenedAt))
        {
            if (@event.ActivityId == "freeze-all" || @event.ActivityId == "unfreeze-all")
                continue; // Not implemented for now.

            if (@event.Type == ActivityEventType.Started)
            {
                started = true;
                desiredAmount = @event.ActivityStartedDesiredAmount;
                cycleLength = @event.PeriodDays;
                description = @event.Description;
                continue;
            }

            if (@event.Type == ActivityEventType.Stopped)
            {
                started = false;
                continue;
            }

            if (!started)
                continue;

            lastActivity = @event.HappenedAt;
            AddEventToDay(@event, @event.Amount);
        }

        var streakDays = new List<StreakDay>();
        foreach (var day in dayAndAmount.Keys.OrderBy(x => x))
        {
            streakDays.Add(new StreakDay(day, dayAndAmount[day]));
        }

        var streakInfo = CalculateStreakInfo(activityId, cycleLength, desiredAmount, streakDays, lastActivity);

        if (streakInfo != null) // TODO: Figure out why it can be null.
            return streakInfo;

        return null;
    }

    private StreakInfo? CalculateStreakInfo(string activity, int cycleLength, int desiredAmount, IEnumerable<StreakDay> days, DateTimeOffset lastActivity)
    {
        if (!days.Any())
            return null;

        var daysPassed = 0;
        var amount = 0;
        DateOnly lastDay;
        DateOnly startedOnDay;
        foreach (var day in days)
        {
            if (daysPassed == 0)
            {
                startedOnDay = day.Day;
                daysPassed = 1;
                lastDay = day.Day;
            }
            else
            {
                daysPassed += (day.Day.DayNumber - lastDay.DayNumber);
                lastDay = day.Day;
            }

            if (daysPassed <= cycleLength)
            {
                amount += day.Amount;
            }
            else
            {
                while (daysPassed > cycleLength)
                {
                    daysPassed -= cycleLength;
                }

                startedOnDay = DateOnly.FromDayNumber(day.Day.DayNumber - daysPassed + 1);
                amount = day.Amount;
            }
        }


        var today = DateOnly.FromDateTime(DateTime.UtcNow - TimeSpan.FromHours(cycleLength));
        if (today > lastDay)
        {
            daysPassed += today.DayNumber - lastDay.DayNumber;
            if (daysPassed > cycleLength)
            {
                amount = 0;
                while (daysPassed > cycleLength)
                {
                    daysPassed -= cycleLength;
                }
                startedOnDay = today;
            }
        }

        var shouldFinishOn = DateOnly.FromDayNumber(startedOnDay.DayNumber + cycleLength - 1);
        var nextCycleStartsAt = shouldFinishOn;
        if (desiredAmount - amount <= 0)
            shouldFinishOn = DateOnly.FromDayNumber(shouldFinishOn.DayNumber + cycleLength);
        //nextCycleStartsAt = DateOnly.FromDayNumber(nextCycleStartsAt.DayNumber + cycleLength - 1);

        var deadline = shouldFinishOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddDays(1)
            .AddHours(startOfDay);

        var nextCycleStartsAtDateTime = nextCycleStartsAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddDays(1)
            .AddHours(startOfDay).ToLocalTime();

        return new StreakInfo(
            activity,
            desiredAmount - amount <= 0 ? desiredAmount : desiredAmount - amount,
            desiredAmount - amount <= 0,
            startedOnDay,
            DateOnly.FromDayNumber(startedOnDay.DayNumber + cycleLength - 1),
            deadline.ToLocalTime(),
            nextCycleStartsAtDateTime,
            desiredAmount - amount < 0 ? 0 : desiredAmount - amount,
            (DateTimeOffset.Now - lastActivity).Days);
    }

}

internal interface IStreakAggregator
{
    ValueTask<IEnumerable<StreakInfo>> CalculateCurrentStreaksAsync(CancellationToken cancellationToken);
    ValueTask<IEnumerable<StreakInfoNg>> CalculateCurrentStreaksNgAsync(CancellationToken cancellationToken);
}

internal sealed record StreakInfo(
    string ActivityId,
    int NeedToDo,
    bool DoneThisStreak,
    DateOnly StartedLatestStreakOn,
    DateOnly ShouldFinishActivityTill,
    DateTime AbsoluteDeadLine,
    DateTime NextCycleStartsAt,
    int AmountLeftToDo,
    int LastDoneDaysAgo);

internal sealed record StreakDay(
    DateOnly Day,
    int Amount);

internal sealed class StreakAggregator : IStreakAggregator
{
    private readonly IEventStore _eventStore;
    private readonly IStreakCalculator _streakCalculator;
    private readonly IStreakCalculatorNg _streakCalculatorNg;
    private readonly ICycleCalculator _cycleCalculator;

    public StreakAggregator(
        IEventStore eventStore,
        IStreakCalculator streakCalculator,
        IStreakCalculatorNg streakCalculatorNg,
        ICycleCalculator cycleCalculator)
    {
        _eventStore = eventStore;
        _streakCalculator = streakCalculator;
        _streakCalculatorNg = streakCalculatorNg;
        _cycleCalculator = cycleCalculator;
    }

    public async ValueTask<IEnumerable<StreakInfo>> CalculateCurrentStreaksAsync(CancellationToken cancellationToken)
    {
        var events = await _eventStore.GetAllEventsAsync(cancellationToken);

        var streaks = new List<StreakInfo>();
        foreach (var activityEvents in events.GroupBy(x => x.ActivityId))
        {
            var streakInfo = _streakCalculator.CalculateStreakInfo(activityEvents);
            if (streakInfo != null)
                streaks.Add(streakInfo);
        }

        return streaks;
    }

    public async ValueTask<IEnumerable<StreakInfoNg>> CalculateCurrentStreaksNgAsync(CancellationToken cancellationToken)
    {
        var events = await _eventStore.GetAllEventsAsync(cancellationToken);

        var streaks = new List<StreakInfoNg>();
        foreach (var activityEvents in events.GroupBy(x => x.ActivityId))
        {
            var activityId = activityEvents.Key;
            var currentDayNumber = DateOnly.FromDateTime(DateTimeOffset.Now.UtcDateTime.AddHours(-3)).DayNumber;
            var cycles = _cycleCalculator.CalculateCycles(activityEvents, currentDayNumber);
            var streakInfo = _streakCalculatorNg.Calculate(activityId, cycles, currentDayNumber);
            if (streakInfo != null)
                streaks.Add(streakInfo);
        }

        return streaks;
    }
}
