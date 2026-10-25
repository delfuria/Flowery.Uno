using Flowery.Controls.Scheduler.Models;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    public static readonly DependencyProperty WeekRuleProperty =
        Register(nameof(WeekRule), CalendarWeekRule.FirstFourDayWeek, static value => Enum.IsDefined(value));

    /// <summary>First week of a week-numbering year. Combined with FirstDayOfWeek; defaults to ISO rules.</summary>
    public CalendarWeekRule WeekRule
    {
        get => Validated<CalendarWeekRule>(WeekRuleProperty);
        set => SetValidated(WeekRuleProperty, value);
    }

    public static readonly DependencyProperty ShowWeekendsProperty =
        Register(nameof(ShowWeekends), true);

    /// <summary>Shows Saturday and Sunday in the Week, Month and Planner views. Other views always show every date.</summary>
    public bool ShowWeekends
    {
        get => (bool)GetValue(ShowWeekendsProperty);
        set => SetValue(ShowWeekendsProperty, value);
    }

    private bool IsShownDay(DateTime date) => ShowWeekends || date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static readonly DependencyProperty DateTimeFormatProperty =
        Register(nameof(DateTimeFormat), SchedulerDateTimeFormat.Locale, static value => Enum.IsDefined(value));

    public SchedulerDateTimeFormat DateTimeFormat
    {
        get => Validated<SchedulerDateTimeFormat>(DateTimeFormatProperty);
        set => SetValidated(DateTimeFormatProperty, value);
    }

    public static readonly DependencyProperty ClockFormatProperty =
        Register(nameof(ClockFormat), SchedulerClockFormat.Locale, static value => Enum.IsDefined(value));

    public SchedulerClockFormat ClockFormat
    {
        get => Validated<SchedulerClockFormat>(ClockFormatProperty);
        set => SetValidated(ClockFormatProperty, value);
    }

    public static readonly DependencyProperty AppointmentColorsProperty =
        Register<IReadOnlyList<Color>?>(nameof(AppointmentColors), null);

    /// <summary>Optional host editor palette. Null or empty hides the palette in the Gallery editor.
    /// A null appointment BackColor always means the theme default.</summary>
    public IReadOnlyList<Color>? AppointmentColors
    {
        get => (IReadOnlyList<Color>?)GetValue(AppointmentColorsProperty);
        set => SetValue(AppointmentColorsProperty, value);
    }

    public static readonly DependencyProperty TimeSlotStartProperty =
        Register(nameof(TimeSlotStart), TimeSpan.FromHours(7),
            static value => value >= TimeSpan.Zero && value < TimeSpan.FromDays(1));

    /// <summary>First time offered to host editors by GetTimeSlots(). Does not restrict appointment data.</summary>
    public TimeSpan TimeSlotStart
    {
        get => Validated<TimeSpan>(TimeSlotStartProperty);
        set => SetValidated(TimeSlotStartProperty, value);
    }

    public static readonly DependencyProperty TimeSlotEndProperty =
        Register(nameof(TimeSlotEnd), TimeSpan.FromHours(17),
            static value => value >= TimeSpan.Zero && value < TimeSpan.FromDays(1));

    /// <summary>Last offered time, included if it lies on the configured interval.</summary>
    public TimeSpan TimeSlotEnd
    {
        get => Validated<TimeSpan>(TimeSlotEndProperty);
        set => SetValidated(TimeSlotEndProperty, value);
    }

    public static readonly DependencyProperty TimeSlotIntervalProperty =
        Register(nameof(TimeSlotInterval), TimeSpan.FromMinutes(30),
            static value => value >= TimeSpan.FromMinutes(1) && value <= TimeSpan.FromDays(1));

    /// <summary>Editor time step. Independent of TimeIncrement, which controls grid snapping.</summary>
    public TimeSpan TimeSlotInterval
    {
        get => Validated<TimeSpan>(TimeSlotIntervalProperty);
        set => SetValidated(TimeSlotIntervalProperty, value);
    }

    /// <summary>Culture for date/time display and host editor parsing. UI translations use FloweryLocalization.</summary>
    public CultureInfo FormattingCulture => DateTimeFormat switch
    {
        SchedulerDateTimeFormat.US => CultureInfo.GetCultureInfo("en-US"),
        SchedulerDateTimeFormat.UK => CultureInfo.GetCultureInfo("en-GB"),
        SchedulerDateTimeFormat.European => CultureInfo.GetCultureInfo("de-DE"),
        _ => CultureInfo.GetCultureInfo(FloweryLocalization.CurrentCultureName)
    };

    private string TimePattern => ClockFormat switch
    {
        SchedulerClockFormat.TwelveHour => "h:mm tt",
        SchedulerClockFormat.TwentyFourHour => "HH:mm",
        _ => FormattingCulture.DateTimeFormat.ShortTimePattern
    };

    /// <summary>Formats a time with this scheduler's clock and regional overrides.</summary>
    public string FormatTime(DateTime time) => time.ToString(TimePattern, FormattingCulture);

    /// <summary>Formats an editor date using the selected regional convention.</summary>
    public string FormatDate(DateTime date) => date.ToString("d", FormattingCulture);

    /// <summary>Returns the inclusive editor time sequence. Does not round or change appointments.</summary>
    /// <exception cref="InvalidOperationException">TimeSlotEnd precedes TimeSlotStart.</exception>
    public IReadOnlyList<TimeSpan> GetTimeSlots()
    {
        if (TimeSlotEnd < TimeSlotStart)
            throw new InvalidOperationException("TimeSlotEnd must not precede TimeSlotStart.");
        var slots = new List<TimeSpan>();
        for (var time = TimeSlotStart; time <= TimeSlotEnd; time += TimeSlotInterval)
            slots.Add(time);
        return slots.ToArray();
    }
}
