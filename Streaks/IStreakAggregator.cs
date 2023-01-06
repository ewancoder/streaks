namespace Streaks;

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
    private readonly int startOfDay = 3; // 3 hours in the morning, by GMT timezone (6 hours in the morning GMT+3).
    private readonly IEventStore _eventStore;

    public StreakAggregator(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async ValueTask<IEnumerable<StreakInfo>> CalculateCurrentStreaksAsync(CancellationToken cancellationToken)
    {
        var events = await _eventStore.GetAllEventsAsync(cancellationToken);

        var streaks = new List<StreakInfo>();
        foreach (var activityEvents in events.GroupBy(x => x.ActivityId))
        {
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

            var streakInfo = CalculateStreakInfo(activityEvents.Key, cycleLength, desiredAmount, streakDays, lastActivity);

            if (streakInfo != null)
                streaks.Add(streakInfo);
        }

        return streaks;
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
