using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Flowery.Controls.Scheduler.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.UnitTests.Controls
{
    /// <summary>Scheduler logic without UI: layout packing, occurrences, time zones and time formatting.</summary>
    [TestClass]
    public class Given_SchedulerLogic
    {
        private static readonly DateTime Monday = new(2026, 10, 12);

        [TestMethod]
        public void When_PackingOverlaps_AndCloning_TheUpstreamDataContractHolds()
        {
            var first = new AppointmentModel { Start = Monday.AddHours(9), End = Monday.AddHours(11), ResourceIds = { 1 }, Tiles = { new AppointmentDecoration { Text = "Tile" } } };
            var second = new AppointmentModel { Start = Monday.AddHours(10), End = Monday.AddHours(12) };
            var third = new AppointmentModel { Start = Monday.AddHours(13), End = Monday.AddHours(14) };
            var layouts = EventBlockLayout.Layout(Monday, new[] { first, second, third });
            Assert.AreEqual(2, layouts.Single(layout => layout.Appointment == first).ColumnCount);
            Assert.AreEqual(1, layouts.Single(layout => layout.Appointment == third).ColumnCount);
            var copy = first.Clone();
            copy.ResourceIds.Add(2);
            copy.Tiles.Clear();
            Assert.HasCount(1, first.ResourceIds);
            Assert.HasCount(1, first.Tiles);
        }

        [TestMethod]
        public void When_DailySpanIsExpanded_EachDateGetsTheStoredClockTimes()
        {
            var daily = new AppointmentModel { Start = Monday.AddHours(9), End = Monday.AddDays(2).AddHours(10), SpanMode = AppointmentSpanMode.Daily };
            var occurrences = AppointmentOccurrences.InRange(daily, Monday, Monday.AddDays(7)).ToArray();
            Assert.HasCount(3, occurrences);
            Assert.AreEqual(Monday.AddDays(2).AddHours(9), occurrences[2].Start);
            Assert.AreEqual(Monday.AddDays(2).AddHours(10), occurrences[2].End);
            // An end time that does not follow the start time is no daily window.
            daily.End = Monday.AddDays(2).AddHours(8);
            Assert.IsEmpty(AppointmentOccurrences.InRange(daily, Monday, Monday.AddDays(7)));
        }

        [TestMethod]
        public void When_ConvertingZones_UtcRoundTrips_AndSpringGapsAreRejected()
        {
            var utc = Monday.AddHours(13);
            var newYork = TimeZoneConverter.ToZone(utc, "America/New_York");
            Assert.AreEqual(Monday.AddHours(9), newYork);
            Assert.AreEqual(utc, TimeZoneConverter.FromZone(newYork, "America/New_York"));
            Assert.AreEqual(utc, TimeZoneConverter.ToZone(utc, "Not/AZone"));
            Assert.ThrowsExactly<ArgumentException>(() => TimeZoneConverter.FromZone(new DateTime(2026, 3, 29, 2, 30, 0), "Europe/Berlin"));
            Assert.AreEqual("New York", TimeZoneConverter.ShortLabel("America/New_York"));
        }

        [TestMethod]
        public void When_FormattingCompactTimes_SharedMeridiemIsDropped()
        {
            Assert.AreEqual("10a-12:30p", SchedulerTimeFormatter.CompactRange(Monday.AddHours(10), Monday.AddHours(12.5)));
            Assert.AreEqual("9-10:30a", SchedulerTimeFormatter.CompactRange(Monday.AddHours(9), Monday.AddHours(10.5)));
            Assert.AreEqual("12a", SchedulerTimeFormatter.Compact(Monday));
        }
    }
}
