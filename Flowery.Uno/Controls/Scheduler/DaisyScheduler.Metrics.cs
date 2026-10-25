namespace Flowery.Controls;

public partial class DaisyScheduler
{
    // Theme brushes that have no palette entry: an app or control resource with the key wins.
    private static readonly Dictionary<string, (string Source, double Opacity)> DerivedBrushes = new()
    {
        ["DaisySchedulerTodayBrush"] = ("DaisyPrimaryBrush", 0.08),
        ["DaisySchedulerEventBackgroundBrush"] = ("DaisyBase200Brush", 1)
    };

    private bool TryFindResource(string key, out object? value) =>
        DaisyResourceLookup.TryGetResource(Resources, key, out value) ||
        DaisyResourceLookup.TryGetResource(Application.Current?.Resources, key, out value);

    private T Token<T>(string key) => TryFindResource(key, out var resource) && resource is T value
        ? value : throw new InvalidOperationException("Missing Flowery design token: " + key);

    private Brush Brush(string key)
    {
        if (TryFindResource(key, out var resource) && resource is Brush brush)
            return brush;
        return DerivedBrushes.TryGetValue(key, out var derived)
            ? DaisyResourceLookup.WithOpacity(Brush(derived.Source), derived.Opacity) ?? Transparent
            : Transparent;
    }

    private static SolidColorBrush Transparent => new(Colors.Transparent);

    private double SpacingXS => Token<double>("DaisySpacingXS");
    private double SpacingSmall => Token<double>("DaisySpacingSmall");
    private double SpacingMedium => Token<double>("DaisySpacingMedium");
    private double SpacingLarge => Token<double>("DaisySpacingLarge");
    private double LineThickness => Token<Thickness>("DaisyBorderThicknessThin").Left;
    private double SelectionThickness => Token<Thickness>("DaisyBorderThicknessMedium").Left;
    private double EventCornerRadius => AppointmentCornerRadius ?? Token<CornerRadius>($"DaisySize{SizeKey}CornerRadius").TopLeft;
    private double RowHeight => RoundToPixel(Token<double>($"DaisySize{SizeKey}Height") + SpacingSmall);
    private double RoundToPixel(double length)
    {
        var scaling = XamlRoot?.RasterizationScale ?? 1;
        return Math.Ceiling(length * scaling) / scaling;
    }

    // Planner and Timeline hours fit their header label and a one-hour bar that shows its start time whole
    // (frame and accent borders, content margins, the gap between bars and one spacing of slack). The width
    // follows the size, font, culture and clock format, and hours widen to fill the visible width. Hosts can
    // raise the minimum with DaisySchedulerTimelineMinimumHourWidth. Text widths are rounded up to device pixels
    // as layout rounds them.
    private double MinimumPixelsPerMinute()
    {
        var infinite = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        var barChrome = SpacingXS * 4 + LineThickness * 4 + 2;
        var width = Token<double>("DaisySchedulerTimelineMinimumHourWidth") * SizeScale;
        for (var hour = 0; hour < 24; hour++)
        {
            var text = FormatTime(DateTime.Today.AddHours(hour));
            var header = Label(text, true);
            header.Measure(infinite);
            // Bars show their start time in the subject's font size.
            var start = Label(text);
            start.FontWeight = FontWeights.SemiBold;
            start.Measure(infinite);
            width = Math.Max(width, Math.Max(RoundToPixel(header.DesiredSize.Width) + SpacingSmall * 2,
                RoundToPixel(start.DesiredSize.Width) + barChrome));
        }
        return RoundToPixel(width) / 60;
    }

    // Only this part depends on the visible width, so a resize does not measure the labels again.
    private double FillPixelsPerMinute(double minimum, int dayCount, double gutterWidth) =>
        Math.Max(minimum, ((_viewHost?.ActualWidth ?? 0) - gutterWidth) / (dayCount * 1440));

    // Avalonia's "Auto,*" definition strings, so the view builders read like the original.
    private static Grid WithDefinitions(Grid grid, string columns, string rows = "")
    {
        foreach (var length in Lengths(columns))
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        foreach (var length in Lengths(rows))
            grid.RowDefinitions.Add(new RowDefinition { Height = length });
        return grid;
    }

    private static IEnumerable<GridLength> Lengths(string definitions) =>
        definitions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(static part => part switch
        {
            "Auto" => GridLength.Auto,
            _ when part.EndsWith('*') => new GridLength(part.Length == 1 ? 1 : double.Parse(part[..^1], CultureInfo.InvariantCulture), GridUnitType.Star),
            _ => new GridLength(double.Parse(part, CultureInfo.InvariantCulture))
        });

    // WinUI has no ClipToBounds; clip to the arranged size instead.
    private static T ClipToBounds<T>(T element) where T : FrameworkElement
    {
        element.SizeChanged += OnClippedSizeChanged;
        return element;
    }

    private static void OnClippedSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is UIElement element)
            element.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }
}
