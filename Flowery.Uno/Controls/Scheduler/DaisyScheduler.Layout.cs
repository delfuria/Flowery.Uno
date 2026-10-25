using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    private sealed record TimeColumn(ColumnDefinition Header, ColumnDefinition Body, Canvas Surface,
        IReadOnlyList<(Border Frame, EventLayout Layout)> Blocks, IReadOnlyList<Border> Backgrounds, double MinimumWidth);

    private readonly List<TimeColumn> _timeColumns = new();
    private Grid? _monthGrid;

    // Uno raises SizeChanged and Loaded inside the layout pass. Android drops a layout request made there once the
    // pass completes, so the scheduler's subtree would never lay out again; run size writes after the pass instead.
    private void RunOutsideLayout(Action action)
    {
#if __ANDROID__
        if (DispatcherQueue?.TryEnqueue(action.Invoke) == true)
            return;
#endif
        action();
    }

    private void UpdateViewGeometry()
    {
        if (_bodyScroll is not { } scroll)
            return;
        // Android reports the viewport only after the first size change; the scroll viewer's size stands in.
        var viewportWidth = scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth;
        var viewportHeight = scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;
        if (_monthGrid is { } month)
        {
            month.Width = Math.Max(month.MinWidth, viewportWidth);
            month.Height = Math.Max(month.MinHeight, viewportHeight);
        }
        if (_timeColumns.Count == 0)
            return;
        var minimumWidth = 0d;
        foreach (var column in _timeColumns)
            minimumWidth += column.MinimumWidth;
        var extra = Math.Max(0, (viewportWidth - minimumWidth) / _timeColumns.Count);
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

    // A geometry belongs to one element, so every hidden part gets its own.
    private static RectangleGeometry CreateHiddenClip() => new() { Rect = new Rect(0, 0, 0, 0) };

    // Optional rows must fit in full; the subject and time take precedence over decoration and notes.
    // No primary constructor: Uno generates native constructors for Android views.
    private sealed partial class AppointmentTextPanel : Panel
    {
        private readonly double _spacing;

        public AppointmentTextPanel(double spacing) => _spacing = spacing;

        private readonly List<int> _priorities = new();
        private bool[] _shown = Array.Empty<bool>();
        /// <summary>The row kept at the bottom edge (the bottom tile strip).</summary>
        public FrameworkElement? BottomRow { get; set; }

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
                    var next = used + (count > 0 ? _spacing : 0) + Children[index].DesiredSize.Height;
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
                if (ReferenceEquals(child, BottomRow))
                    y = Math.Max(y, finalSize.Height - height);
                child.Arrange(new Rect(0, y, finalSize.Width, height));
                y += height + _spacing;
            }
            return finalSize;
        }
    }

    // A single-line chip or bar keeps its start time whole. Status glyphs and tiles show only when they fit
    // beside it, in the order they were added with Optional; the subject takes the rest and trims.
    // No primary constructor: Uno generates native constructors for Android views.
    private sealed partial class AppointmentLinePanel : Panel
    {
        public const int Required = 0;
        public const int Optional = 1;
        public const int Fill = 2;

        private static readonly int[] PlacedRoles = { Required, Optional };

        private readonly double _spacing;
        private readonly List<int> _roles = new();
        private bool[] _shown = Array.Empty<bool>();
        private double _fillWidth;
        private double _fillMinimum;

        public AppointmentLinePanel(double spacing) => _spacing = spacing;

        /// <param name="minimumWidth">For the Fill part: the narrowest width worth showing, such as the
        /// first characters and an ellipsis. Below it the part is hidden instead of showing fragments.</param>
        public void Add(UIElement child, int role, double minimumWidth = 0)
        {
            Children.Add(child);
            _roles.Add(role);
            if (role == Fill)
                _fillMinimum = minimumWidth;
            _shown = new bool[Children.Count];
        }

        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            foreach (var child in Children)
                child.Measure(new Windows.Foundation.Size(double.PositiveInfinity, availableSize.Height));
            Choose(availableSize.Width);
            var width = 0d;
            var height = 0d;
            var count = 0;
            for (var index = 0; index < Children.Count; index++)
            {
                if (!_shown[index])
                    continue;
                if (_roles[index] == Fill)
                    Children[index].Measure(new Windows.Foundation.Size(_fillWidth, availableSize.Height));
                width += (count++ > 0 ? _spacing : 0) + Children[index].DesiredSize.Width;
                height = Math.Max(height, Children[index].DesiredSize.Height);
            }
            return new Windows.Foundation.Size(width, height);
        }

        // Each shown part costs its width plus one spacing; the first spacing is given back.
        private void Choose(double width)
        {
            Array.Clear(_shown);
            var remaining = width + _spacing;
            foreach (var role in PlacedRoles)
                for (var index = 0; index < Children.Count; index++)
                {
                    if (_roles[index] != role)
                        continue;
                    var cost = Children[index].DesiredSize.Width + _spacing;
                    if (role == Optional && cost > remaining)
                        continue;
                    _shown[index] = true;
                    remaining -= cost;
                }
            _fillWidth = Math.Max(0, remaining - _spacing);
            for (var index = 0; index < Children.Count; index++)
                if (_roles[index] == Fill)
                    _shown[index] = _fillWidth > 0 && _fillWidth >= _fillMinimum;
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            Choose(finalSize.Width);
            var x = 0d;
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
                var width = _roles[index] == Fill ? _fillWidth : child.DesiredSize.Width;
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width + _spacing;
            }
            return finalSize;
        }
    }
}
