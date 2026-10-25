// Uno Platform adaptation of Xtreme.Components Scheduler (MIT).
// Copyright (c) 2026 Data Access Inc. and Xtreme.Components contributors.
// See ThirdPartyNotices/Xtreme.Components.txt for attribution and license.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Flowery.Controls.Scheduler.Contracts;
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Windows.System;

namespace Flowery.Controls;

/// <summary>
/// A Flowery calendar and resource scheduler adapted from Xtreme.Components.
/// Data is read-only input; gestures raise requests that the host may accept.
/// Reassign the appointment list after editing a model to refresh the view.
/// </summary>
[TemplatePart(Name = "PART_ViewHost", Type = typeof(Border))]
[TemplatePart(Name = "PART_Toolbar", Type = typeof(DaisySchedulerToolbar))]
public partial class DaisyScheduler : Control
{
    public static readonly DependencyProperty AppointmentsProperty =
        Register<IReadOnlyList<AppointmentModel>?>(nameof(Appointments), null);

    public IReadOnlyList<AppointmentModel>? Appointments
    {
        get => (IReadOnlyList<AppointmentModel>?)GetValue(AppointmentsProperty);
        set => SetValue(AppointmentsProperty, value);
    }

    public static readonly DependencyProperty SchedulerResourcesProperty =
        Register<IReadOnlyList<ResourceModel>?>(nameof(SchedulerResources), null);

    public IReadOnlyList<ResourceModel>? SchedulerResources
    {
        get => (IReadOnlyList<ResourceModel>?)GetValue(SchedulerResourcesProperty);
        set => SetValue(SchedulerResourcesProperty, value);
    }

    public static readonly DependencyProperty CurrentViewProperty =
        Register(nameof(CurrentView), SchedulerView.Week);

    public SchedulerView CurrentView
    {
        get => (SchedulerView)GetValue(CurrentViewProperty);
        set => SetValue(CurrentViewProperty, value);
    }

    public static readonly DependencyProperty SelectedDateProperty =
        Register(nameof(SelectedDate), default(DateTimeOffset));

