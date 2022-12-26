namespace Streaks;

internal sealed class Dependencies
{
    private readonly IEventStore _eventStore;
    private readonly IActivityRepository _activityRepository;
    private readonly IStreakCalculator _streakCalculator;
    private readonly IOutput _output;
    private readonly ITablePrinter _tablePrinter;

    public Dependencies()
    {
        _eventStore = new EventStore()
            .AddCache();

        _activityRepository = new ActivityRepository(_eventStore);
        _streakCalculator = new StreakCalculator(_eventStore);
        _output = new ConsoleOutput();
        _tablePrinter = new TablePrinter(_output);
    }

    public IActivityRepository ActivityRepository => _activityRepository;
    public IStreakCalculator StreakCalculator => _streakCalculator;
    public ITablePrinter TablePrinter => _tablePrinter;
}
