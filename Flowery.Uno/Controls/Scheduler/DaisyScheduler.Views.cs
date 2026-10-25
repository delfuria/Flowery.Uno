// Uno Platform adaptation of Xtreme.Components Scheduler (MIT).
// See ThirdPartyNotices/Xtreme.Components.txt for attribution and license.
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Flowery.Controls.Scheduler.Utilities;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    private FrozenPane? _headerPane;
    private FrozenPane? _gutterPane;
    private readonly List<Lane> _lanes = new();

    /// <summary>A Planner or Timeline element placed by minutes along the shown days. A width of NaN minutes
    /// only places the element.</summary>
    private sealed record HourItem(FrameworkElement Element, double Start, double Minutes, double Offset, double Trim, double MinimumWidth);

    private readonly List<HourItem> _hourItems = new();
    private int _hourDayCount;
    private double _hourGutterWidth;
    private double _hourMinimum;

    private void PlaceByMinutes(FrameworkElement element, double start, double minutes, double offset = 0, double trim = 0, double minimumWidth = 0)
    {
        var item = new HourItem(element, start, minutes, offset, trim, minimumWidth);
        _hourItems.Add(item);
        Place(item, _horizontalPixelsPerMinute);
    }

    private static void Place(HourItem item, double pixelsPerMinute)
    {
        Canvas.SetLeft(item.Element, item.Start * pixelsPerMinute + item.Offset);
        if (!double.IsNaN(item.Minutes))
            item.Element.Width = Math.Max(item.MinimumWidth, item.Minutes * pixelsPerMinute - item.Trim);
    }

    /// <summary>Rescales the Planner or Timeline to a new visible width without rendering it again.</summary>
    private void RescaleHours()
    {
        if (_hourItems.Count == 0 || _renderedView is not (SchedulerView.Planner or SchedulerView.Timeline))
            return;
        var pixelsPerMinute = FillPixelsPerMinute(_hourMinimum, _hourDayCount, _hourGutterWidth);
        if (pixelsPerMinute == _horizontalPixelsPerMinute)
            return;
        _horizontalPixelsPerMinute = pixelsPerMinute;
        foreach (var item in _hourItems)
            Place(item, pixelsPerMinute);
        foreach (var lane in _lanes)
            if (lane.Horizontal)
                lane.PixelsPerMinute = pixelsPerMinute;
    }

    /// <param name="Days">The dates on a Planner axis that hides weekends; null when the axis is continuous.</param>
    private sealed record Lane(Canvas Surface, DateTime Origin, ResourceModel? Resource,
        bool Horizontal, double PixelsPerMinute, int DayCount = 1, IReadOnlyList<DateTime>? Days = null)
    {
        // Planner and Timeline lanes rescale when the visible width changes.
        public double PixelsPerMinute { get; set; } = PixelsPerMinute;
    }

    // A Planner without weekends draws only the shown dates: axis time counts their minutes alone.
    private static DateTime AxisTime(DateTime time, IReadOnlyList<DateTime> days)
    {
        var minutes = 0d;
        foreach (var day in days)
            minutes += Math.Clamp((time - day).TotalMinutes, 0, 1440);
        return days[0].AddMinutes(minutes);
    }

    private static DateTime RealTime(DateTime axisTime, IReadOnlyList<DateTime> days, bool end)
    {
        var minutes = (axisTime - days[0]).TotalMinutes;
        var index = (int)Math.Clamp(Math.Floor(minutes / 1440), 0, days.Count - 1);
        // An end on a day boundary closes the earlier date instead of opening the next shown one.
        if (end && index > 0 && minutes == index * 1440)
            index--;
        return days[index].AddMinutes(minutes - index * 1440);
    }

    /// <summary>Moves a drawn copy onto the shortened axis; false when it lies on hidden dates only.</summary>
    private static bool MapToAxis(AppointmentModel copy, IReadOnlyList<DateTime> days)
    {
        var zeroLength = copy.End <= copy.Start;
        var onShownDate = days.Contains(copy.Start.Date);
        copy.Start = AxisTime(copy.Start, days);
        copy.End = AxisTime(copy.End, days);
        return zeroLength ? onShownDate : copy.End > copy.Start;
    }

    private IReadOnlyList<ResourceModel> ActiveResources(DateTime start, DateTime end)
    {
        var resources = (SchedulerResources ?? Array.Empty<ResourceModel>())
            .Where(resource => ResourceKindFilter is null || resource.Kind == ResourceKindFilter).ToArray();
        if (!HideIdleResources)
            return resources;
        if (CurrentView is SchedulerView.Planner or SchedulerView.Timeline)
        {
            // A Planner without weekends counts only bookings on the dates it shows.
            var ranges = CurrentView == SchedulerView.Planner
                ? VisibleDates().Select(static date => (Start: date, End: date.AddDays(1)))
                : new[] { (Start: start, End: end) };
            var occupied = ranges.SelectMany(range => GetOccupiedResources(range.Start, range.End, ResourceKindFilter)).ToHashSet();
            return resources.Where(occupied.Contains).ToArray();
        }
        // Day/Week draw stored times unchanged, including for resources with a time zone.
        var dates = VisibleDates();
        return resources.Where(resource => Items.Any(appointment => !appointment.IsCancelled &&
            MatchesResource(appointment, resource) && dates.Any(date => OnDate(appointment, date)))).ToArray();
    }

    private Border Cell(UIElement child, bool header = false, bool today = false) => new()
    {
        Child = child,
        Padding = new Thickness(SpacingXS),
        Background = Brush(today ? "DaisySchedulerTodayBrush" : header ? "DaisyBase200Brush" : "DaisyBase100Brush"),
        BorderBrush = Brush("DaisyBase300Brush"),
        BorderThickness = new Thickness(0, 0, LineThickness, LineThickness)
    };

    private FrameworkElement BuildMonth()
    {
        var month = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        var start = WeekStart(month, FirstDayOfWeek);
        // Day offsets within a week that get a column; ShowWeekends removes Saturday and Sunday.
        var columns = Enumerable.Range(0, 7).Where(day => IsShownDay(start.AddDays(day))).ToArray();
        var days = Enumerable.Range(0, 42).Where(day => Array.IndexOf(columns, day % 7) >= 0).ToArray();
        var maxAppointments = days.Max(day => Items.Count(appointment => OnDate(appointment, start.AddDays(day))));
        var minimumHeight = 6 * Math.Max(Token<double>("DaisySchedulerMonthMinimumCellHeight") * SizeScale,
            Token<double>($"DaisySize{SizeKey}Height") + maxAppointments * (ChipHeight + SpacingXS / 2)) + ChipHeight;
        var grid = WithDefinitions(new Grid
        {
            MinWidth = Token<double>("DaisySchedulerMonthMinimumWidth") * SizeScale,
            Width = Math.Max(Token<double>("DaisySchedulerMonthMinimumWidth") * SizeScale, ActualWidth - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right - SpacingLarge),
            MinHeight = minimumHeight,
            Height = minimumHeight
        }, string.Join(",", columns.Select(static _ => "*")), "Auto,*,*,*,*,*,*");
        _monthGrid = grid;
        for (var column = 0; column < columns.Length; column++)
        {
            var text = Label(start.AddDays(columns[column]).ToString("ddd", Culture), true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            var header = Cell(text, header: true);
            Grid.SetColumn(header, column);
            grid.Children.Add(header);
        }
        foreach (var index in days)
        {
            var date = start.AddDays(index);
            var stack = new StackPanel { Spacing = 2, Margin = new Thickness(2) };
            var number = Label(date.Day.ToString(Culture), true);
            if (date.Month != month.Month)
                number.Foreground = Brush("DaisyBaseContentDisabledBrush");
            if (date == DateTime.Today)
            {
                number.Foreground = Brush("DaisyPrimaryForegroundBrush");
                number.FontWeight = FontWeights.Bold;
            }
            stack.Children.Add(number);
            var appointments = Items.Where(appointment => OnDate(appointment, date))
                .OrderByDescending(static appointment => appointment.IsAllDay).ThenBy(static appointment => appointment.Start).ToArray();
            foreach (var appointment in appointments)
                stack.Children.Add(CreateAppointment(appointment, AppointmentSurface.Chip));
            var cell = ClipToBounds(Cell(stack, today: date == DateTime.Today));
            AttachEmptyDay(cell, date, null);
            Grid.SetColumn(cell, Array.IndexOf(columns, index % 7));
            Grid.SetRow(cell, index / 7 + 1);
            grid.Children.Add(cell);
        }
        return CreateScroll(grid);
    }

    private FrameworkElement BuildAgenda()
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (var date in VisibleDates())
        {
            var title = Label(date.ToString("D", Culture));
            title.FontWeight = FontWeights.SemiBold;
            stack.Children.Add(Cell(title, header: true, today: date == DateTime.Today));
            var appointments = Items.Where(appointment => OnDate(appointment, date))
                .OrderBy(static appointment => appointment.Start).ToArray();
            if (appointments.Length == 0)
                stack.Children.Add(Cell(Label(Text("NoEvents"), true)));
            foreach (var appointment in appointments)
            {
                var row = WithDefinitions(new Grid { ColumnSpacing = 12, Margin = new Thickness(8, 4, 8, 4) }, "Auto,*");
                var time = Label(appointment.IsAllDay ? Text("AllDay") : FormatTime(appointment.Start) + " – " + FormatTime(appointment.End), true);
                time.MinWidth = Token<double>("DaisySchedulerAgendaTimeWidth") * SizeScale;
                row.Children.Add(time);
                var detail = new StackPanel { Spacing = 4 };
                detail.Children.Add(CreateAppointment(appointment, AppointmentSurface.Chip));
                var resources = SchedulerResources ?? Array.Empty<ResourceModel>();
                var names = resources.Where(resource => appointment.ResourceIds.Contains(resource.Id)).Select(static resource => resource.Name);
                detail.Children.Add(Label(string.Join(", ", names), true));
                Grid.SetColumn(detail, 1);
                row.Children.Add(detail);
                stack.Children.Add(Cell(row));
            }
        }
        return CreateScroll(stack);
    }

    private FrameworkElement BuildTimeGrid()
    {
        var dates = VisibleDates();
        var resources = ActiveResources(dates[0], dates[^1].AddDays(1));
        var columns = new List<(DateTime Date, ResourceModel? Resource)>();
        if (SchedulerResources is { Count: > 0 } && resources.Count == 0)
            return Label(Text("NoEvents"));
        if (resources.Count == 0)
            columns.AddRange(dates.Select(static date => (date, (ResourceModel?)null)));
        else if (GroupMode == SchedulerGroupMode.ByResource)
        {
            foreach (var resource in resources)
                foreach (var date in dates)
                    columns.Add((date, resource));
        }
        else
        {
            foreach (var date in dates)
                foreach (var resource in resources)
                    columns.Add((date, resource));
        }
        var body = new Grid { Height = 1440 * PixelsPerMinute };
        var gutter = new Canvas { Height = body.Height };
        var tick = Math.Clamp(TimeIncrement?.MajorTick.TotalMinutes ?? 60, 1, 1440);
        for (var minute = 0d; minute < 1440; minute += tick)
        {
            var label = Label(FormatTime(dates[0].AddMinutes(minute)), true);
            Canvas.SetTop(label, minute * PixelsPerMinute);
            gutter.Children.Add(label);
        }
        // Size the gutter to its widest label and the corner caption, as the Timeline sizes its hours.
        var gutterWidth = Math.Max(Token<double>("DaisySchedulerTimeGutterMinimumWidth") * SizeScale, FontSize * 4);
        foreach (var label in gutter.Children.OfType<TextBlock>().Append(Label(Text("AllDay"), true)))
        {
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            gutterWidth = Math.Max(gutterWidth, RoundToPixel(label.DesiredSize.Width + SpacingXS * 2 + LineThickness));
        }
        gutter.Width = gutterWidth;
        var width = Math.Max((ColumnMinWidth ?? Token<double>("DaisySchedulerColumnMinimumWidth")) * SizeScale,
            (ActualWidth - gutterWidth - SpacingLarge - SpacingXS) / columns.Count);
        var header = new Grid();
        for (var index = 0; index < columns.Count; index++)
        {
            var (date, resource) = columns[index];
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            var heading = new StackPanel { Spacing = 4 };
            heading.Children.Add(Label(date.ToString("ddd, d MMM", Culture)));
            if (resource is not null)
                heading.Children.Add(Label(resource.Name, true));
            var banners = new StackPanel { Spacing = 2, MinHeight = ChipHeight };
            foreach (var appointment in Items.Where(appointment => IsBanner(appointment) && OnDate(appointment, date) && MatchesResource(appointment, resource)))
                banners.Children.Add(CreateAppointment(appointment, AppointmentSurface.Chip, resourceId: resource?.Id));
            var allDay = new Border { Background = Transparent, Child = banners };
            AttachEmptyDay(allDay, date, resource);
            heading.Children.Add(allDay);
            var headerCell = Cell(heading, header: true, today: date == DateTime.Today);
            Grid.SetColumn(headerCell, index);
            header.Children.Add(headerCell);

            var canvas = new Canvas { Width = width, Height = body.Height, Background = Brush("DaisyBase100Brush") };
            var lane = new Lane(canvas, date, resource, false, PixelsPerMinute);
            _lanes.Add(lane);
            if (WorkDayEnd > WorkDayStart)
            {
                AddRectangle(canvas, 0, 0, width, WorkDayStart.TotalMinutes * PixelsPerMinute, Brush("DaisyBase200Brush"));
                AddRectangle(canvas, 0, WorkDayEnd.TotalMinutes * PixelsPerMinute, width,
                    (1440 - WorkDayEnd.TotalMinutes) * PixelsPerMinute, Brush("DaisyBase200Brush"));
            }
            if (date == DateTime.Today)
                AddRectangle(canvas, 0, 0, width, body.Height, Brush("DaisySchedulerTodayBrush"));
            for (var minute = 0d; minute < 1440; minute += SnapMinutes)
                AddRectangle(canvas, 0, minute * PixelsPerMinute, width, 1, Brush("DaisyBase300Brush"));
            var timed = Items.Where(appointment => !IsBanner(appointment) && MatchesResource(appointment, resource));
            var layouts = EventBlockLayout.Layout(date, timed, Math.Max(15, ChipHeight / PixelsPerMinute));
            var blocks = new List<(Border Frame, EventLayout Layout)>();
            var minimumWidth = (ColumnMinWidth ?? Token<double>("DaisySchedulerColumnMinimumWidth")) * SizeScale;
            foreach (var layout in layouts)
            {
                minimumWidth = Math.Max(minimumWidth, layout.ColumnCount * TimedAppointmentMinimumWidth(layout.Appointment));
                var block = CreateAppointment(layout.Appointment, AppointmentSurface.Timed, lane);
                block.Width = Math.Max(1, width / layout.ColumnCount - 4);
                block.Height = Math.Max(ChipHeight, (layout.EndMinutes - layout.StartMinutes) * PixelsPerMinute - 2);
                Canvas.SetLeft(block, layout.ColumnIndex * width / layout.ColumnCount + 2);
                Canvas.SetTop(block, layout.StartMinutes * PixelsPerMinute);
                canvas.Children.Add(block);
                blocks.Add((block, layout));
            }
            if (date == DateTime.Today)
                AddRectangle(canvas, 0, DateTime.Now.TimeOfDay.TotalMinutes * PixelsPerMinute, width, 2, Brush("DaisyPrimaryBrush"));
            AttachLane(lane);
            var wrapper = new Border { Child = canvas, BorderBrush = Brush("DaisyBase300Brush"), BorderThickness = new Thickness(0, 0, 1, 0) };
            Grid.SetColumn(wrapper, index);
            body.Children.Add(wrapper);
            _timeColumns.Add(new TimeColumn(header.ColumnDefinitions[index], body.ColumnDefinitions[index], canvas,
                blocks, canvas.Children.OfType<Border>().Where(static child => !child.IsHitTestVisible).ToArray(), minimumWidth));
        }
        return FrozenGrid(header, gutter, body, gutterWidth);
    }

    private FrameworkElement BuildPlanner()
    {
        var dayCount = CurrentView == SchedulerView.Timeline ? 1 : 7;
        var start = dayCount == 1 ? SelectedDate.Date : WeekStart(SelectedDate.Date, FirstDayOfWeek);
        var end = start.AddDays(dayCount);
        var resources = ActiveResources(start, end);
        if (resources.Count == 0)
            return Label(Text("NoResources"));
        // Without weekends the axis holds only the shown dates; null keeps the continuous week.
        var days = dayCount == 7 && VisibleDates() is { Length: < 7 } shownDays ? shownDays : null;
        if (days is not null)
        {
            start = days[0];
            dayCount = days.Length;
            end = start.AddDays(dayCount);
        }
        var gutterWidth = Token<double>(CurrentView == SchedulerView.Timeline ? "DaisySchedulerTimelineResourceWidth" : "DaisySchedulerPlannerResourceWidth") * SizeScale;
        _hourDayCount = dayCount;
        _hourGutterWidth = gutterWidth;
        var minimum = _hourMinimum = MinimumPixelsPerMinute();
        var ppm = _horizontalPixelsPerMinute = FillPixelsPerMinute(minimum, dayCount, gutterWidth);
        var totalMinutes = dayCount * 1440d;
        var rowHeight = RowHeight;
        var header = new Canvas { Height = ChipHeight * 2 };
        PlaceByMinutes(header, 0, totalMinutes);
        for (var day = 0; day < dayCount; day++)
        {
            var date = days?[day] ?? start.AddDays(day);
            var dayHeader = Cell(Label(date.ToString("ddd, d MMM", Culture)), header: true, today: date == DateTime.Today);
            dayHeader.Height = ChipHeight;
            PlaceByMinutes(dayHeader, day * 1440, 1440);
            header.Children.Add(dayHeader);
            for (var hour = 0; hour < 24; hour++)
            {
                var label = Label(FormatTime(date.AddHours(hour)), true);
                PlaceByMinutes(label, day * 1440 + hour * 60, double.NaN, offset: 4);
                Canvas.SetTop(label, ChipHeight);
                header.Children.Add(label);
            }
        }
        var body = new StackPanel();
        PlaceByMinutes(body, 0, totalMinutes);
        var gutter = new StackPanel { Width = gutterWidth };
        foreach (var rowGroup in TimelineGroups(resources, start))
        {
            if (rowGroup.Name is not null)
            {
                var group = Cell(GroupHeading(rowGroup), header: true);
                group.Height = rowHeight;
                group.Padding = new Thickness(0);
                gutter.Children.Add(group);
                body.Children.Add(new Border { Height = rowHeight, Background = Brush("DaisyBase200Brush"),
                    BorderBrush = Brush("DaisyBase300Brush"), BorderThickness = new Thickness(0, 0, 0, LineThickness) });
            }
            if (rowGroup.Model is { } model && CollapsedResourceGroupIds?.Contains(model.Id) == true)
                continue;
            foreach (var resource in rowGroup.Resources)
            {
                var shown = new List<AppointmentModel>();
                var originals = new Dictionary<AppointmentModel, AppointmentModel>();
                foreach (var appointment in Items.Where(appointment => MatchesResource(appointment, resource)))
                {
                    var copy = appointment.IsAllDay ? appointment.Clone() : TimeZoneConverter.ToZone(appointment, resource.TimeZoneId);
                    if (rowGroup.Date is { } groupDate && !OnDate(copy, groupDate) || days is not null && !MapToAxis(copy, days))
                        continue;
                    shown.Add(copy);
                    originals[copy] = appointment;
                }
                // Rows are packed for the narrowest hours, so wider hours never make short bars overlap.
                var layouts = EventBlockLayout.LayoutRange(start, end, shown, ChipHeight / minimum);
                var rows = new List<AppointmentModel>();
                foreach (var layout in layouts)
                {
                    var original = OriginalAppointment(originals[layout.Appointment]);
                    if (!rows.Contains(original))
                        rows.Add(original);
                }
                var height = Math.Max(1, rows.Count) * rowHeight;
                var canvas = new Canvas { Height = height, Background = Brush("DaisyBase100Brush") };
                PlaceByMinutes(canvas, 0, totalMinutes);
                var lane = new Lane(canvas, start, resource, true, ppm, dayCount, days);
                _lanes.Add(lane);
                for (var hour = 0; hour < dayCount * 24; hour++)
                    if (AddRectangle(canvas, 0, 0, LineThickness, height, Brush("DaisyBase300Brush")) is { } line)
                        PlaceByMinutes(line, hour * 60, double.NaN);
                for (var row = 1; row <= Math.Max(1, rows.Count); row++)
                    if (AddRectangle(canvas, 0, row * rowHeight - LineThickness, totalMinutes * ppm, LineThickness, Brush("DaisyBase300Brush")) is { } line)
                        PlaceByMinutes(line, 0, totalMinutes);
                foreach (var layout in layouts)
                {
                    var original = OriginalAppointment(originals[layout.Appointment]);
                    var block = CreateAppointment(originals[layout.Appointment], AppointmentSurface.Bar,
                        original.IsAllDay ? null : lane, layout.Appointment, resource.Id);
                    PlaceByMinutes(block, layout.StartMinutes, layout.EndMinutes - layout.StartMinutes, trim: 2, minimumWidth: ChipHeight);
                    block.Height = ChipHeight;
                    Canvas.SetTop(block, rows.IndexOf(original) * rowHeight + (rowHeight - ChipHeight) / 2);
                    canvas.Children.Add(block);
                }
                AttachLane(lane);
                body.Children.Add(canvas);
                var names = new StackPanel();
                if (rows.Count == 0)
                    names.Children.Add(ResourceNameRow(resource.Name));
                foreach (var appointment in rows)
                    names.Children.Add(ResourceNameRow(resource.Name + " · " + appointment.Subject));
                var resourceCell = Cell(names);
                resourceCell.Padding = default;
                resourceCell.Height = height;
                resourceCell.BorderBrush = resource.Color is { } color ? new SolidColorBrush(color) : Brush("DaisyBase300Brush");
                resourceCell.BorderThickness = new Thickness(SpacingXS, 0, 0, 0);
                gutter.Children.Add(resourceCell);
            }
        }
        return FrozenGrid(header, gutter, body, gutter.Width);
    }

    private Border ResourceNameRow(string name) => new()
    {
        Height = RowHeight, Padding = new Thickness(SpacingXS, 0, SpacingXS, 0), Child = Label(name, true),
        BorderBrush = Brush("DaisyBase300Brush"), BorderThickness = new Thickness(0, 0, 0, LineThickness)
    };

    private static bool MatchesResource(AppointmentModel appointment, ResourceModel? resource) =>
        resource is null || appointment.ResourceIds.Contains(resource.Id);

    private static bool IsBanner(AppointmentModel appointment) => appointment.IsAllDay ||
        appointment.End > appointment.Start && appointment.Start.Date != appointment.End.AddTicks(-1).Date;

    private ScrollViewer CreateScroll(FrameworkElement content)
    {
        _bodyScroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };
        _bodyScroll.Loaded += OnBodyLoaded;
        _bodyScroll.SizeChanged += OnBodySizeChanged;
        _bodyScroll.ViewChanged += OnBodyScrolled;
        return _bodyScroll;
    }

    private FrameworkElement FrozenGrid(FrameworkElement header, FrameworkElement gutter, FrameworkElement body, double gutterWidth)
    {
        var root = WithDefinitions(new Grid(), "Auto,*", "Auto,*");
        _headerPane = new FrozenPane(header, horizontal: true);
        _gutterPane = new FrozenPane(gutter, horizontal: false);
        var scroll = CreateScroll(body);
        var corner = Cell(Label(CurrentView is SchedulerView.Planner or SchedulerView.Timeline ? Text("Resources") : Text("AllDay"), true), header: true);
        corner.Width = gutterWidth;
        root.Children.Add(corner);
        Grid.SetColumn(_headerPane, 1);
        root.Children.Add(_headerPane);
        Grid.SetRow(_gutterPane, 1);
        root.Children.Add(_gutterPane);
        Grid.SetColumn(scroll, 1);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        return root;
    }

    private void OnBodySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _bodyScroll))
            RunOutsideLayout(UpdateViewGeometry);
    }

    private void OnBodyScrolled(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _bodyScroll) && _bodyScroll is { } body)
        {
#if __ANDROID__
            // Uno restores the offset of the last ChangeView at every arrange and clears it on touch input only in
            // its managed presenter, so the next layout jumped back to the initial hour after the user had scrolled.
            // A ChangeView to the settled offsets makes them the restored ones without scrolling.
            if (!e.IsIntermediate)
                body.ChangeView(body.HorizontalOffset, body.VerticalOffset, null, true);
#endif
            if (_headerPane is { } header)
                header.Offset = body.HorizontalOffset;
            if (_gutterPane is { } gutter)
                gutter.Offset = body.VerticalOffset;
        }
    }

    private void OnBodyLoaded(object? sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _bodyScroll) || _bodyScroll is null)
            return;
        _bodyScroll.Loaded -= OnBodyLoaded;
        RunOutsideLayout(ShowInitialPosition);
    }

    private void ShowInitialPosition()
    {
        if (_bodyScroll is null)
            return;
        UpdateViewGeometry();
        var initial = CurrentView switch
        {
            SchedulerView.Day or SchedulerView.Week => new Point(0, 7 * 60 * PixelsPerMinute),
            SchedulerView.Planner => new Point(WorkDayStart.TotalMinutes * _horizontalPixelsPerMinute, 0),
            SchedulerView.Timeline => new Point(7 * 60 * _horizontalPixelsPerMinute, 0),
            _ => default
        };
        var offset = _savedOffset ?? initial;
        // The extent is known only after this layout pass; apply the offset once it is.
        _bodyScroll.UpdateLayout();
        _bodyScroll.ChangeView(offset.X, offset.Y, null, true);
    }

    private static Border? AddRectangle(Canvas canvas, double x, double y, double width, double height, Brush fill)
    {
        if (height <= 0 || width <= 0)
            return null;
        var rectangle = new Border { Width = width, Height = height, Background = fill, IsHitTestVisible = false };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
        return rectangle;
    }
}