    public DateTimeOffset SelectedDate
    {
        get => (DateTimeOffset)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public static readonly DependencyProperty SelectedDatesProperty =
        Register<IReadOnlyList<DateTime>?>(nameof(SelectedDates), null);

    public IReadOnlyList<DateTime>? SelectedDates
    {
        get => (IReadOnlyList<DateTime>?)GetValue(SelectedDatesProperty);
        set => SetValue(SelectedDatesProperty, value);
    }

    public static readonly DependencyProperty VisibleDaysProperty =
        Register(nameof(VisibleDays), 1, static value => value is > 0 and <= 366);

    public int VisibleDays
    {
        get => Validated<int>(VisibleDaysProperty);
        set => SetValidated(VisibleDaysProperty, value);
    }

    public static readonly DependencyProperty FirstDayOfWeekProperty =
        Register(nameof(FirstDayOfWeek), DayOfWeek.Monday, static value => Enum.IsDefined(value));

    public DayOfWeek FirstDayOfWeek
    {
        get => Validated<DayOfWeek>(FirstDayOfWeekProperty);
        set => SetValidated(FirstDayOfWeekProperty, value);
    }

    public static readonly DependencyProperty GroupModeProperty =
        Register(nameof(GroupMode), SchedulerGroupMode.ByResource);

    public SchedulerGroupMode GroupMode
    {
        get => (SchedulerGroupMode)GetValue(GroupModeProperty);
        set => SetValue(GroupModeProperty, value);
    }

    public static readonly DependencyProperty HideIdleResourcesProperty =
        Register(nameof(HideIdleResources), false);

    public bool HideIdleResources
    {
        get => (bool)GetValue(HideIdleResourcesProperty);
        set => SetValue(HideIdleResourcesProperty, value);
    }

    public static readonly DependencyProperty WorkDayStartProperty =
        Register(nameof(WorkDayStart), TimeSpan.FromHours(8), static value => value >= TimeSpan.Zero && value < TimeSpan.FromDays(1));

    public TimeSpan WorkDayStart
    {
        get => Validated<TimeSpan>(WorkDayStartProperty);
        set => SetValidated(WorkDayStartProperty, value);
    }

    public static readonly DependencyProperty WorkDayEndProperty =
        Register(nameof(WorkDayEnd), TimeSpan.FromHours(17), static value => value > TimeSpan.Zero && value <= TimeSpan.FromDays(1));

    public TimeSpan WorkDayEnd
    {
        get => Validated<TimeSpan>(WorkDayEndProperty);
        set => SetValidated(WorkDayEndProperty, value);
    }

    public static readonly DependencyProperty TimeIncrementProperty =
        Register<TimeIncrementConfig?>(nameof(TimeIncrement), null);

    public TimeIncrementConfig? TimeIncrement
    {
        get => (TimeIncrementConfig?)GetValue(TimeIncrementProperty);
        set => SetValue(TimeIncrementProperty, value);
    }

    public static readonly DependencyProperty ColumnMinWidthProperty =
        Register<double?>(nameof(ColumnMinWidth), null,
            static value => value is null || double.IsFinite(value.Value) && value.Value > 0);

    public double? ColumnMinWidth
    {
        get => Validated<double?>(ColumnMinWidthProperty);
        set => SetValidated(ColumnMinWidthProperty, value);
    }

    public static readonly DependencyProperty AppointmentCornerRadiusProperty =
        Register<double?>(nameof(AppointmentCornerRadius), null,
            static value => value is null || double.IsFinite(value.Value) && value.Value >= 0);

    public double? AppointmentCornerRadius
    {
        get => Validated<double?>(AppointmentCornerRadiusProperty);
        set => SetValidated(AppointmentCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        Register(nameof(IsReadOnly), false);

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DependencyProperty SingleClickCreatesProperty =
        Register(nameof(SingleClickCreates), false);

    public bool SingleClickCreates
    {
        get => (bool)GetValue(SingleClickCreatesProperty);
        set => SetValue(SingleClickCreatesProperty, value);
    }

    public static readonly DependencyProperty ShowTilesProperty =
        Register(nameof(ShowTiles), true);

    public bool ShowTiles
    {
        get => (bool)GetValue(ShowTilesProperty);
        set => SetValue(ShowTilesProperty, value);
    }

    public static readonly DependencyProperty ShowToolbarProperty =
        Register(nameof(ShowToolbar), true);

    /// <summary>Whether the built-in <see cref="DaisySchedulerToolbar"/> shows. Turn it off to place a toolbar elsewhere,
    /// with its <see cref="DaisySchedulerToolbar.Scheduler"/> set to this scheduler.</summary>
    public bool ShowToolbar
    {
        get => (bool)GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }

    public static readonly DependencyProperty SizeProperty =
        Register(nameof(Size), DaisySize.Auto);

    /// <summary>The size tier. Auto follows the global size, as in the other Flowery controls.</summary>
    public DaisySize Size
    {
        get => (DaisySize)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The effective size: <see cref="Size"/>, or the global size when it is Auto.</summary>
    public DaisySize ResolvedSize => FlowerySizeManager.ResolveSize(this, Size);

    public static readonly DependencyProperty SelectedAppointmentProperty =
        Register<AppointmentModel?>(nameof(SelectedAppointment), null);

    public AppointmentModel? SelectedAppointment
    {
        get => (AppointmentModel?)GetValue(SelectedAppointmentProperty);
        set => SetValue(SelectedAppointmentProperty, value);
    }

    public static readonly DependencyProperty AppointmentTemplateProperty =
        Register<DataTemplate?>(nameof(AppointmentTemplate), null);

    public DataTemplate? AppointmentTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentTemplateProperty);
        set => SetValue(AppointmentTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentChipTemplateProperty =
        Register<DataTemplate?>(nameof(AppointmentChipTemplate), null);

    public DataTemplate? AppointmentChipTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentChipTemplateProperty);
        set => SetValue(AppointmentChipTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentBarTemplateProperty =
        Register<DataTemplate?>(nameof(AppointmentBarTemplate), null);

    public DataTemplate? AppointmentBarTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentBarTemplateProperty);
        set => SetValue(AppointmentBarTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentTooltipTemplateProperty =
        Register<DataTemplate?>(nameof(AppointmentTooltipTemplate), null);

    public DataTemplate? AppointmentTooltipTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentTooltipTemplateProperty);
        set => SetValue(AppointmentTooltipTemplateProperty, value);
    }

    public static readonly DependencyProperty AppointmentRendererProperty =
        Register<Func<AppointmentContext, FrameworkElement?>?>(nameof(AppointmentRenderer), null);

    public Func<AppointmentContext, FrameworkElement?>? AppointmentRenderer
    {
        get => (Func<AppointmentContext, FrameworkElement?>?)GetValue(AppointmentRendererProperty);
        set => SetValue(AppointmentRendererProperty, value);
    }

    public static readonly DependencyProperty AppointmentTooltipRendererProperty =
        Register<Func<AppointmentContext, FrameworkElement?>?>(nameof(AppointmentTooltipRenderer), null);

    public Func<AppointmentContext, FrameworkElement?>? AppointmentTooltipRenderer
    {
        get => (Func<AppointmentContext, FrameworkElement?>?)GetValue(AppointmentTooltipRendererProperty);
        set => SetValue(AppointmentTooltipRendererProperty, value);
    }

    /// <summary>Read-only: the control sets it whenever the selection changes.</summary>
    public static readonly DependencyProperty SelectedAppointmentsProperty =
        Register<IReadOnlyList<AppointmentModel>>(nameof(SelectedAppointments), Array.Empty<AppointmentModel>());

    public IReadOnlyList<AppointmentModel> SelectedAppointments => (IReadOnlyList<AppointmentModel>)GetValue(SelectedAppointmentsProperty);
    public ObservableCollection<SchedulerToolbarButton> ToolbarButtons { get; } = new();
    public ObservableCollection<SchedulerToolbarButton> ToolbarAdornments { get; } = new();

    public event EventHandler<AppointmentCreateArgs>? AppointmentCreateRequested;
    public event EventHandler<EmptySlotSelectedArgs>? EmptySlotSelected;
    public event EventHandler<AppointmentMoveArgs>? AppointmentMoveRequested;
    public event EventHandler<AppointmentResizeArgs>? AppointmentResizeRequested;
    public event EventHandler<AppointmentsMoveArgs>? AppointmentsMoveRequested;
    public event EventHandler<AppointmentsResizeArgs>? AppointmentsResizeRequested;
    public event EventHandler<AppointmentsDeleteArgs>? AppointmentsDeleteRequested;
    public event EventHandler<ExternalDropArgs>? ExternalAppointmentDropped;
    public event EventHandler<AppointmentEditRequestedArgs>? AppointmentEditRequested;
    public event EventHandler<AppointmentContextRequestedArgs>? AppointmentContextRequested;
    public event EventHandler<IReadOnlyList<AppointmentModel>>? SelectionChanged;
    public event EventHandler<SchedulerToolbarButton>? ToolbarButtonClicked;
    public event EventHandler<SchedulerToolbarButton>? AdornmentToggleRequested;
    public event EventHandler<SchedulerTileOption>? TileOptionToggled;

    private readonly DaisyControlLifecycle _lifecycle;
    private Border? _viewHost;
    private DaisySchedulerToolbar? _toolbar;
    private readonly List<AppointmentModel> _selection = new();
    private readonly Dictionary<AppointmentModel, object> _selectionResourceIds = new();
    private readonly List<(Border Frame, Border Selection, AppointmentModel Appointment)> _frames = new();
    private bool _selectionSyncing;
    private bool _renderPending;
    private bool _attached;
    private ScrollViewer? _bodyScroll;
    private Point? _savedOffset;
    private SchedulerView? _renderedView;
    private double _horizontalPixelsPerMinute;
    private DaisySize? _renderedSize;
    private IReadOnlyList<SchedulerTileOption> _tileOptions = Array.Empty<SchedulerTileOption>();

    public DaisyScheduler()
    {
        DefaultStyleKey = typeof(DaisyScheduler);
        SelectedDate = new DateTimeOffset(DateTime.Today);
        TimeIncrement = TimeIncrementConfig.Default;
        _lifecycle = new DaisyControlLifecycle(this, RequestRender);
        ToolbarButtons.CollectionChanged += OnToolbarItemsChanged;
        ToolbarAdornments.CollectionChanged += OnToolbarItemsChanged;
        RegisterPropertyChangedCallback(FontSizeProperty, OnFontChanged);
        RegisterPropertyChangedCallback(FontFamilyProperty, OnFontChanged);
        RegisterPropertyChangedCallback(CornerRadiusProperty, OnCornerRadiusChanged);
        FlowerySizeManager.Register(this, OnResolvedSizeRefreshed, () => ResolvedSize);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // A running gesture owns the pointer it captured. On Android a scroll viewer between the appointment and the
        // scheduler marks touch moves handled, so the gesture also listens to handled events; a native scroll view
        // that takes a swipe cancels the pointer.
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnGesturePointerMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnGesturePointerReleased), true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler(OnGesturePointerCanceled), true);
        IsTabStop = true;
    }

