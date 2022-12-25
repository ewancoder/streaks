using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Streaks;

internal sealed class Dependencies
{
    private readonly IEventStore _eventStore;
    private readonly IActivityRepository _activityRepository;
    private readonly IStreakCalculator _streakCalculator;

    public Dependencies()
    {
        _eventStore = new EventStore()
            .AddCache();

        _activityRepository = new ActivityRepository(_eventStore);
        _streakCalculator = new StreakCalculator(_eventStore);
    }

    public IActivityRepository ActivityRepository => _activityRepository;
    public IStreakCalculator StreakCalculator => _streakCalculator;
}
