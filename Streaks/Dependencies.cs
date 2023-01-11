namespace Streaks;

internal sealed class Dependencies
{
    private readonly IEventStore _eventStore;
    private readonly IActivityRepository _activityRepository;
    private readonly IStreakAggregator _streakAggregator;
    private readonly IOutput _output;
    private readonly ITablePrinter _tablePrinter;

    public Dependencies()
    {
        _eventStore = new EventStore()
            .AddCache();

        var streakCalculator = new StreakCalculator();
        var cycleCalculator = new CycleCalculator();

        _activityRepository = new ActivityRepository(_eventStore);
        _streakAggregator = new StreakAggregator(_eventStore, streakCalculator, cycleCalculator);
        _output = new LoggingOutputDecorator(
            new FileOutput(),
            new ConsoleOutput());
        _tablePrinter = new TablePrinter(_output);
    }

    public IActivityRepository ActivityRepository => _activityRepository;
    public IStreakAggregator StreakAggregator => _streakAggregator;
    public ITablePrinter TablePrinter => _tablePrinter;
}
