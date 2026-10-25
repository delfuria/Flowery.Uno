using Flowery.Controls.Scheduler.Models;
using Microsoft.UI.Xaml.Automation;

namespace Flowery.Controls;

/// <summary>
/// The toolbar of a <see cref="DaisyScheduler"/>: the date range as a title that opens a date picker, previous and next,
/// Today, the host buttons and the settings on the first row, and the views as icon segments on the second.
/// <see cref="DaisyScheduler"/> shows one by default; set <see cref="DaisyScheduler.ShowToolbar"/> to false and place
/// a toolbar anywhere else, such as in a title bar, with its <see cref="Scheduler"/> set.
/// </summary>
public partial class DaisySchedulerToolbar : UserControl
{
    public static readonly DependencyProperty SchedulerProperty = DependencyProperty.Register(
        nameof(Scheduler), typeof(DaisyScheduler), typeof(DaisySchedulerToolbar), new PropertyMetadata(null, OnSchedulerChanged));

    // 16-unit line icons drawn with a 1.5 stroke.
    private const string PreviousIcon = "M10 3.5 5.5 8 10 12.5";
    private const string NextIcon = "M6 3.5 10.5 8 6 12.5";
    private const string SettingsIcon =
        "M2.5 4.5h6.5 M12 4.5h1.5 M10.5 3v3 M2.5 8h2 M7.5 8h6 M6 6.5v3 M2.5 11.5h7.5 M13 11.5h0.5 M11.5 10v3";
    private const string DayIcon = "M3.5 2.5h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z M2.5 5.5h11 M5.5 8.5h5 M5.5 11h5";
    private const string WeekIcon = "M3.5 2.5h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z M2.5 5.5h11 M6.2 5.5v8 M9.8 5.5v8";
    private const string MonthIcon = "M3.5 2.5h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z M2.5 5.5h11 M2.5 8.5h11 M2.5 11h11 M6.2 5.5v8 M9.8 5.5v8";
    private const string AgendaIcon = "M3 4.5h0.5 M6 4.5h7 M3 8h0.5 M6 8h7 M3 11.5h0.5 M6 11.5h7";
    private const string PlannerIcon = "M2.5 3.5v9 M4.5 5h4 M6.5 8h6 M4.5 11h5";
    private const string TimelineIcon = "M2.5 4h11 M4 2.5v3 M8 2.5v3 M12 2.5v3 M4 8.5h5 M7 11.5h5";
    private const string CalendarIcon = "M3.5 3.5h9a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-8a1 1 0 0 1 1-1z M5.5 2v3 M10.5 2v3 M2.5 6.5h11 M7.5 10h1";

    private DaisyScheduler? _attached;
    private bool _loaded;
    private double _height = 32;
    private double _iconSize = 20;

    // What the built parts depend on: a change of size, font, language or theme builds them again; anything else only
    // updates them, so an open settings menu and the keyboard focus stay.
    private (DaisySize Size, double FontSize, string Culture, string? Theme, ElementTheme Variant)? _builtFor;
    private TextBlock? _primary;
    private TextBlock? _secondary;
    private StackPanel? _hostButtons;
    private readonly Dictionary<SchedulerView, ToolbarButton> _segments = new();
    private SchedulerToolbarButton[] _hostModels = Array.Empty<SchedulerToolbarButton>();