    // DependencyProperty.Register keeps these members of the property type when the app is trimmed (WebAssembly).
    private static DependencyProperty Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name, T defaultValue, Func<T, bool>? validate = null)
    {
        var property = DependencyProperty.Register(name, typeof(T), typeof(DaisyScheduler),
            new PropertyMetadata(defaultValue, OnSchedulerPropertyChanged));
        if (validate is not null)
            Validation.Rules[property] = new Validator<T>(validate).IsValid;
        return property;
    }

    // Partial files register properties too; a nested type is initialized before its first use.
    private static class Validation
    {
        public static readonly Dictionary<DependencyProperty, Func<object?, bool>> Rules = new();
    }

    private sealed class Validator<T>(Func<T, bool> validate)
    {
        public bool IsValid(object? value) => value is T typed ? validate(typed) : value is null && default(T) is null && validate(default!);
    }

    // Avalonia rejects invalid values on set; WinUI has no validation hook, so setters throw the same way.
    private void SetValidated(DependencyProperty property, object? value)
    {
        if (!Validation.Rules[property](value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid value for this scheduler property.");
        SetValue(property, value);
    }

    // XAML and bindings bypass the setters. Writing the old value back would replace a binding, so an
    // invalid value stays in the property and the scheduler keeps using the last valid one.
    private readonly Dictionary<DependencyProperty, object?> _lastValidValues = new();

    private T Validated<T>(DependencyProperty property) =>
        (T)(_lastValidValues.TryGetValue(property, out var valid) ? valid : GetValue(property))!;

    private static void OnSchedulerPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var scheduler = (DaisyScheduler)sender;
        if (Validation.Rules.TryGetValue(e.Property, out var validate))
        {
            if (!validate(e.NewValue))
            {
                // The first invalid value records the valid one it replaced; later ones keep that record.
                scheduler._lastValidValues.TryAdd(e.Property, e.OldValue);
                return;
            }
            scheduler._lastValidValues.Remove(e.Property);
        }
        scheduler.OnPropertyChanged(e.Property);
    }

    private void OnPropertyChanged(DependencyProperty property)
    {
        if (property == SelectedAppointmentProperty)
        {
            if (_selectionSyncing)
                return;
            _selection.Clear();
            if (SelectedAppointment is { IsLocked: false } selected)
                _selection.Add(selected);
            CommitSelection();
            return;
        }
        if (property == SelectedAppointmentsProperty)
            return;
        if (property == CurrentViewProperty)
        {
            CancelGesture();
            if (CurrentView is SchedulerView.Week or SchedulerView.Month or SchedulerView.Planner or SchedulerView.Timeline)
                SelectedDates = null;
        }
        RequestRender();
    }

    private void OnFontChanged(DependencyObject sender, DependencyProperty property)
    {
        // The scheduler's own sizing writes are followed by the render that requested them.
        if (_applyingSizing)
            return;
        if (property == FontSizeProperty)
            _hostFontSize = ReadLocalValue(FontSizeProperty) != DependencyProperty.UnsetValue;
        RequestRender();
    }

    private void OnCornerRadiusChanged(DependencyObject sender, DependencyProperty property)
    {
        if (_applyingSizing)
            return;
        _hostCornerRadius = ReadLocalValue(CornerRadiusProperty) != DependencyProperty.UnsetValue;
        // The radius only reaches the template border: a cleared value takes the tier's radius without a render.
        if (!_hostCornerRadius)
            ApplySizing();
    }

    private void OnToolbarItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestRender();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _attached = true;
        FloweryLocalization.CultureChanged += OnCultureChanged;
        RequestRender();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _attached = false;
        FloweryLocalization.CultureChanged -= OnCultureChanged;
        CancelGesture();
    }

    private void OnCultureChanged(object? sender, string culture) => RequestRender();

    // The size manager refreshes every registered control whenever any size policy changes (for example while
    // the Gallery sidebar builds its items); rebuilding the views is only needed when this tier changed.
    private void OnResolvedSizeRefreshed()
    {
        if (ResolvedSize != _renderedSize)
            RequestRender();
    }

    protected override void OnApplyTemplate()
    {
        CancelGesture();
        if (_toolbar is not null)
            _toolbar.Scheduler = null;
        if (_viewHost is not null)
            _viewHost.SizeChanged -= OnViewHostResized;
        base.OnApplyTemplate();
        _viewHost = GetTemplateChild("PART_ViewHost") as Border;
        if (_viewHost is not null)
            _viewHost.SizeChanged += OnViewHostResized;
        _toolbar = GetTemplateChild("PART_Toolbar") as DaisySchedulerToolbar;
        if (_toolbar is not null)
            _toolbar.Scheduler = this;
        if (_viewHost is not null)
            ClipToBounds(_viewHost);
        ApplySizing();
        RenderActiveView();
    }

    // Planner and Timeline hours fill the visible width; a new width rescales them in place.
    private void OnViewHostResized(object? sender, SizeChangedEventArgs e) => RunOutsideLayout(RescaleHours);

    // WinUI sets IsLoaded when the control enters the live tree and raises Loaded later; either one means the
    // control is in the tree.
    internal bool IsAttached => IsLoaded || _attached;

    private void RequestRender()
    {
        if (!IsAttached)
            return;
        // Sizing runs before the render is queued, so its font change does not queue a second render.
        ApplySizing();
        if (_renderPending)
            return;
        _renderPending = true;
        if (DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, OnRenderRequested) != true)
            _renderPending = false;
    }

    private void OnRenderRequested()
    {
        _renderPending = false;
        if (IsAttached)
            RenderActiveView();
    }

    private void RenderActiveView()
    {
        if (_viewHost is null)
            return;
        CancelGesture();
        if (_bodyScroll is not null && _renderedView == CurrentView)
            _savedOffset = new Point(_bodyScroll.HorizontalOffset, _bodyScroll.VerticalOffset);
        else
            _savedOffset = null;
        _bodyScroll = null;
        _headerPane = null;
        _gutterPane = null;
        _monthGrid = null;
        _timeColumns.Clear();
        _frames.Clear();
        _occurrenceSources.Clear();
        _lanes.Clear();
        _hourItems.Clear();
        _visibleItems = GetVisibleOccurrences().ToArray();
        var appointments = Appointments ?? Array.Empty<AppointmentModel>();
        var retained = _selection.Select(old => appointments.FirstOrDefault(a => Same(a, old)))
            .OfType<AppointmentModel>().Where(static a => !a.IsLocked).ToArray();
        if (!_selection.SequenceEqual(retained))
        {
            _selection.Clear();
            _selection.AddRange(retained);
            CommitSelection();
        }
        UpdateToolbar();
        _viewHost.Child = CurrentView switch
        {
            SchedulerView.Month => BuildMonth(),
            SchedulerView.Agenda => BuildAgenda(),
            SchedulerView.Calendar => BuildCalendar(),
            SchedulerView.Planner or SchedulerView.Timeline => BuildPlanner(),
            _ => BuildTimeGrid()
        };
        _renderedView = CurrentView;
        _renderedSize = ResolvedSize;
        ApplySelection();
    }

    private bool _applyingSizing;
    private bool _hostFontSize;
    private bool _hostCornerRadius;

    // Replaces the Size selectors of the Avalonia control theme. As there, local values the host sets win;
    // the change callbacks tell host writes from these. WinUI has no SetCurrentValue, so these are local values
    // too, and a host write of the value already shown raises no change and stays indistinguishable.
    private void ApplySizing()
    {
        DaisyTokenDefaults.EnsureDefaults(Application.Current.Resources);
        _applyingSizing = true;
        try
        {
            if (!_hostFontSize)
                FontSize = Token<double>($"DaisySize{SizeKey}FontSize");
            if (!_hostCornerRadius)
                CornerRadius = Token<CornerRadius>($"DaisySize{SizeKey}CornerRadius");
        }
        finally
        {
            _applyingSizing = false;
        }
    }

    private string SizeKey => DaisyResourceLookup.GetSizeKeyFull(ResolvedSize);
    internal double SizeScale => Token<double>($"DaisySize{SizeKey}FontSize") / Token<double>("DaisySizeMediumFontSize");
    private double ChipHeight => RoundToPixel(Token<double>($"DaisyBadge{SizeKey}Height"));
    private CultureInfo Culture => FormattingCulture;
    private double SnapMinutes => Math.Clamp(TimeIncrement?.MinorTick.TotalMinutes ?? 30, 1, 1440);
    private double PixelsPerMinute => Math.Clamp(TimeIncrement?.PixelsPerMinute ?? 0.8, 0.1, 5) * SizeScale;
    private readonly Dictionary<AppointmentModel, AppointmentModel> _occurrenceSources = new();
    private IReadOnlyList<AppointmentModel> _visibleItems = Array.Empty<AppointmentModel>();
    private IEnumerable<AppointmentModel> Items => _visibleItems;
    private AppointmentModel OriginalAppointment(AppointmentModel appointment) =>
        _occurrenceSources.GetValueOrDefault(appointment, appointment);

    private IEnumerable<AppointmentModel> GetVisibleOccurrences()
    {
        var dates = VisibleDates();
        var start = CurrentView == SchedulerView.Month
            ? WeekStart(new DateTime(SelectedDate.Year, SelectedDate.Month, 1), FirstDayOfWeek) : dates[0];
        var end = CurrentView == SchedulerView.Month ? start.AddDays(42) : dates[^1].AddDays(1);
        // A zoned Planner may display UTC appointments from either adjacent date.
        if (CurrentView is SchedulerView.Planner or SchedulerView.Timeline)
        {
            start = start.AddDays(-1);
            end = end.AddDays(1);
        }
        foreach (var source in Appointments ?? Array.Empty<AppointmentModel>())
            foreach (var occurrence in Scheduler.Utilities.AppointmentOccurrences.InRange(source, start, end))
            {
                if (!ReferenceEquals(source, occurrence))
                    _occurrenceSources[occurrence] = source;
                yield return occurrence;
            }
    }
    private static bool Same(AppointmentModel a, AppointmentModel b) =>
        ReferenceEquals(a, b) || a.Id is not null && b.Id is not null && Equals(a.Id, b.Id);

    private static DateTime WeekStart(DateTime date, DayOfWeek firstDay) =>
        date.Date.AddDays(-((7 + (int)date.DayOfWeek - (int)firstDay) % 7));

    private DateTime[] VisibleDates()
    {
        if (CurrentView is SchedulerView.Day or SchedulerView.Agenda && SelectedDates is { Count: > 0 })
            return SelectedDates.Select(static d => d.Date).Distinct().OrderBy(static d => d).ToArray();
        var start = CurrentView is SchedulerView.Week or SchedulerView.Planner
            ? WeekStart(SelectedDate.Date, FirstDayOfWeek) : SelectedDate.Date;
        var count = CurrentView is SchedulerView.Week or SchedulerView.Planner ? 7 : CurrentView == SchedulerView.Calendar ? 1 : VisibleDays;
        var dates = new DateTime[count];
        for (var index = 0; index < count; index++)
            dates[index] = start.AddDays(index);
        return CurrentView is SchedulerView.Week or SchedulerView.Planner ? dates.Where(IsShownDay).ToArray() : dates;
    }

    /// <summary>Navigate by the visible date span, week, or month.</summary>
    public void Navigate(int direction)
    {
        if (direction == 0)
            return;
        if (CurrentView is SchedulerView.Day or SchedulerView.Agenda && SelectedDates is { Count: > 0 })
        {
            var shifted = VisibleDates().Select(date => date.AddDays(7 * direction)).ToArray();
            SelectedDates = shifted;
            SelectedDate = new DateTimeOffset(shifted[0]);
        }
        else
            SelectedDate = CurrentView == SchedulerView.Month
                ? SelectedDate.AddMonths(direction)
                : SelectedDate.AddDays(direction * (CurrentView is SchedulerView.Week or SchedulerView.Planner ? 7 :
                    CurrentView is SchedulerView.Timeline or SchedulerView.Calendar ? 1 : VisibleDays));
    }

    /// <summary>Raise a deletion request without changing the host's appointment list.</summary>
    public void DeleteSelected(bool withShift = false)
    {
        if (IsReadOnly)
            return;
        var selected = _selection.Where(static appointment => !appointment.IsLocked).ToArray();
        if (selected.Length == 0)
            return;
        _selection.Clear();
        CommitSelection();
        AppointmentsDeleteRequested?.Invoke(this, new AppointmentsDeleteArgs { Appointments = selected, WithShift = withShift });
    }

    public void SetTileOptions(IReadOnlyList<SchedulerTileOption> options)
    {
        _tileOptions = options ?? throw new ArgumentNullException(nameof(options));
        RequestRender();
    }

    private void SelectAppointment(AppointmentModel appointment, bool multiple)
    {
        if (appointment.IsLocked)
            return;
        var previous = _selection.FirstOrDefault(selected => Same(selected, appointment));
        if (multiple && previous is not null)
            _selection.Remove(previous);
        else
        {
            if (!multiple)
                _selection.Clear();
            if (!_selection.Any(selected => Same(selected, appointment)))
                _selection.Add(appointment);
        }
        CommitSelection();
        Focus(FocusState.Pointer);
    }

    private void CommitSelection()
    {
        // Retain the clicked lane across host model replacement, and release deselected models.
        foreach (var (appointment, resourceId) in _selectionResourceIds.ToArray())
        {
            var selected = _selection.FirstOrDefault(item => Same(item, appointment));
            if (ReferenceEquals(selected, appointment))
                continue;
            _selectionResourceIds.Remove(appointment);
            if (selected is not null)
                _selectionResourceIds[selected] = resourceId;
        }
        _selectionSyncing = true;
        SetValue(SelectedAppointmentsProperty, _selection.ToArray());
        SelectedAppointment = _selection.Count == 1 ? _selection[0] : null;
        _selectionSyncing = false;
        ApplySelection();
        SelectionChanged?.Invoke(this, SelectedAppointments);
    }

    private void ApplySelection()
    {
        foreach (var (frame, selection, appointment) in _frames)
        {
            var selected = _selection.Any(item => Same(item, appointment));
            // The selection ring is an overlay, so selecting neither moves nor resizes the appointment.
            selection.BorderBrush = Brush("DaisyPrimaryBrush");
            selection.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetZIndex(frame, selected ? 10 : 0);
            // A touch drag on a selected appointment moves or resizes it; elsewhere it scrolls the view. On Android
            // only None keeps the native scroll views from taking over the touch; Skia and WinUI also accept it.
            frame.ManipulationMode = selected ? ManipulationModes.None : ManipulationModes.System;
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        // Typing in a host-supplied editor must not delete appointments.
        if (e.OriginalSource is not TextBox && HandleKey(e.Key, IsShiftPressed()))
            e.Handled = true;
    }

    internal bool HandleKey(VirtualKey key, bool withShift)
    {
        if (key == VirtualKey.Escape)
        {
            CancelGesture();
            _selection.Clear();
            CommitSelection();
            return true;
        }
        if (key == VirtualKey.Delete && !IsReadOnly && _selection.Count > 0)
        {
            DeleteSelected(withShift);
            return true;
        }
        if (key == VirtualKey.Enter && SelectedAppointment is { IsLocked: false } appointment)
        {
            AppointmentEditRequested?.Invoke(this, new AppointmentEditRequestedArgs { Appointment = appointment });
            return true;
        }
        return false;
    }

    private static bool IsShiftPressed() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private TextBlock Label(string text, bool secondary = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = FontFamily,
            Foreground = Brush(secondary ? "DaisyBaseContentMutedBrush" : "DaisyBaseContentBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        SetFontSize(label, secondary ? Token<double>($"DaisySize{SizeKey}SecondaryFontSize") : FontSize);
        return label;
    }

    // Skia lines are taller than Avalonia's (18 px for 9 px text), which overflowed the Avalonia row
    // geometry. A fixed line height of 1.2 em matches the Avalonia text rows the layout was made for.
    private static void SetFontSize(TextBlock label, double fontSize)
    {
        label.FontSize = fontSize;
        label.LineHeight = Math.Ceiling(fontSize * 1.2);
    }

    private static bool OnDate(AppointmentModel appointment, DateTime date) =>
        appointment.Start < date.AddDays(1) &&
        (appointment.End > date || appointment.End == appointment.Start && appointment.Start.Date == date);
}
