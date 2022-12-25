using Streaks;
using System.Text;
using System.Text.Json;

var deps = new Dependencies();
var activityRepository = deps.ActivityRepository;
var streakCalculator = deps.StreakCalculator;

Console.ForegroundColor = ConsoleColor.White;
while (true)
{
    var cts = new CancellationTokenSource();

    var streaks = await streakCalculator.CalculateCurrentStreaksAsync(cts.Token);

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

        await activityRepository.AddActivityAsync(new Activity(
            activityId, activityDesiredAmount, description, period, true), cts.Token);
    }

    if (command.StartsWith("do "))
    {
        var activity = command.Replace("do ", "").Split(" ");
        var activityId = activity[0];
        var amount = Convert.ToInt32(activity[1]);

        await activityRepository.PerformActivityAsync(activityId, amount, cts.Token);
    }
}
