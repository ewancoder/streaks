using Streaks;
using System.Runtime.CompilerServices;
using System.Text;
[assembly:InternalsVisibleTo("Streaks.Tests")]

var deps = new Dependencies();
var activityRepository = deps.ActivityRepository;
var streakAggregator = deps.StreakAggregator;
var tablePrinter = deps.TablePrinter;

Console.ForegroundColor = ConsoleColor.White;
while (true)
{
    var cts = new CancellationTokenSource();

    var streaks = await streakAggregator.CalculateCurrentStreaksAsync(cts.Token);

    Console.Clear();
    Console.BackgroundColor = ConsoleColor.Black;
    Console.WriteLine("== Activities ==");
    Console.WriteLine();

    var activityTable = new Table(5);
    activityTable.AddHeader("Activity", "Desired", "Active", "Period", "Description");

    var activities = await activityRepository.FindAllAsync(cts.Token);
    foreach (var activity in activities.OrderBy(x => x.ActivityId))
    {
        activityTable.AddRow(
            activity.ActivityId,
            activity.DesiredAmount,
            activity.Active,
            activity.Period,
            activity.Description);
    }

    tablePrinter.Print(activityTable, new TablePrinterOptions());

    Console.WriteLine();
    Console.WriteLine($"TODAY {DateTimeOffset.Now}");
    Console.WriteLine();

    Console.WriteLine("===== STREAKS =====");

    var ngStreakTable = new Table(6);
    ngStreakTable.AddHeader("Activity ID", "Successful streak", "Need to do in this cycle", "This cycle deadline (days)", "Next cycle deadline (days)", "Need to do in time");
    foreach (var streak in streaks
        .Where(x => !x.CurrentCycleIsDone)
        .Where(x => !x.ActivityId.ToLowerInvariant().Contains("freeze"))
        .OrderBy(x => x.CurrentCycleDeadLineInDays)
        .ThenByDescending(x => x.NeedToDoInCurrentCycle))
    {
        ngStreakTable.AddRow(streak.ActivityId, streak.SuccessfulCycles, $"{streak.NeedToDoInCurrentCycle} / {activities.First(x => x.ActivityId == streak.ActivityId).DesiredAmount}", streak.CurrentCycleDeadLineInDays, streak.NextCycleDeadLineInDays, streak.NeedToDoInTime);
    }
    ngStreakTable.AddEmptyRow();
    foreach (var streak in streaks
        .Where(x => x.CurrentCycleIsDone)
        .Where(x => !x.ActivityId.ToLowerInvariant().Contains("freeze"))
        .OrderBy(x => x.CurrentCycleDeadLineInDays)
        .ThenByDescending(x => x.NeedToDoInCurrentCycle))
    {
        ngStreakTable.AddRow($"(done) {streak.ActivityId}", streak.SuccessfulCycles, $"{streak.NeedToDoInCurrentCycle} / {activities.First(x => x.ActivityId == streak.ActivityId).DesiredAmount}", streak.CurrentCycleDeadLineInDays, streak.NextCycleDeadLineInDays, streak.NeedToDoInTime);
    }
    tablePrinter.Print(ngStreakTable, new TablePrinterOptions());

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
