using Flowery.Controls.Scheduler.Contracts;
using Flowery.Controls.Scheduler.Models;
using Flowery.Uno.Gallery.Localization;

namespace Flowery.Uno.Gallery.Examples;

public sealed partial class SchedulerExamples : ScrollableExamplePage, ICollapsibleIntroExample
{
    private readonly List<AppointmentModel> _appointments = new();
    private readonly ResourceModel[] _resources =
    [
        new() { Id = 1, Name = "Alex", Kind = ResourceKind.Person, GroupValues = { ["Department"] = "Development" } },
        new() { Id = 2, Name = "Bailey", Kind = ResourceKind.Person, GroupValues = { ["Department"] = "Development" } },
        new() { Id = 3, Name = "Casey", Kind = ResourceKind.Person, GroupValues = { ["Department"] = "Support" } },
        new() { Id = 4, Name = "Xaver", Kind = ResourceKind.Person, GroupValues = { ["Department"] = "Sales" } },
        new() { Id = 5, Name = "Sammy", Kind = ResourceKind.Person, GroupValues = { ["Department"] = "Communications" } },
        new() { Id = "room-a", Name = "Room A", Kind = ResourceKind.Room },
        new() { Id = "room-b", Name = "Room B", Kind = ResourceKind.Room }
    ];
    private AppointmentModel? _editing;
    private bool _isNew;
    private int _editingResourceIndex;
    private readonly string _helpKey;
    // Below this width the page keeps narrow side margins, so a phone gives the scheduler the room.
    private const double CompactWidth = 600;
    private double _pageWidth;

    public GalleryLocalization Localization { get; } = GalleryLocalization.Instance;

    public SchedulerExamples() : this(IsTouchPlatform)
    {
    }

