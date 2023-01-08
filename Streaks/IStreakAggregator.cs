using System.Security.Cryptography;

namespace Streaks;

internal interface IStreakCalculatorNg
{
    StreakInfoNg Calculate(IEnumerable<ActivityEvent> activityEvents);
}

internal sealed class StreakCalculatorNg : IStreakCalculatorNg
{
    public StreakInfoNg Calculate(IEnumerable<ActivityEvent> activityEvents)
    {
        var activityId = activityEvents.GroupBy(x => x.ActivityId).Single().Key;

        var startedAt = DateTimeOffset.MinValue;
        var desiredAmount = 0;
        var cycleLengthDays = 0;

        var cycles = new List<CycleNg>();
        var startOfCycle = DateOnly.MinValue;
        var accumulatedAmount = 0;
        var lastCycleBroken = false;

        var needToDoInCurrentCycle = 0;

        foreach (var @event in activityEvents.OrderBy(x => x.HappenedAt))
        {
            if (@event.Type == ActivityEventType.Started)
            {
                startedAt = @event.HappenedAt;
                desiredAmount = @event.ActivityStartedDesiredAmount;
                cycleLengthDays = @event.PeriodDays;
            }

            if (@event.Type == ActivityEventType.Performed)
            {
                if (startedAt == DateTimeOffset.MinValue)
                    continue; // Activity hasn't started yet.

                if (startOfCycle == DateOnly.MinValue)
                {
                    // First date of the cycle.
                    startOfCycle = GetDay(@event.HappenedAt);

                    accumulatedAmount = @event.Amount;
                    needToDoInCurrentCycle = desiredAmount - @event.Amount;

                    if (@event.Amount >= desiredAmount)
                    {
                        var cycle = new CycleNg(startOfCycle.DayNumber, cycleLengthDays, 3, 3, 3);
                        cycles.Add(cycle);
                        startOfCycle = DateOnly.FromDayNumber(cycle.NextCycleFirstDayNumber);
                        accumulatedAmount = 0;
                        lastCycleBroken = false;
                        needToDoInCurrentCycle = 0;
                        continue;
                        // Cycle was finished with the first record.
                    }

                    continue;
                }

                if (cycles.Any() && cycles.Last().NextCycleFirstDayNumber > GetDay(@event.HappenedAt).DayNumber)
                {
                    // Event happened within already finished cycle, no need to consider it.
                    continue;
                }

                // New cycle is started by this event.
                if (cycles.Any() && GetDay(DateTimeOffset.Now).DayNumber >= cycles.Last().NextCycleFirstDayNumber)
                {
                    // Cycle is broken.
                    startOfCycle = GetDay(DateTimeOffset.Now);
                    accumulatedAmount = @event.Amount;
                    lastCycleBroken = true;
                    needToDoInCurrentCycle = desiredAmount - @event.Amount;

                    // Copied from above.
                    if (@event.Amount >= desiredAmount)
                    {
                        var cycle = new CycleNg(startOfCycle.DayNumber, cycleLengthDays, 3, 3, 3    );
                        cycles.Add(cycle);
                        startOfCycle = DateOnly.FromDayNumber(cycle.NextCycleFirstDayNumber);
                        accumulatedAmount = 0;
                        lastCycleBroken = false;
                        needToDoInCurrentCycle = 0;
                        continue;
                        // Cycle was finished with the first record.
                    }

                    continue;
                }

                accumulatedAmount += @event.Amount;
                needToDoInCurrentCycle -= @event.Amount;
                if (accumulatedAmount >= desiredAmount)
                {
                    var cycle = new CycleNg(startOfCycle.DayNumber, cycleLengthDays, 3,3,3);
                    cycles.Add(cycle);
                    startOfCycle = DateOnly.FromDayNumber(cycle.NextCycleFirstDayNumber);
                    accumulatedAmount = 0;
                    lastCycleBroken = false;
                    needToDoInCurrentCycle = 0;
                    continue;
                    // Cycle was finished by this event.
                }
            }
        }

        DateOnly GetDay(DateTimeOffset timespan)
        {
            return DateOnly.FromDateTime(timespan.UtcDateTime.AddHours(-3).Date);
        }

        int CalculateSuccessfulCyclesSinceDays()
        {
            var firstDayNumber = 0;
            var nextCycleFirstDayNumber = 0;
            foreach (var cycle in cycles)
            {
                if (firstDayNumber == 0)
                {
                    firstDayNumber = cycle.FromDayNumber;
                    nextCycleFirstDayNumber = cycle.NextCycleFirstDayNumber;
                    continue;
                }

                if (cycle.FromDayNumber == nextCycleFirstDayNumber)
                {
                    nextCycleFirstDayNumber = cycle.NextCycleFirstDayNumber;
                    continue;
                }

                // Broken cycle.
                firstDayNumber = cycle.FromDayNumber;
                nextCycleFirstDayNumber = cycle.NextCycleFirstDayNumber;
            }

            return 3;
        }

        var successfulCyclesSinceDays = CalculateSuccessfulCyclesSinceDays();

        // TODO: Account for breaking cycle when no actions were performed for long time.

        var streakInfo = new StreakInfoNg(
            successfulCyclesSinceDays,
            needToDoInCurrentCycle, 0, 0);

        throw new NotImplementedException();
    }
}

internal record StreakInfoNg(
    int SuccessfulCyclesSinceDays,
    int NeedToDoInCurrentCycle,
    int CurrentCycleDeadLineInDays,
    int NextCycleDeadLineInDays)
{
    public bool CurrentCycleIsDone => NeedToDoInCurrentCycle == 0;
}

internal record CycleNg(
    int FromDayNumber,
    int CycleLength,
    int CycleOrder,
    int DesiredAmount,
    int AccumulatedAmount)
{
    public int NextCycleFirstDayNumber => FromDayNumber + CycleLength;
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

    public StreakAggregator(
        IEventStore eventStore,
        IStreakCalculator streakCalculator)
    {
        _eventStore = eventStore;
        _streakCalculator = streakCalculator;
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
}
