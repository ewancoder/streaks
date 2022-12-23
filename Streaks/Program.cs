using Streaks;
using System.Text;
using System.Text.Json;

var eventStore = new EventStore()
    .AddCache();

var activityRepository = new ActivityRepository(eventStore);

Console.ForegroundColor = ConsoleColor.White;
while (true)
{
    var startOfDay = 3; // 3 hours in the morning, by GMT timezone (6 hours in the morning GMT+3).

    var cts = new CancellationTokenSource();
    var events = await eventStore.GetAllEventsAsync(cts.Token);

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

        var streakInfo = CalculateStreakInfo(activityEvents.Key, cycleLength, desiredAmount, streakDays);

        if (streakInfo != null)
            streaks.Add(streakInfo);
    }

    Console.Clear();
    Console.BackgroundColor = ConsoleColor.Black;
    Console.WriteLine("== Activities ==");
    Console.WriteLine();

    Console.WriteLine($"{"Activity",-20}\t{"Desired",-8}\t{"Active",-6}\t{"Period",-6}\t{"Description"}");

    var activities = await activityRepository.FindAllAsync(cts.Token);
    foreach (var activity in activities.OrderBy(x => x.ActivityId))
    {
        Console.WriteLine($"{activity.ActivityId,-20}\t{activity.DesiredAmount,-8}\t{activity.Active,-6}\t{activity.Period,-6}\t{activity.Description}");
    }

    Console.WriteLine();
    Console.WriteLine($"TODAY {DateTimeOffset.Now}");
    Console.WriteLine();

    Console.WriteLine("===== STREAKS =====");
    Console.WriteLine();

    Console.WriteLine($"{"Activity",-20}{"Need to do",-15}{"In time",-50}{"Next cycle starts at"}");
    Console.WriteLine();
    var alternativeColor = ConsoleColor.DarkGray;
    var color = alternativeColor;

    foreach (var activity in activities.Where(x => !streaks.Any(s => s.ActivityId == x.ActivityId)).OrderBy(x => x.ActivityId))
    {
        color = color == alternativeColor ? ConsoleColor.Black : alternativeColor;
        Console.BackgroundColor = color;

        Console.WriteLine($"{activity.ActivityId,-20}{activity.Description}");
    }

    Console.WriteLine();

    foreach (var streak in streaks.Where(x => !x.DoneThisStreak).OrderBy(x => x.AbsoluteDeadLine))
    {
        color = color == alternativeColor ? ConsoleColor.Black : alternativeColor;
        Console.BackgroundColor = color;

        Console.WriteLine($"{streak.ActivityId,-20}{(streak.DoneThisStreak ? streak.NeedToDo + " (done)" : streak.NeedToDo),-15}{GetHumanTime(streak.AbsoluteDeadLine-DateTime.Now),-50}{(streak.DoneThisStreak ? streak.NextCycleStartsAt : "")}");
    }

    Console.WriteLine();

    foreach (var streak in streaks.Where(x => x.DoneThisStreak).OrderBy(x => x.AbsoluteDeadLine))
    {
        color = color == alternativeColor ? ConsoleColor.Black : alternativeColor;
        Console.BackgroundColor = color;

        Console.WriteLine($"{streak.ActivityId,-20}{(streak.DoneThisStreak ? streak.NeedToDo + " (done)" : streak.NeedToDo),-15}{GetHumanTime(streak.AbsoluteDeadLine-DateTime.Now),-50}{(streak.DoneThisStreak ? streak.NextCycleStartsAt : "")}");
    }

    Console.BackgroundColor = ConsoleColor.Black;
    Console.WriteLine();

    string GetHumanTime(TimeSpan timespan)
    {
        var sb = new StringBuilder();
        if (timespan.TotalDays >= 1)
            sb.Append($"{Math.Floor(timespan.TotalDays)} days");

        if (timespan.Hours > 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{timespan.Hours} hours");
        }

        if (timespan.Minutes > 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{timespan.Hours} minutes");
        }

        return sb.ToString();
    }

    var command = Console.ReadLine();
    if (command == null)
        throw new InvalidOperationException("Invalid input.");

    if (command.StartsWith("add activity "))
    {
        var activity = command.Replace("add activity ", "").Split(" ");
        var activityId = activity[0];
        var activityDesiredAmount = Convert.ToInt32(activity[1]);
        var period = Convert.ToInt32(activity[2]);
        var description = string.Join(' ', activity.Skip(3));

        await activityRepository.SaveAsync(new Activity(
            activityId, activityDesiredAmount, description, period, true), cts.Token);
    }

    if (command.StartsWith("do "))
    {
        var activity = command.Replace("do ", "").Split(" ");
        var activityId = activity[0];
        var amount = Convert.ToInt32(activity[1]);

        await eventStore.AddEventAsync(new ActivityEvent(
            activityId, ActivityEventType.Performed, DateTimeOffset.Now, 0, string.Empty, amount, 0),
            cts.Token);
    }

    StreakInfo? CalculateStreakInfo(string activity, int cycleLength, int desiredAmount, IEnumerable<StreakDay> days)
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
            desiredAmount - amount < 0 ? 0 : desiredAmount - amount);
    }
}

public sealed record StreakInfo(
    string ActivityId,
    int NeedToDo,
    bool DoneThisStreak,
    DateOnly StartedLatestStreakOn,
    DateOnly ShouldFinishActivityTill,
    DateTime AbsoluteDeadLine,
    DateTime NextCycleStartsAt,
    int AmountLeftToDo);

public sealed record StreakDay(
    DateOnly Day,
    int Amount);

public sealed record Activity(
    string ActivityId,
    int DesiredAmount,
    string Description,
    int Period,
    bool Active);
