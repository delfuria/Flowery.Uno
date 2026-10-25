using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Flowery.Controls.Scheduler.Utilities;
using Microsoft.UI.Xaml.Automation;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    private sealed record CalendarDate(DateTime Date);

    private FrameworkElement BuildCalendar()
    {
        var root = WithDefinitions(new Grid { ColumnSpacing = SpacingMedium }, "Auto,*");
        var month = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        var start = WeekStart(month, FirstDayOfWeek);
        var resourceIds = (SchedulerResources ?? Array.Empty<ResourceModel>())
            .Where(resource => ResourceKindFilter is null || resource.Kind == ResourceKindFilter)
            .Select(static resource => resource.Id).ToHashSet();
        var occupiedDays = new HashSet<DateTime>();
        var end = start.AddDays(42);
        foreach (var appointment in Appointments ?? Array.Empty<AppointmentModel>())
        {
            if (ResourceKindFilter is not null && !appointment.ResourceIds.Any(resourceIds.Contains))
                continue;
            foreach (var occurrence in AppointmentOccurrences.InRange(appointment, start, end))
                for (var day = occurrence.Start.Date < start ? start : occurrence.Start.Date;
                     day < end && day < occurrence.End; day = day.AddDays(1))
                    occupiedDays.Add(day);
        }
        var calendar = WithDefinitions(new Grid
        {
            Name = "PART_MonthNavigator", Width = Token<double>("DaisySchedulerCalendarWidth") * SizeScale,
            RowSpacing = SpacingSmall
        }, "", "Auto,Auto");
        var heading = WithDefinitions(new Grid(), "*,Auto,Auto");
        heading.Children.Add(Label(month.ToString("MMMM yyyy", Culture)));
        foreach (var direction in new[] { -1, 1 })
        {
            var button = new DaisyButton
            {
                Content = direction < 0 ? "↑" : "↓", Size = ResolvedSize, Variant = DaisyButtonVariant.Ghost,
                Tag = direction
            };
            AutomationProperties.SetName(button, Text(direction < 0 ? "Previous" : "Next"));
            button.Click += OnCalendarMonthChanged;
            Grid.SetColumn(button, direction < 0 ? 1 : 2);
            heading.Children.Add(button);
        }
        calendar.Children.Add(heading);
        var days = WithDefinitions(new Grid { Name = "PART_CalendarDays" },
            "Auto,*,*,*,*,*,*,*", "Auto,Auto,Auto,Auto,Auto,Auto,Auto");
        days.Children.Add(Label(Text("WeekNumber"), true));
        for (var column = 0; column < 7; column++)
        {
            var day = Label(Culture.DateTimeFormat.GetShortestDayName(start.AddDays(column).DayOfWeek), true);
            day.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(day, column + 1);
            days.Children.Add(day);
        }
        for (var week = 0; week < 6; week++)
        {
            var weekStart = start.AddDays(week * 7);
            var number = Label(CalendarWeekNumber(weekStart).ToString(Culture), true);
            number.Margin = new Thickness(SpacingXS);
            Grid.SetRow(number, week + 1);
            days.Children.Add(number);
            for (var column = 0; column < 7; column++)
            {
                var date = weekStart.AddDays(column);
                var selected = date == SelectedDate.Date;
                var occupied = occupiedDays.Contains(date);
                var button = new DaisyButton
                {
                    Content = date.Day.ToString(Culture), Tag = new CalendarDate(date), Size = ResolvedSize,
                    Variant = selected || occupied ? DaisyButtonVariant.Primary : DaisyButtonVariant.Ghost,
                    ButtonStyle = occupied && !selected ? DaisyButtonStyle.Soft : DaisyButtonStyle.Default,
                    FontWeight = date == DateTime.Today || occupied ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 0, ContentPadding = new Thickness(SpacingXS)
                };
                // DaisyButton owns Foreground; its lightweight-styling key overrides it for every variant.
                if (date.Month != month.Month)
                    button.Resources["DaisyButtonForeground"] = Brush("DaisyBaseContentMutedBrush");
                AutomationProperties.SetName(button, date.ToString("D", Culture));
                button.Click += OnCalendarDateSelected;
                Grid.SetRow(button, week + 1);
                Grid.SetColumn(button, column + 1);
                days.Children.Add(button);
            }
        }
        Grid.SetRow(days, 1);
        calendar.Children.Add(days);
        root.Children.Add(new ScrollViewer
        {
            Content = calendar, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var detail = WithDefinitions(new Grid { RowSpacing = SpacingSmall }, "", "Auto,*");
        detail.Children.Add(Label(SelectedDate.ToString("D", Culture)));
        var entries = new StackPanel { Name = "PART_CalendarAppointments", Spacing = SpacingSmall };
        var appointments = Items.Where(appointment => OnDate(appointment, SelectedDate.Date) &&
            (ResourceKindFilter is null || appointment.ResourceIds.Any(resourceIds.Contains)))
            .OrderByDescending(static appointment => appointment.IsAllDay).ThenBy(static appointment => appointment.Start).ToArray();
        foreach (var appointment in appointments)
            entries.Children.Add(CreateAppointment(appointment, AppointmentSurface.Timed));
        if (appointments.Length == 0)
            entries.Children.Add(Label(Text("NoEvents"), true));
        var scroll = CreateScroll(entries);
        scroll.Name = "PART_CalendarAppointmentScroll";
        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Grid.SetRow(scroll, 1);
        detail.Children.Add(scroll);
        Grid.SetColumn(detail, 1);
        root.Children.Add(detail);
        return root;
    }

    private int CalendarWeekNumber(DateTime date)
    {
        var day = date.Ticks / TimeSpan.TicksPerDay;
        var year = date.Year;
        var first = FirstWeekStart(new DateTime(year, 1, 1).Ticks / TimeSpan.TicksPerDay);
        if (day < first && year > 1)
            first = FirstWeekStart(new DateTime(year - 1, 1, 1).Ticks / TimeSpan.TicksPerDay);
        else if (year < 9999)
        {
            var next = FirstWeekStart(new DateTime(year + 1, 1, 1).Ticks / TimeSpan.TicksPerDay);
            if (day >= next) first = next;
        }
        return (int)((day - first) / 7 + 1);
    }

    private void OnCalendarMonthChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is DaisyButton { Tag: int direction })
        {
            SelectedDates = null;
            SelectedDate = SelectedDate.AddMonths(direction);
        }
    }

    private void OnCalendarDateSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is DaisyButton { Tag: CalendarDate target })
        {
            SelectedDates = null;
            SelectedDate = new DateTimeOffset(target.Date);
        }
    }
}
