using System.Collections;

namespace Streaks.Tests
{
    public class CycleCalculatorTests
    {
        private readonly CycleCalculator _sut;

        public CycleCalculatorTests()
        {
            _sut = new CycleCalculator();
        }

        [Theory]
        [ClassData(typeof(CycleCalculatorTestData))]
        internal void ShouldCalculate(
            int currentDayNumber,
            IEnumerable<ActivityEvent> activityEvents,
            IEnumerable<CycleNg> cycles)
        {
            var result = _sut.CalculateCycles(activityEvents, currentDayNumber)
                .ToList();

            Assert.Equal(cycles, result);
        }
    }

    public class CycleCalculatorTestData : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            // Nitrotype, 5 races per day.
            yield return new object[]
            {
                6,
                new ActivityEventBuilder()
                    .StartActivity(1, 0, 5, 1)
                    .Perform(1, 10, 5)
                    .Perform(2, 2, 1)
                    .Perform(2, 3, 2)
                    .Perform(2, 19, 4)
                    .Perform(3, 0, 1)
                    .Perform(3, 23, 2)
                    .Perform(6, 3, 2)
                    .Build(),
                new CycleBuilder(1, 5)
                    .StartStreak(1, 5) // 1 day.
                    .AddCycle(7) // 2 day.
                    .AddCycle(3) // 3rd day, broken streak.
                    .StartStreak(6, 2) // 6th day, current streak.
                    .Build()
            };

            // Nitrotype, 5 races per day.
            yield return new object[]
            {
                20, // Currently it's 20th day.
                new ActivityEventBuilder()
                    .StartActivity(1, 0, 5, 1)
                    .Perform(1, 10, 5)
                    .Perform(2, 2, 1)
                    .Perform(2, 3, 2)
                    .Perform(2, 19, 4)
                    .Perform(3, 0, 1)
                    .Perform(3, 23, 2)
                    .Perform(6, 3, 2)
                    .Build(),
                new CycleBuilder(1, 5)
                    .StartStreak(1, 5) // 1 day.
                    .AddCycle(7) // 2 day.
                    .AddCycle(3) // 3rd day, broken streak.
                    .StartStreak(6, 2) // 6th day, broken streak.
                    .StartStreak(20, 0) // Current streak starting on current day.
                    .Build()
            };

            // English book, 5 pages per 2 days.
            yield return new object[]
            {
                10,
                new ActivityEventBuilder()
                    .StartActivity(1, 0, 5, 2)
                    .Perform(1, 10, 10)
                    .Perform(3, 5, 4)
                    .Perform(4, 6, 2)
                    .Perform(6, 0, 5)
                    .Perform(8, 23, 5)
                    .Perform(10, 5, 1)
                    .Build(),
                new CycleBuilder(2, 5)
                    .StartStreak(1, 10)
                    .AddCycle(6)
                    .AddCycle(5)
                    .AddCycle(5)
                    .AddCycle(1)
                    .Build()
            };

            yield return new object[]
            {
                20,
                new ActivityEventBuilder()
                    .StartActivity(1, 0, 5, 2)
                    .Perform(1, 10, 10)
                    .Perform(3, 5, 4)
                    .Perform(4, 6, 2)
                    .Perform(6, 0, 5)
                    .Perform(8, 23, 5)
                    .Perform(10, 5, 1)
                    .Build(),
                new CycleBuilder(2, 5)
                    .StartStreak(1, 10)
                    .AddCycle(6)
                    .AddCycle(5)
                    .AddCycle(5)
                    .AddCycle(1)
                    .StartStreak(20, 0)
                    .Build()
            };

            yield return new object[]
            {
                11,
                new ActivityEventBuilder()
                    .StartActivity(1, 0, 5, 2)
                    .Perform(1, 10, 10)
                    .Perform(3, 5, 4)
                    .Perform(4, 6, 2)
                    .Perform(6, 0, 5)
                    .Perform(8, 23, 5)
                    .Perform(10, 5, 1)
                    .Build(),
                new CycleBuilder(2, 5)
                    .StartStreak(1, 10)
                    .AddCycle(6)
                    .AddCycle(5)
                    .AddCycle(5)
                    .AddCycle(1)
                    .StartStreak(11, 0)
                    .Build()
            };
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    internal sealed class ActivityEventBuilder
    {
        private readonly string _activityId = "activity";
        private readonly List<ActivityEvent> _events = new List<ActivityEvent>();

        public ActivityEventBuilder StartActivity(int day, int hour, int desiredAmount, int periodDays)
        {
            var date = GetDate(day, hour);

            _events.Add(new ActivityEvent(_activityId, ActivityEventType.Started, date, desiredAmount, string.Empty, 0, periodDays));

            return this;
        }

        public ActivityEventBuilder Perform(int day, int hour, int amount)
        {
            var date = GetDate(day, hour);

            _events.Add(new ActivityEvent(_activityId, ActivityEventType.Performed, date, 0, string.Empty, amount, 0));

            return this;
        }

        public IEnumerable<ActivityEvent> Build() => _events;

        private DateTimeOffset GetDate(int day, int hour)
        {
            var date = new DateTimeOffset(DateOnly.FromDayNumber(day).ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(3))
                .AddHours(6); // UTC-3 and 3-hour offset, effectively making new day at 6am.

            return date;
        }
    }

    internal sealed class CycleBuilder
    {
        private readonly int _cycleLength;
        private readonly int _desiredAmount;
        private readonly List<CycleNg> _cycles = new List<CycleNg>();
        private int _order = 1;

        public CycleBuilder(int cycleLength, int desiredAmount)
        {
            _cycleLength = cycleLength;
            _desiredAmount = desiredAmount;
        }

        public CycleBuilder StartStreak(int fromDayNumber, int accumulatedAmount)
        {
            _order = 1;
            _cycles.Add(new CycleNg(fromDayNumber, _cycleLength, _order, _desiredAmount, accumulatedAmount));

            return this;
        }

        public CycleBuilder AddCycle(int accumulatedAmount)
        {
            _order++;
            _cycles.Add(new CycleNg(_cycles.Last().NextCycleFirstDayNumber, _cycleLength, _order, _desiredAmount, accumulatedAmount));

            return this;
        }

        public IEnumerable<CycleNg> Build() => _cycles;
    }
}
