using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    private sealed record TimeColumn(ColumnDefinition Header, ColumnDefinition Body, Canvas Surface,
        IReadOnlyList<(Border Frame, EventLayout Layout)> Blocks, IReadOnlyList<Border> Backgrounds, double MinimumWidth);

    private readonly List<TimeColumn> _timeColumns = new();
    private Grid? _monthGrid;

    private void UpdateViewGeometry()
    {
        if (_bodyScroll is not { ViewportWidth: > 0, ViewportHeight: > 0 } scroll)
            return;
        if (_monthGrid is { } month)
        {
            month.Width = Math.Max(month.MinWidth, scroll.ViewportWidth);
            month.Height = Math.Max(month.MinHeight, scroll.ViewportHeight);
        }
        if (_timeColumns.Count == 0)
            return;
        var minimumWidth = 0d;
        foreach (var column in _timeColumns)
            minimumWidth += column.MinimumWidth;
        var extra = Math.Max(0, (scroll.ViewportWidth - minimumWidth) / _timeColumns.Count);
        foreach (var column in _timeColumns)
        {
            var width = column.MinimumWidth + extra;
            column.Header.Width = new GridLength(width);
            column.Body.Width = new GridLength(width);
            column.Surface.Width = width;
            foreach (var background in column.Backgrounds)
                background.Width = width;
            foreach (var (frame, layout) in column.Blocks)
            {
                var slotWidth = width / layout.ColumnCount;
                frame.Width = Math.Max(LineThickness, slotWidth - SpacingXS);
                Canvas.SetLeft(frame, layout.ColumnIndex * slotWidth + SpacingXS / 2);
            }
        }
    }

    private double TimedAppointmentMinimumWidth(AppointmentModel appointment)
    {
        var time = Label(FormatTime(appointment.Start) + " – " + FormatTime(appointment.End), true);
        time.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var flags = Label(AppointmentFlags(appointment), true);
        flags.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var statusWidth = flags.DesiredSize.Width;
        foreach (var indicator in appointment.Indicators)
        {
            var glyph = Label(indicator.Glyph, true);
            if (indicator.FontFamily is { } family)
                glyph.FontFamily = family;
            glyph.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            statusWidth = Math.Max(statusWidth, glyph.DesiredSize.Width);
        }
        // Account for the status gutter, accent, content margins, selection border and slot gap.
        var insets = statusWidth + SpacingXS * 3 + (SpacingXS + LineThickness) * 2 + SelectionThickness * 2;
        return Math.Max((ColumnMinWidth ?? Token<double>("DaisySchedulerColumnMinimumWidth")) * SizeScale,
            time.DesiredSize.Width + insets);
    }

    // Optional rows must fit in full; the subject and time take precedence over decoration and notes.
    private sealed partial class AppointmentTextPanel(double spacing) : Panel
    {
        private readonly List<int> _priorities = new();
        private bool[] _shown = Array.Empty<bool>();
        public FrameworkElement? Bottom { get; set; }

        // A geometry belongs to one element, so every hidden row gets its own.
        private static RectangleGeometry CreateHiddenClip() => new() { Rect = new Rect(0, 0, 0, 0) };

        public void Add(FrameworkElement child, int priority, bool first = false)
        {
            if (first)
            {
                Children.Insert(0, child);
                _priorities.Insert(0, priority);
            }
            else
            {
                Children.Add(child);
                _priorities.Add(priority);
            }
            _shown = new bool[Children.Count];
        }

        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            foreach (var child in Children)
                child.Measure(new Windows.Foundation.Size(availableSize.Width, double.PositiveInfinity));
            var height = ChooseRows(availableSize.Height);
            var width = 0d;
            for (var index = 0; index < Children.Count; index++)
                if (_shown[index])
                    width = Math.Max(width, Children[index].DesiredSize.Width);
            return new Windows.Foundation.Size(width, Math.Min(height, availableSize.Height));
        }

        private double ChooseRows(double height)
        {
            Array.Clear(_shown);
            var used = 0d;
            var count = 0;
            for (var priority = 0; priority <= 4; priority++)
                for (var index = 0; index < Children.Count; index++)
                {
                    if (_priorities[index] != priority || Children[index].DesiredSize.Height <= 0)
                        continue;
                    var next = used + (count > 0 ? spacing : 0) + Children[index].DesiredSize.Height;
                    if (priority != 0 && next > height)
                        continue;
                    _shown[index] = true;
                    used = next;
                    count++;
                }
            return used;
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            ChooseRows(finalSize.Height);
            var y = 0d;
            for (var index = 0; index < Children.Count; index++)
            {
                var child = Children[index];
                if (!_shown[index])
                {
                    // WinUI centers an element in a slot smaller than its desired size, so clip it away.
                    child.Arrange(default);
                    child.Clip ??= CreateHiddenClip();
                    continue;
                }
                if (child.Clip is not null)
                    child.Clip = null;
                var height = Math.Min(child.DesiredSize.Height, Math.Max(0, finalSize.Height - y));
                if (ReferenceEquals(child, Bottom))
                    y = Math.Max(y, finalSize.Height - height);
                child.Arrange(new Rect(0, y, finalSize.Width, height));
                y += height + spacing;
            }
            return finalSize;
        }
    }
}