    /// <param name="touchHelp">Explain the touch gestures below the scheduler instead of mouse and keyboard, as on phones.</param>
    public SchedulerExamples(bool touchHelp)
    {
        _helpKey = touchHelp ? "TouchHelp" : "Help";
        InitializeComponent();
        if (IsTouchPlatform)
        {
            // A scrolling page takes every vertical swipe from the scheduler's own scroll views on a phone, so the
            // page keeps the viewport height and the scheduler scrolls its days and hours itself.
            MainScrollViewer.VerticalScrollMode = ScrollMode.Disabled;
            MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PageGrid.MinHeight = 0;
        }
        DemoScheduler.ResourceGroups = new[]
        {
            new ResourceGroupModel { Id = "all", Name = Text("AllPeople"), Kind = ResourceKind.Person },
            new ResourceGroupModel { Id = "development", Name = Text("Development"), Kind = ResourceKind.Person, GroupType = "Department", GroupValue = "Development" },
            new ResourceGroupModel { Id = "support", Name = Text("Support"), Kind = ResourceKind.Person, GroupType = "Department", GroupValue = "Support" },
            new ResourceGroupModel { Id = "sales", Name = Text("Sales"), Kind = ResourceKind.Person, GroupType = "Department", GroupValue = "Sales" },
            new ResourceGroupModel { Id = "communications", Name = Text("Communications"), Kind = ResourceKind.Person, GroupType = "Department", GroupValue = "Communications" },
            new ResourceGroupModel { Id = "rooms", Name = Text("Rooms"), Kind = ResourceKind.Room }
        };
        foreach (var option in new[] { ResourcesOption, TilesOption, IconsOption, AdornmentsOption, DetailsOption, ReadOnlyOption })
        {
            option.Checked += OnOptionsChanged;
            option.Unchecked += OnOptionsChanged;
        }
        AllDayInput.Checked += OnAllDayChanged;
        AllDayInput.Unchecked += OnAllDayChanged;
        DemoScheduler.AppointmentCreateRequested += OnCreate;
        DemoScheduler.AppointmentEditRequested += OnEdit;
        DemoScheduler.AppointmentMoveRequested += OnMove;
        DemoScheduler.AppointmentResizeRequested += OnResize;
        DemoScheduler.AppointmentsMoveRequested += OnMoveMany;
        DemoScheduler.AppointmentsResizeRequested += OnResizeMany;
        DemoScheduler.AppointmentsDeleteRequested += OnDelete;
        DemoScheduler.AppointmentContextRequested += OnContext;
        DemoScheduler.ToolbarButtonClicked += OnToolbarAction;
        DemoScheduler.SelectionChanged += OnSelectionChanged;
        DemoScheduler.ExternalAppointmentDropped += OnExternalDrop;
        foreach (var property in new[]
        {
            DaisyScheduler.CurrentViewProperty, DaisyScheduler.DateTimeFormatProperty, DaisyScheduler.ClockFormatProperty,
            DaisyScheduler.TimeSlotStartProperty, DaisyScheduler.TimeSlotEndProperty, DaisyScheduler.TimeSlotIntervalProperty,
            DaisyScheduler.AppointmentColorsProperty
        })
            DemoScheduler.RegisterPropertyChangedCallback(property, OnSchedulerPropertyChanged);
        // Light dismiss. The scheduler handles its own presses, so this also listens to handled ones.
        DemoScheduler.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSchedulerPressed), true);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnPageKeyDown), true);
        ApplyEditorShadow();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
        SeedAppointments();
        OnSelectionChanged(this, DemoScheduler.SelectedAppointments);
    }

    protected override ScrollViewer? ScrollViewer => MainScrollViewer;

    public bool IsIntroVisible
    {
        get => IntroPanel.Visibility == Visibility.Visible;
        set => IntroPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    // Android drops a margin written inside its layout pass, which raises SizeChanged; write it after the pass.
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _pageWidth = e.NewSize.Width;
        DispatcherQueue.TryEnqueue(ApplyPageMargin);
    }

    private void ApplyPageMargin() =>
        PageGrid.Margin = _pageWidth < CompactWidth ? new Thickness(8, 0, 8, 8) : new Thickness(24, 0, 24, 16);

    private void ApplyEditorShadow()
    {
        try
        {
            var shadow = new ThemeShadow();
            shadow.Receivers.Add(DemoScheduler);
            EditorPanel.Shadow = shadow;
            EditorPanel.Translation = new System.Numerics.Vector3(0, 0, 24);
        }
        catch (InvalidOperationException)
        {
            // A head without theme shadows shows the editor with its border only.
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        DaisyThemeManager.ThemeChanged += OnThemeChanged;
        GalleryLocalization.CultureChanged += OnCultureChanged;
        Refresh();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        DaisyThemeManager.ThemeChanged -= OnThemeChanged;
        GalleryLocalization.CultureChanged -= OnCultureChanged;
    }

    private void OnThemeChanged(object? sender, string themeName) => Refresh();
    private void OnCultureChanged(object? sender, string culture)
    {
        // Desktop/Skia does not always refresh indexer bindings from INotifyPropertyChanged.
        MainScrollViewer.DataContext = null;
        MainScrollViewer.DataContext = Localization;
        foreach (var group in DemoScheduler.ResourceGroups ?? Array.Empty<ResourceGroupModel>())
            group.Name = Text(group.Id switch
            {
                "all" => "AllPeople", "development" => "Development", "support" => "Support", "sales" => "Sales",
                "communications" => "Communications", _ => "Rooms"
            });
        DemoScheduler.ResourceGroups = DemoScheduler.ResourceGroups?.ToArray();
        Refresh();
        OnSelectionChanged(this, DemoScheduler.SelectedAppointments);
        if (EditorPanel.Visibility == Visibility.Visible)
        {
            var selectedResource = ResourceInput.SelectedIndex;
            ResourceInput.ItemsSource = new[] { Text("Unassigned") }.Concat(_resources.Select(static resource => resource.Name)).ToArray();
            ResourceInput.SelectedIndex = selectedResource;
            RefreshEditorChoices();
            if (!string.IsNullOrEmpty(EditorError.Text))
                EditorError.Text = Text("InvalidInput");
        }
    }
    private void OnOptionsChanged(object? sender, RoutedEventArgs e) => Refresh();
    private void OnSchedulerPropertyChanged(DependencyObject sender, DependencyProperty property)
    {
        if (property == DaisyScheduler.CurrentViewProperty)
            Refresh();
        if (EditorPanel.Visibility == Visibility.Visible && (property == DaisyScheduler.TimeSlotStartProperty ||
            property == DaisyScheduler.TimeSlotEndProperty || property == DaisyScheduler.TimeSlotIntervalProperty ||
            property == DaisyScheduler.ClockFormatProperty || property == DaisyScheduler.DateTimeFormatProperty ||
            property == DaisyScheduler.AppointmentColorsProperty))
            RefreshEditorChoices();
    }

    // Phones and tablets: the help names taps and swipes instead of mouse and keyboard input.
    private static bool IsTouchPlatform => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    private static string Text(string key) => GalleryLocalization.GetString("Scheduler_" + key);

    private static Color? ThemeColor(string key) =>
        DaisyResourceLookup.TryGetResource(Application.Current.Resources, key, out var resource) && resource is Color color ? color : null;

    private void SeedAppointments()
    {
        _appointments.Clear();
        var monday = DateTime.Today.AddDays(-((7 + (int)DateTime.Today.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var subjects = new[] { "Stand-up", "Site survey", "Annual service", "Installation", "Warranty visit", "Parts pickup", "Follow-up call", "Team meeting", "Office hours" };
        for (var index = 0; index < subjects.Length; index++)
        {
            var start = monday.AddDays(index % 5).AddHours(FeatureHours[index]);
            _appointments.Add(new AppointmentModel
            {
                Id = Guid.NewGuid(), Subject = subjects[index], Start = start, End = start.AddHours(index % 2 == 0 ? 1 : 2),
                ResourceIds = { index % 3 + 1 },
                IsComplete = index == 2, IsTentative = index == 5, RecurrenceRule = index == 0 ? "FREQ=WEEKLY" : null
            });
        }
        _appointments.Add(new AppointmentModel { Id = Guid.NewGuid(), Subject = "Trade show", Start = monday.AddDays(3), End = monday.AddDays(5), IsAllDay = true, ResourceIds = { 1, 2 } });
        _appointments.Add(new AppointmentModel { Id = Guid.NewGuid(), Subject = "Reserved maintenance", Start = monday.AddDays(2).AddHours(14), End = monday.AddDays(2).AddHours(16), IsLocked = true, ResourceIds = { 3 } });
        _appointments.Add(new AppointmentModel { Id = Guid.NewGuid(), Subject = "Training", Start = monday.AddHours(9), End = monday.AddDays(4).AddHours(12), SpanMode = AppointmentSpanMode.Daily, ResourceIds = { 1, "room-a" } });
        _appointments.Add(new AppointmentModel { Id = Guid.NewGuid(), Subject = "Exhibition setup", Start = monday.AddDays(1).AddHours(7), End = monday.AddDays(3).AddHours(17), ResourceIds = { "room-b" } });
        SeedPeople(monday);
        SeedMonth(monday);
        foreach (var appointment in _appointments)
            appointment.Description = Notes.GetValueOrDefault(appointment.Subject) ?? Text("SampleDescription");
        Refresh();
    }

    // The start hours of the feature samples, spread over the working day.
    private static readonly double[] FeatureHours = [7.5, 13, 16.5, 9, 14.5, 11, 17, 8.5, 15];

    // A start between 7:00 and 17:30 that looks random but is the same on every run. The slots of a day get separate
    // parts of the day, so they do not pile up in the morning.
    private static double SpreadHour(int day, int slot, int count)
    {
        var steps = 22 / count;
        return 7 + 0.5 * (slot * steps + (day * 7 + slot * 3) % steps);
    }

    // What each sample appointment is about, with made-up customers and suppliers.
    private static readonly Dictionary<string, string> Notes = new()
    {
        ["Stand-up"] = "Daily sync of the dev team: blockers in the Fjordsoft API migration, release plan for Friday.",
        ["Site survey"] = "Measure hall 3 at Nordwind Logistics for the shelving offer. Bring the laser meter.",
        ["Annual service"] = "Yearly maintenance of the heat pump at Riverside Dental. Change filters, log the pressures.",
        ["Installation"] = "Set up 12 workstations at Kessler Bau, 2nd floor. Contact: Mrs. Kessler, ext. 214.",
        ["Warranty visit"] = "Replace the faulty controller board at Polar Pumps Ltd. under warranty, RMA 4711.",
        ["Parts pickup"] = "Collect the spare motors at the Alpenmilch eG supplier depot, gate B, before 4 pm.",
        ["Follow-up call"] = "Call Brightline Media about the open invoice 2026-118 and the second project phase.",
        ["Team meeting"] = "Monthly team round: Q4 goals, vacation planning, budget for the new coffee machine.",
        ["Office hours"] = "Open door for questions about the new ticket system. No agenda.",
        ["Trade show"] = "Booth B12 at Industry Expo Hamburg. Demo units and brochures are in the van.",
        ["Reserved maintenance"] = "Server room closed: Voltwerk Service replaces the UPS batteries.",
        ["Training"] = "Onboarding course for the new ERP, purchasing module. Laptops are provided.",
        ["Exhibition setup"] = "Showroom setup for the Kessler Bau open house: screens, banners, catering by Café Lindner.",
        ["Customer visit Huber & Sons"] = "Meet Mr. Huber senior about the 2027 frame contract. Bring the price list and the samples.",
        ["Quote follow-up"] = "Follow up on quote Q-2026-311 for Nordwind Logistics: 40 shelving units, delivery in March.",
        ["Sales meeting"] = "Weekly pipeline review with the sales team. Xaver presents the Huber & Sons forecast.",
        ["Client lunch"] = "Lunch with the head of purchasing at Alpenmilch eG, Gasthaus zur Linde. Table for two is booked.",
        ["Trade fair prep"] = "Pack the demo units and pick up the brochures from Druckerei Sonnleitner for Industry Expo Hamburg.",
        ["Newsletter draft"] = "October newsletter: product update, the Riverside Dental customer story, event dates.",
        ["Press release review"] = "Final check of the Fjordsoft partnership press release with legal before Thursday.",
        ["Social media plan"] = "Plan the posts for trade fair week. The photos from the Kessler Bau project are approved.",
        ["Interview prep"] = "Prepare talking points for Friday's interview with Brightline Media.",
        ["Website update"] = "Publish the new references page and replace the outdated team photos.",
        ["Customer visit"] = "Quarterly visit at a key account: open tickets, next order, a coffee with the workshop lead.",
        ["Code review"] = "Review the pull requests for the Fjordsoft API client before the release.",
        ["Planning"] = "Plan next week's assignments and service routes.",
        ["Inventory check"] = "Count the spare parts in the warehouse and reorder seals from Polar Pumps Ltd.",
        ["Support shift"] = "First-level hotline. Escalate hardware cases to Casey.",
        ["Design review"] = "Review the new brochure layout from Brightline Media.",
        ["Delivery"] = "Incoming delivery from Nordwind Logistics: 6 pallets, check them against the delivery note.",
        ["Room booking"] = "Room reserved for a customer workshop. Projector and flip chart needed.",
        ["Press call"] = "Short call with a trade magazine about the product launch.",
        ["Sales pitch"] = "Pitch the service contract to a new prospect, with a demo on the tablet."
    };

    // Xaver, a seasoned sales rep, works with customers; Sammy runs press, newsletter and social media.
    private void SeedPeople(DateTime monday)
    {
        (string Subject, int Day, double Hour, double Hours)[] xaver =
        [
            ("Customer visit Huber & Sons", 0, 14.5, 1.5), ("Quote follow-up", 1, 16, 1), ("Sales meeting", 2, 7.5, 1),
            ("Client lunch", 3, 12, 1.5), ("Trade fair prep", 4, 13, 3)
        ];
        (string Subject, int Day, double Hour, double Hours)[] sammy =
        [
            ("Newsletter draft", 0, 8, 1), ("Press release review", 1, 15.5, 1), ("Social media plan", 2, 16.5, 1.5),
            ("Interview prep", 3, 9.5, 1), ("Website update", 4, 17, 1)
        ];
        foreach (var (person, plan) in new[] { (4, xaver), (5, sammy) })
            foreach (var (subject, day, hour, hours) in plan)
            {
                var start = monday.AddDays(day).AddHours(hour);
                _appointments.Add(new AppointmentModel
                {
                    Id = Guid.NewGuid(), Subject = subject, Start = start, End = start.AddHours(hours), ResourceIds = { person },
                    RecurrenceRule = subject == "Sales meeting" ? "FREQ=WEEKLY" : null
                });
            }
    }

    // The other days of the current month get appointments too, weekends included, so the month view is filled; the
    // second and fourth Sunday stay free, unless one of them is today.
    // Weekdays of the current week keep the feature samples above, which have three or more per day; today gets four
    // more for four resources on any date, so Day and Timeline always show several.
    private void SeedMonth(DateTime monday)
    {
        var subjects = new[] { "Customer visit", "Code review", "Planning", "Inventory check", "Support shift", "Design review", "Delivery", "Room booking", "Press call", "Sales pitch" };
        var resources = new object[] { 1, 2, 3, 4, 5, "room-a" };
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var sundays = 0;
        for (var day = first; day.Month == first.Month; day = day.AddDays(1))
        {
            if (day != DateTime.Today && day >= monday && day < monday.AddDays(5))
                continue;
            if (day.DayOfWeek == DayOfWeek.Sunday && ++sundays % 2 == 0 && day != DateTime.Today)
                continue;
            var count = day == DateTime.Today ? 4 : day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 2;
            for (var slot = 0; slot < count; slot++)
            {
                // Within a day, slots differ in subject, resource and time.
                var start = day.AddHours(SpreadHour(day.Day, slot, count));
                _appointments.Add(new AppointmentModel
                {
                    Id = Guid.NewGuid(), Subject = subjects[(day.Day + slot * 3) % subjects.Length], Start = start,
                    End = start.AddHours(1 + (day.Day + slot) % 2), ResourceIds = { resources[(day.Day + slot) % resources.Length] }
                });
            }
        }
    }

    private void Refresh()
    {
        var palette = new[] { ThemeColor("DaisyPrimaryColor"), ThemeColor("DaisySecondaryColor"), ThemeColor("DaisyAccentColor") };
        for (var index = 0; index < _resources.Length; index++)
            _resources[index].Color = palette[index % palette.Length];
        DemoScheduler.AppointmentColors = palette.OfType<Color>().ToArray();
        foreach (var appointment in _appointments)
        {
            var resource = _resources.FirstOrDefault(item => appointment.ResourceIds.Contains(item.Id));
            appointment.BarColor = resource?.Color;
            appointment.Tiles.Clear();
            appointment.Indicators.Clear();
            appointment.Adornments.Clear();
            appointment.Details.Clear();
            if (TilesOption.IsChecked == true)
                appointment.Tiles.Add(new AppointmentDecoration { Text = resource?.Name ?? Text("Unassigned"), Position = DecorationPosition.Bottom });
            if (IconsOption.IsChecked == true && appointment.IsRecurring)
                appointment.Indicators.Add(new AppointmentIndicator { Glyph = "↻", Tooltip = Text("Recurring") });
            if (AdornmentsOption.IsChecked == true && appointment.IsTentative)
                appointment.Adornments.Add(new AppointmentDecoration { Text = "?", Position = DecorationPosition.FloatRight, BackColor = ThemeColor("DaisyWarningColor"), ForeColor = ThemeColor("DaisyWarningContentColor") });
            if (DetailsOption.IsChecked == true)
            {
                appointment.Details.Add(new AppointmentDetail(Text("Resource"), resource?.Name ?? Text("Unassigned")));
                appointment.Details.Add(new AppointmentDetail(Text("Status"), Text(appointment.IsComplete ? "StatusComplete" : appointment.IsLocked ? "StatusLocked" : "StatusScheduled")));
            }
        }
        DemoScheduler.IsReadOnly = ReadOnlyOption.IsChecked == true;
        DemoScheduler.ShowTiles = TilesOption.IsChecked == true;
        DemoScheduler.SchedulerResources = ResourcesOption.IsChecked == true ? _resources.ToArray() : Array.Empty<ResourceModel>();
        // Horizontal views need resource lanes even if resource grouping is off in the other views.
        if (DemoScheduler.CurrentView is SchedulerView.Planner or SchedulerView.Timeline or SchedulerView.Calendar)
            DemoScheduler.SchedulerResources = _resources.ToArray();
        DemoScheduler.Appointments = _appointments.ToArray();
        DemoScheduler.ToolbarButtons.Clear();
        DemoScheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "new", Label = Text("New"), IconData = SchedulerToolbarIcons.Add, IsEnabled = !DemoScheduler.IsReadOnly });
        DemoScheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "delete", Label = Text("Delete"), IconData = SchedulerToolbarIcons.Delete, IsEnabled = !DemoScheduler.IsReadOnly });
        if (DemoScheduler.IsReadOnly)
            EditorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnToolbarAction(object? sender, SchedulerToolbarButton action)
    {
        if (action.Id == "delete")
            DemoScheduler.DeleteSelected();
        else if (action.Id == "new")
            OpenEditor(new AppointmentModel { Id = Guid.NewGuid(), Start = DemoScheduler.SelectedDate.Date + DemoScheduler.TimeSlotStart,
                End = DemoScheduler.SelectedDate.Date + DemoScheduler.TimeSlotStart + DemoScheduler.TimeSlotInterval }, true);
    }

    private void OnSelectionChanged(object? sender, IReadOnlyList<AppointmentModel> selected)
    {
        StatusText.Text = selected.Count == 0 ? Text(_helpKey) : string.Join(", ", selected.Select(static appointment => appointment.Subject));
    }

    private void OnCreate(object? sender, AppointmentCreateArgs e)
    {
        var appointment = new AppointmentModel { Id = Guid.NewGuid(), Start = e.Start, End = e.End, IsAllDay = e.IsAllDay };
        if (e.ResourceId is { } resource)
            appointment.ResourceIds.Add(resource);
        OpenEditor(appointment, true);
    }

    private void OnEdit(object? sender, AppointmentEditRequestedArgs e)
    {
        if (!DemoScheduler.IsReadOnly)
        {
            OpenEditor(e.Appointment, false);
            e.Handled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => EditorPanel.Visibility = Visibility.Collapsed;
    private void OnReset(object? sender, RoutedEventArgs e) { EditorPanel.Visibility = Visibility.Collapsed; SeedAppointments(); }

    private void ApplyMove(AppointmentModel appointment, DateTime start, DateTime end, object? from, object? to, bool copy)
    {
        if (copy)
        {
            appointment = appointment.Clone();
            appointment.Id = Guid.NewGuid();
            _appointments.Add(appointment);
        }
        appointment.Start = start;
        appointment.End = end;
        if (!Equals(from, to))
        {
            if (from is not null)
                appointment.ResourceIds.Remove(from);
            if (to is not null && !appointment.ResourceIds.Contains(to))
                appointment.ResourceIds.Add(to);
        }
    }

    private void OnMove(object? sender, AppointmentMoveArgs e) { ApplyMove(e.Appointment, e.NewStart, e.NewEnd, e.FromResourceId, e.ToResourceId, e.IsCopy); Refresh(); }
    private void OnResize(object? sender, AppointmentResizeArgs e) { e.Appointment.Start = e.NewStart; e.Appointment.End = e.NewEnd; Refresh(); }
    private void OnMoveMany(object? sender, AppointmentsMoveArgs e)
    {
        foreach (var item in e.Items)
            ApplyMove(item.Appointment, item.NewStart, item.NewEnd, item.FromResourceId, item.ToResourceId, e.IsCopy);
        Refresh();
    }
    private void OnResizeMany(object? sender, AppointmentsResizeArgs e)
    {
        foreach (var item in e.Items) { item.Appointment.Start = item.NewStart; item.Appointment.End = item.NewEnd; }
        Refresh();
    }
    private void OnDelete(object? sender, AppointmentsDeleteArgs e)
    {
        foreach (var appointment in e.Appointments)
            _appointments.Remove(appointment);
        EditorPanel.Visibility = Visibility.Collapsed;
        Refresh();
    }
    private void OnExternalDrop(object? sender, ExternalDropArgs e)
    {
        var appointment = new AppointmentModel { Id = Guid.NewGuid(), Subject = e.Item.Subject, Start = e.Start, End = e.End, Tag = e.Item.Payload };
        if (e.ResourceId is { } resource)
            appointment.ResourceIds.Add(resource);
        _appointments.Add(appointment);
        Refresh();
    }

    private void OnContext(object? sender, AppointmentContextRequestedArgs e)
    {
        var menu = new MenuFlyout();
        foreach (var action in new[] { "Edit", "Duplicate", "Complete", "Delete" })
        {
            var item = new MenuFlyoutItem { Text = Text(action), IsEnabled = !DemoScheduler.IsReadOnly && !e.Appointment.IsLocked, Tag = (action, e.Appointment) };
            item.Click += OnContextAction;
            menu.Items.Add(item);
        }
        // The position is relative to the window content; the menu anchors in the scheduler's space.
        if (e.Position is { } position && XamlRoot?.Content is UIElement root)
            menu.ShowAt(DemoScheduler, root.TransformToVisual(DemoScheduler).TransformPoint(position));
        else
            menu.ShowAt(DemoScheduler);
    }

    private void OnContextAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ValueTuple<string, AppointmentModel> action })
            return;
        var (id, appointment) = action;
        switch (id)
        {
            case "Edit": OpenEditor(appointment, false); return;
            case "Duplicate": ApplyMove(appointment, appointment.Start.AddHours(1), appointment.End.AddHours(1), null, null, true); break;
            case "Complete": appointment.IsComplete = !appointment.IsComplete; break;
            case "Delete": _appointments.Remove(appointment); break;
        }
        Refresh();
    }
}
