using Flowery.Controls.Scheduler.Models;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    internal sealed record ToolbarAction(SchedulerToolbarButton Button, bool Adornment);

    /// <summary>Raised when what the toolbars of this scheduler show has changed.</summary>
    internal event EventHandler? ToolbarChanged;

    internal static string Text(string key) => FloweryLocalization.GetStringInternal("Scheduler_" + key);

    internal double ToolbarCornerRadius => Token<CornerRadius>($"DaisySize{SizeKey}CornerRadius").TopLeft;

    internal CornerRadius ToolbarButtonCornerRadius => Token<CornerRadius>("DaisySizeSmallCornerRadius");

    internal double ToolbarButtonHeight => Token<double>($"DaisySize{SizeKey}Height");

    internal double ToolbarTitleFontSize => Token<double>($"DaisySize{SizeKey}HeaderFontSize");

    internal double ToolbarIconSize => Math.Round(Token<double>("DaisySchedulerToolbarIconSize") * SizeScale);

    private void UpdateToolbar()
    {
        if (_toolbar is not null)
        {
            // The template binds the margin once; the token set here follows host overrides.
            _toolbar.Margin = Token<Thickness>("DaisySchedulerToolbarMargin");
            _toolbar.Visibility = ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        }
        ToolbarChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The title of the shown dates: the emphasized part, such as the month, and the rest, such as the year.</summary>
    // The month or the days lead in bold and the year follows muted. A culture that writes the year first keeps it first
    // in one text: for the month, and for days where it also writes the month before the day (such as Japanese or
    // Chinese). Days are written in the culture's order of month and day, with the month abbreviated. A single day
    // keeps the culture's long date with its weekday.
    internal (string Primary, string Secondary) ToolbarTitle()
    {
        var format = Culture.DateTimeFormat;
        var yearMonth = format.YearMonthPattern;
        var yearFirst = yearMonth.IndexOf('y') < yearMonth.IndexOf('M');
        if (CurrentView == SchedulerView.Month)
            return yearFirst
                ? (SelectedDate.ToString(yearMonth, Culture), string.Empty)
                : (SelectedDate.ToString("MMMM", Culture), SelectedDate.ToString("yyyy", Culture));
        var dates = VisibleDates();
        var (first, last) = (dates[0], dates[^1]);
        if (dates.Length == 1)
            return (first.ToString("D", Culture), string.Empty);
        var monthDay = format.MonthDayPattern.Replace("MMMM", "MMM");
        // Years as the culture's calendar counts them, which need not change on 1 January.
        var sameYear = format.Calendar.GetYear(first) == format.Calendar.GetYear(last);
        // Only a culture that writes year, month and day in this order gets the year before the days; one that writes the
        // year first but the day before the month (such as Persian) keeps the year after them.
        if (yearFirst && monthDay.StartsWith('M'))
        {
            // The year as the culture writes it before the month, such as "yyyy年".
            var withYear = yearMonth[..yearMonth.IndexOf('M')] + monthDay;
            return (first.ToString(withYear, Culture) + " – " + last.ToString(sameYear ? monthDay : withYear, Culture), string.Empty);
        }
        var years = sameYear
            ? last.ToString("yyyy", Culture)
            : first.ToString("yyyy", Culture) + " – " + last.ToString("yyyy", Culture);
        return (first.ToString(monthDay, Culture) + " – " + last.ToString(monthDay, Culture), years);
    }

    internal void GoToToday()
    {
        SelectedDates = null;
        SelectedDate = new DateTimeOffset(DateTime.Today);
    }

    internal void ShowDatePicker(FrameworkElement anchor)
    {
        var calendar = new CalendarView
        {
            SelectionMode = CalendarViewSelectionMode.Single,
            FirstDayOfWeek = (Windows.Globalization.DayOfWeek)FirstDayOfWeek
        };
        calendar.SelectedDates.Add(SelectedDate);
        calendar.SetDisplayDate(SelectedDate);
        calendar.SelectedDatesChanged += OnDatePicked;
        new Flyout { Content = calendar }.ShowAt(anchor);
    }

    internal void ShowSettings(FrameworkElement anchor) =>
        new Flyout { Content = BuildSettings(), FlyoutPresenterStyle = WithTooltipPadding(SchedulerStyle("DaisySchedulerFlyoutPresenterStyle"), typeof(FlyoutPresenter)) }
            .ShowAt(anchor);

    internal void SetToolbarTooltip(FrameworkElement target, string? text) => SetSchedulerTooltip(target, text);

    internal MenuFlyout CreateToolbarMenu(SchedulerToolbarButton model, bool adornment)
    {
        var menu = new MenuFlyout();
        foreach (var entry in model.MenuItems ?? Array.Empty<SchedulerToolbarButton>())
            menu.Items.Add(CreateMenuItem(entry, adornment));
        return menu;
    }

    private MenuFlyoutItemBase CreateMenuItem(SchedulerToolbarButton model, bool adornment)
    {
        if (model.MenuItems is { Count: > 0 } entries)
        {
            var submenu = new MenuFlyoutSubItem { Text = model.Label, IsEnabled = model.IsEnabled, Tag = new ToolbarAction(model, adornment) };
            foreach (var entry in entries)
                submenu.Items.Add(CreateMenuItem(entry, adornment));
            return submenu;
        }
        var item = new MenuFlyoutItem { Text = model.Label, IsEnabled = model.IsEnabled, Tag = new ToolbarAction(model, adornment) };
        item.Click += OnCustomMenuClick;
        return item;
    }

    private void OnCustomMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ToolbarAction action })
            InvokeToolbarAction(action);
    }

    internal void InvokeToolbarAction(ToolbarAction action)
    {
        if (action.Button.Command is { } command && command.CanExecute(action.Button.CommandParameter))
            command.Execute(action.Button.CommandParameter);
        if (action.Adornment)
            AdornmentToggleRequested?.Invoke(this, action.Button);
        else
            ToolbarButtonClicked?.Invoke(this, action.Button);
    }

    private void OnDatePicked(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs e)
    {
        if (e.AddedDates is [var date, ..])
        {
            SelectedDates = null;
            SelectedDate = new DateTimeOffset(date.Date);
        }
    }

    private FrameworkElement BuildSettings()
    {
        var settings = new StackPanel { Spacing = SpacingXS, MinWidth = Token<double>("DaisySchedulerSettingsMinimumWidth") };
        settings.Children.Add(SettingCheckBox("ShowTiles", ShowTiles));
        settings.Children.Add(SettingCheckBox("ShowWeekends", ShowWeekends));
        settings.Children.Add(SettingCheckBox("HideIdleResources", HideIdleResources));
        settings.Children.Add(SettingCheckBox("GroupByDay", GroupMode == SchedulerGroupMode.ByDay));
        AddPreferences(settings);
        var increments = new[]
        {
            string.Format(Culture, Text("Minutes"), 15),
            string.Format(Culture, Text("Minutes"), 30),
            string.Format(Culture, Text("Minutes"), 60)
        };
        var scale = new DaisySelect { Size = ResolvedSize, Tag = "TimeScale", ItemsSource = increments, SelectedIndex = SnapMinutes <= 15 ? 0 : SnapMinutes <= 30 ? 1 : 2 };
        scale.SelectionChanged += OnTimeScaleChanged;
        settings.Children.Add(Label(Text("TimeScale")));
        settings.Children.Add(scale);
        foreach (var option in _tileOptions)
        {
            var check = new DaisyCheckBox { Content = option.Label, IsChecked = option.IsOn, Tag = option, Size = ResolvedSize };
            check.Checked += OnTileOptionChanged;
            check.Unchecked += OnTileOptionChanged;
            settings.Children.Add(check);
        }
        return new ScrollViewer
        {
            Content = settings, MaxHeight = Token<double>("DaisySchedulerSettingsMaximumHeight"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private DaisyCheckBox SettingCheckBox(string key, bool value)
    {
        var check = new DaisyCheckBox { Content = Text(key), IsChecked = value, Tag = key, Size = ResolvedSize };
        check.Checked += OnSettingChanged;
        check.Unchecked += OnSettingChanged;
        return check;
    }

    private void OnSettingChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not DaisyCheckBox { Tag: string key } check)
            return;
        var value = check.IsChecked == true;
        switch (key)
        {
            case "ShowTiles": ShowTiles = value; break;
            case "ShowWeekends": ShowWeekends = value; break;
            case "HideIdleResources": HideIdleResources = value; break;
            case "GroupByDay": GroupMode = value ? SchedulerGroupMode.ByDay : SchedulerGroupMode.ByResource; break;
        }
    }

    private void OnTimeScaleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DaisySelect { SelectedIndex: >= 0 } select)
            TimeIncrement = new TimeIncrementConfig
            {
                MinorTick = TimeSpan.FromMinutes(select.SelectedIndex switch { 0 => 15, 1 => 30, _ => 60 }),
                MajorTick = TimeIncrement?.MajorTick ?? TimeSpan.FromHours(1),
                PixelsPerMinute = TimeIncrement?.PixelsPerMinute ?? 0.8
            };
    }

    private void OnTileOptionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is DaisyCheckBox { Tag: SchedulerTileOption option } check)
            TileOptionToggled?.Invoke(this, new SchedulerTileOption { Key = option.Key, Label = option.Label, IsOn = check.IsChecked == true });
    }
}