    public DaisySchedulerToolbar()
    {
        IsTabStop = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityChanged);
    }

    /// <summary>Gets or sets the scheduler this toolbar shows and controls.</summary>
    public DaisyScheduler? Scheduler
    {
        get => (DaisyScheduler?)GetValue(SchedulerProperty);
        set => SetValue(SchedulerProperty, value);
    }

    // A toolbar follows its scheduler only while it is in the tree; Loaded attaches it.
    private static void OnSchedulerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var toolbar = (DaisySchedulerToolbar)sender;
        if (toolbar.IsLoaded || toolbar._loaded)
            toolbar.Attach(e.NewValue as DaisyScheduler);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _loaded = true;
        Attach(Scheduler);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        // WinUI raises Unloaded of the old place after Loaded of a new one when the toolbar moves.
        if (IsLoaded)
            return;
        _loaded = false;
        Attach(null);
    }

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (Visibility == Visibility.Visible)
            Update();
    }

    private void Attach(DaisyScheduler? scheduler)
    {
        // Loaded can come again without Unloaded; the same scheduler keeps the built parts, its focus and open menus.
        if (!ReferenceEquals(_attached, scheduler))
        {
            if (_attached is not null)
                _attached.ToolbarChanged -= OnToolbarChanged;
            if (scheduler is not null)
                scheduler.ToolbarChanged += OnToolbarChanged;
            _attached = scheduler;
            _builtFor = null;
        }
        Update();
    }

    private void OnToolbarChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        // The scheduler takes its design tokens from the tree: until it is attached there is nothing to show. A hidden
        // toolbar, such as the built-in one with ShowToolbar off, catches up when it is shown.
        if (_attached is not { } scheduler || !(IsLoaded || _loaded) || !scheduler.IsAttached)
        {
            Content = null;
            _builtFor = null;
            return;
        }
        if (Visibility != Visibility.Visible)
            return;
        var key = (scheduler.ResolvedSize, scheduler.FontSize, FloweryLocalization.CurrentCultureName, DaisyThemeManager.CurrentThemeName,
            scheduler.ActualTheme);
        if (_builtFor != key)
        {
            Build(scheduler);
            _builtFor = key;
        }

        var (primary, secondary) = scheduler.ToolbarTitle();
        _primary!.Text = primary;
        _secondary!.Text = secondary;
        _secondary.Visibility = secondary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (view, segment) in _segments)
            segment.IsSelected = view == scheduler.CurrentView;
        var models = scheduler.ToolbarButtons.Concat(scheduler.ToolbarAdornments).ToArray();
        if (!models.SequenceEqual(_hostModels))
        {
            _hostModels = models;
            _hostButtons!.Children.Clear();
            foreach (var model in scheduler.ToolbarButtons)
                _hostButtons.Children.Add(HostButton(scheduler, model, false));
            foreach (var model in scheduler.ToolbarAdornments)
                _hostButtons.Children.Add(HostButton(scheduler, model, true));
        }
    }

    private void Build(DaisyScheduler scheduler)
    {
        // Heights, icons and the title follow the design tokens of the scheduler size, which follows the size of the app.
        _height = scheduler.ToolbarButtonHeight;
        _iconSize = scheduler.ToolbarIconSize;
        FontSize = scheduler.FontSize;
        _primary = new TextBlock { FontWeight = FontWeights.Bold, FontSize = scheduler.ToolbarTitleFontSize };
        _secondary = new TextBlock { FontSize = scheduler.ToolbarTitleFontSize, Foreground = scheduler.Brush("DaisyBaseContentMutedBrush") };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = scheduler.SpacingXS * 1.5, Children = { _primary, _secondary } };
        var today = Action(scheduler, "Today", new TextBlock { Text = DaisyScheduler.Text("Today"), FontWeight = FontWeights.SemiBold }, "Today");
        today.Kind = ToolbarButtonKind.Today;
        today.MinHeight = Math.Round(_height * 0.8);
        today.Padding = new Thickness(Math.Round(_height * 0.4), 0, Math.Round(_height * 0.4), 0);
        today.CornerRadius = new CornerRadius(999);

        var left = new WrapPanel
        {
            Orientation = Orientation.Horizontal, HorizontalSpacing = scheduler.SpacingXS, VerticalSpacing = scheduler.SpacingXS,
            VerticalAlignment = VerticalAlignment.Center
        };
        left.Children.Add(Action(scheduler, "Date", title, "ChooseDate"));
        left.Children.Add(Action(scheduler, "Previous", Icon(PreviousIcon), "Previous"));
        left.Children.Add(Action(scheduler, "Next", Icon(NextIcon), "Next"));
        left.Children.Add(today);

        _hostButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = scheduler.SpacingXS };
        _hostModels = Array.Empty<SchedulerToolbarButton>();
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = scheduler.SpacingXS, VerticalAlignment = VerticalAlignment.Center,
            Children = { _hostButtons, Action(scheduler, "Settings", Icon(SettingsIcon), "Settings") }
        };

        var top = new EndsPanel(scheduler.SpacingXS);
        top.Children.Add(left);
        top.Children.Add(right);

        _segments.Clear();
        // A segment is as wide as its icon and a padding on each side, and narrower where the bar does not fit.
        var segments = new SegmentsPanel(_iconSize + 2 * Math.Round(_height * 0.45), 2);
        foreach (var view in Enum.GetValues<SchedulerView>())
        {
            var segment = new ToolbarButton(scheduler, ToolbarButtonKind.Segment)
            {
                Content = Icon(ViewIcon(view)), Tag = view,
                // The segments and the padding of their bar are as high as a toolbar button.
                MinHeight = _height - 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(scheduler.ToolbarCornerRadius)
            };
            Describe(scheduler, segment, DaisyScheduler.Text(view.ToString()));
            segment.Click += OnClick;
            _segments[view] = segment;
            segments.Children.Add(segment);
        }
        var views = new Border
        {
            Background = scheduler.Brush("DaisyBase200Brush"),
            CornerRadius = new CornerRadius(scheduler.ToolbarCornerRadius + 2),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, scheduler.SpacingXS * 2, 0, 0),
            Child = segments
        };
        Grid.SetRow(views, 1);

        var rows = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } } };
        rows.Children.Add(top);
        rows.Children.Add(views);
        Content = new Border
        {
            Child = rows,
            Padding = new Thickness(0, 0, 0, scheduler.SpacingXS * 2),
            BorderBrush = scheduler.Brush("DaisyBase300Brush"),
            BorderThickness = new Thickness(0, 0, 0, scheduler.LineThickness)
        };
    }

    private ToolbarButton Action(DaisyScheduler scheduler, string tag, FrameworkElement content, string nameKey)
    {
        var button = new ToolbarButton(scheduler, ToolbarButtonKind.Action) { Content = content, Tag = tag };
        Fit(button);
        Describe(scheduler, button, DaisyScheduler.Text(nameKey), tooltip: content is Viewbox);
        button.Click += OnClick;
        return button;
    }

    private ToolbarButton HostButton(DaisyScheduler scheduler, SchedulerToolbarButton model, bool adornment)
    {
        FrameworkElement content;
        if (model.Image is { Length: > 0 } bytes)
        {
            // An image with a label shows both.
            var image = DaisyScheduler.CreateOwnedImage(bytes, 18 * scheduler.SizeScale);
            content = model.Label is { Length: > 0 } caption
                ? new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4,
                    Children = { image, new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center } }
                }
                : image;
        }
        else if (model.IconData is { Length: > 0 } data)
            content = Icon(data);
        else if (model.Glyph is { Length: > 0 } glyph)
        {
            // A glyph of the symbol font, which a text in the toolbar font would show as a box.
            var symbol = new FontIcon { Glyph = glyph, FontSize = _iconSize };
            content = model.Label is { Length: > 0 } label
                ? new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4,
                    Children = { symbol, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } }
                }
                : symbol;
        }
        else
            content = new TextBlock { Text = model.Label ?? model.Id ?? string.Empty };
        var button = new ToolbarButton(scheduler, ToolbarButtonKind.Action)
        {
            Content = content, Tag = new DaisyScheduler.ToolbarAction(model, adornment), IsEnabled = model.IsEnabled,
            IsChecked = model.IsToggle && model.IsChecked
        };
        Fit(button);
        AutomationProperties.SetName(button, model.Label ?? model.Tooltip ?? model.Id ?? string.Empty);
        scheduler.SetToolbarTooltip(button, model.Tooltip ?? (content is Viewbox or Image or FontIcon ? model.Label : null));
        if (model.MenuItems is { Count: > 0 })
            button.Flyout = scheduler.CreateToolbarMenu(model, adornment);
        else
            button.Click += OnClick;
        return button;
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _attached is not { } scheduler)
            return;
        switch (button.Tag)
        {
            case SchedulerView view: scheduler.CurrentView = view; break;
            case DaisyScheduler.ToolbarAction action: scheduler.InvokeToolbarAction(action); break;
            case "Date": scheduler.ShowDatePicker(button); break;
            case "Previous": scheduler.Navigate(-1); break;
            case "Next": scheduler.Navigate(1); break;
            case "Today": scheduler.GoToToday(); break;
            case "Settings": scheduler.ShowSettings(button); break;
        }
    }

    private static void Describe(DaisyScheduler scheduler, FrameworkElement control, string name, bool tooltip = true)
    {
        AutomationProperties.SetName(control, name);
        if (tooltip)
            scheduler.SetToolbarTooltip(control, name);
    }

    // A 16-unit line icon, scaled with its stroke to the icon size of the scheduler. Its button sets the stroke.
    private Viewbox Icon(string data) => new()
    {
        Width = _iconSize, Height = _iconSize, VerticalAlignment = VerticalAlignment.Center,
        Child = new Path
        {
            Data = FloweryPathHelpers.ParseGeometry(data), Width = 16, Height = 16, Stretch = Stretch.None, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        }
    };

    // A toolbar button is as high as the buttons of the scheduler size; an icon button is square.
    private void Fit(ToolbarButton button)
    {
        button.MinHeight = _height;
        if (button.Content is Viewbox)
        {
            button.MinWidth = _height;
            button.Padding = default;
        }
        else
        {
            var side = Math.Round(_height * 0.3);
            button.Padding = new Thickness(side, 0, side, 0);
        }
    }

    private static string ViewIcon(SchedulerView view) => view switch
    {
        SchedulerView.Day => DayIcon,
        SchedulerView.Week => WeekIcon,
        SchedulerView.Month => MonthIcon,
        SchedulerView.Agenda => AgendaIcon,
        SchedulerView.Planner => PlannerIcon,
        SchedulerView.Timeline => TimelineIcon,
        _ => CalendarIcon
    };

    internal enum ToolbarButtonKind { Action, Today, Segment }

    // A quiet toolbar button, transparent until hovered like the toolbar of a native calendar; a view segment is muted,
    // with the selected one raised on the base surface. The colors are set here rather than by visual states, so a
    // line icon takes the text color of its button.
    // No primary constructor: Uno generates native constructors for Android views.
    internal sealed partial class ToolbarButton : Button
    {
        private readonly DaisyScheduler _scheduler;
        private ToolbarButtonKind _kind;
        private bool _isSelected;
        private bool _isChecked;
        private bool _isPointerOver;
        private bool _isPressed;

        public ToolbarButton(DaisyScheduler scheduler, ToolbarButtonKind kind)
        {
            _scheduler = scheduler;
            _kind = kind;
            if (_scheduler.SchedulerStyle("DaisySchedulerToolbarButtonStyle") is { } style)
                Style = style;
            CornerRadius = scheduler.ToolbarButtonCornerRadius;
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            // A button handles its own presses, so these listen to handled events too.
            AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
            AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
            PointerCaptureLost += OnPointerReleased;
            IsEnabledChanged += OnIsEnabledChanged;
            ApplyColors();
        }

        public ToolbarButtonKind Kind
        {
            get => _kind;
            set { _kind = value; ApplyColors(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; ApplyColors(); }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; ApplyColors(); }
        }

        private void OnPointerEntered(object? sender, PointerRoutedEventArgs e) { _isPointerOver = true; ApplyColors(); }

        private void OnPointerExited(object? sender, PointerRoutedEventArgs e) { _isPointerOver = false; _isPressed = false; ApplyColors(); }

        private void OnPointerPressed(object? sender, PointerRoutedEventArgs e) { _isPressed = true; ApplyColors(); }

        private void OnPointerReleased(object? sender, PointerRoutedEventArgs e) { _isPressed = false; ApplyColors(); }

        private void OnIsEnabledChanged(object? sender, DependencyPropertyChangedEventArgs e) => ApplyColors();

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            ApplyColors();
        }

        private void ApplyColors()
        {
            Brush background;
            Brush foreground;
            if (_kind == ToolbarButtonKind.Segment)
            {
                background = _isSelected ? _scheduler.Brush("DaisyBase100Brush") : new SolidColorBrush(Colors.Transparent);
                foreground = _scheduler.Brush(_isSelected || _isPointerOver ? "DaisyBaseContentBrush" : "DaisyBaseContentMutedBrush");
                // WinUI's content presenter draws no box shadow; a hairline raises the selected segment instead.
                BorderBrush = _isSelected ? _scheduler.Brush("DaisyBase300Brush") : new SolidColorBrush(Colors.Transparent);
                BorderThickness = new Thickness(1);
            }
            else
            {
                background = _isPressed ? _scheduler.Brush("DaisyBase300Brush")
                    : _isPointerOver || _isChecked ? _scheduler.Brush("DaisyBase200Brush")
                    : new SolidColorBrush(Colors.Transparent);
                foreground = _scheduler.Brush(_isChecked ? "DaisyPrimaryBrush" : "DaisyBaseContentBrush");
                var today = _kind == ToolbarButtonKind.Today;
                BorderBrush = today ? _scheduler.Brush("DaisyBase300Brush") : new SolidColorBrush(Colors.Transparent);
                BorderThickness = new Thickness(today ? 1 : 0);
            }
            Background = background;
            Foreground = foreground;
            Opacity = IsEnabled ? 1 : 0.4;
            if (Content is Viewbox { Child: Path path })
                path.Stroke = foreground;
        }
    }

    // The view segments side by side, each as wide as the given width, or all narrower by the same amount where they do
    // not fit, as on a phone; a segment centers its icon.
    // No primary constructor: Uno generates native constructors for Android views.
    private sealed partial class SegmentsPanel : Panel
    {
        private readonly double _itemWidth;
        private readonly double _spacing;

        public SegmentsPanel(double itemWidth, double spacing)
        {
            _itemWidth = itemWidth;
            _spacing = spacing;
        }

        private double ItemWidth(double available) =>
            Children.Count == 0 ? 0 : Math.Max(0, Math.Min(_itemWidth, (available - _spacing * (Children.Count - 1)) / Children.Count));

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = ItemWidth(availableSize.Width);
            var height = 0d;
            foreach (var child in Children)
            {
                child.Measure(new Size(width, availableSize.Height));
                height = Math.Max(height, child.DesiredSize.Height);
            }
            return new Size(Children.Count == 0 ? 0 : width * Children.Count + _spacing * (Children.Count - 1), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = ItemWidth(finalSize.Width);
            var x = 0d;
            foreach (var child in Children)
            {
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width + _spacing;
            }
            return finalSize;
        }
    }

    // The first row: the start on the left and the end on the right, at least the spacing apart, or the end
    // right-aligned below the start where both do not fit side by side.
    // No primary constructor: Uno generates native constructors for Android views.
    private sealed partial class EndsPanel : Panel
    {
        private readonly double _spacing;

        public EndsPanel(double spacing) => _spacing = spacing;

        private bool SideBySide(double width) =>
            Children[0].DesiredSize.Width + _spacing + Children[1].DesiredSize.Width <= width;

        protected override Size MeasureOverride(Size availableSize)
        {
            var (start, end) = (Children[0], Children[1]);
            start.Measure(availableSize);
            end.Measure(availableSize);
            return SideBySide(availableSize.Width)
                ? new Size(start.DesiredSize.Width + _spacing + end.DesiredSize.Width, Math.Max(start.DesiredSize.Height, end.DesiredSize.Height))
                : new Size(Math.Max(start.DesiredSize.Width, end.DesiredSize.Width),
                    start.DesiredSize.Height + _spacing + end.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var (start, end) = (Children[0], Children[1]);
            var endLeft = Math.Max(0, finalSize.Width - end.DesiredSize.Width);
            if (SideBySide(finalSize.Width))
            {
                start.Arrange(new Rect(0, 0, start.DesiredSize.Width, finalSize.Height));
                end.Arrange(new Rect(endLeft, 0, end.DesiredSize.Width, finalSize.Height));
            }
            else
            {
                start.Arrange(new Rect(0, 0, start.DesiredSize.Width, start.DesiredSize.Height));
                end.Arrange(new Rect(endLeft, start.DesiredSize.Height + _spacing, end.DesiredSize.Width, end.DesiredSize.Height));
            }
            return finalSize;
        }
    }
}
