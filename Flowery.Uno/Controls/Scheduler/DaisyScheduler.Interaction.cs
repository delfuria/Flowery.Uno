// Uno Platform adaptation of Xtreme.Components Scheduler (MIT).
// See ThirdPartyNotices/Xtreme.Components.txt for attribution and license.
using Flowery.Controls.Scheduler.Contracts;
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Utilities;
using Microsoft.UI.Input;
using Windows.System;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    private sealed record AppointmentTarget(AppointmentModel Appointment, Lane? Lane, AppointmentModel Displayed, object? ResourceId);
    private sealed record DayTarget(DateTime Date, ResourceModel? Resource);
    private enum GestureKind { Create, Move, ResizeStart, ResizeEnd }

    private sealed class Gesture
    {
        public required uint PointerId { get; init; }
        public Pointer? Pointer { get; init; }
        public required UIElement Capture { get; init; }
        public required Lane Source { get; init; }
        public required Point PressPoint { get; init; }
        public required DateTime PressTime { get; init; }
        public required GestureKind Kind { get; init; }
        public required int ClickCount { get; init; }
        public AppointmentModel? Appointment { get; init; }
        public required DateTime OriginalStart { get; init; }
        public required DateTime OriginalEnd { get; init; }
        public Lane? Target { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool Started { get; set; }
        public Border? Ghost { get; set; }
    }

    /// <summary>One pointer event: WinUI input, or input that runtime tests synthesize at a window position.</summary>
    internal sealed class PointerInput
    {
        private readonly PointerRoutedEventArgs? _args;
        private readonly UIElement? _root;

        public PointerInput(PointerRoutedEventArgs args)
        {
            var point = args.GetCurrentPoint(null);
            _args = args;
            Pointer = args.Pointer;
            PointerId = args.Pointer.PointerId;
            WindowPosition = point.Position;
            IsLeftButton = point.Properties.IsLeftButtonPressed;
            IsRightButton = point.Properties.IsRightButtonPressed;
            Modifiers = args.KeyModifiers;
            Timestamp = point.Timestamp;
        }

        /// <summary>Synthesized input; <paramref name="root"/> is the window content the position refers to.</summary>
        public PointerInput(UIElement root, Point windowPosition, bool isLeftButton, bool isRightButton,
            VirtualKeyModifiers modifiers, ulong timestamp, uint pointerId = 1)
        {
            _root = root;
            PointerId = pointerId;
            WindowPosition = windowPosition;
            IsLeftButton = isLeftButton;
            IsRightButton = isRightButton;
            Modifiers = modifiers;
            Timestamp = timestamp;
        }

        /// <summary>Null for synthesized input, which captures nothing.</summary>
        public Pointer? Pointer { get; }
        public uint PointerId { get; }
        public Point WindowPosition { get; }
        public bool IsLeftButton { get; }
        public bool IsRightButton { get; }
        public VirtualKeyModifiers Modifiers { get; }
        /// <summary>Microseconds, as <see cref="PointerPoint.Timestamp"/>.</summary>
        public ulong Timestamp { get; }

        // From the window position: on Android, GetCurrentPoint(element) reports a captured pointer relative to the
        // capturing element instead of the given one.
        public Point PositionIn(UIElement element) => _args is null
            ? _root!.TransformToVisual(element).TransformPoint(WindowPosition)
            : element.TransformToVisual(null).Inverse.TransformPoint(WindowPosition);
    }

    // WinUI reports no click count: count presses on the same target within the system double-click time.
    private static readonly ulong SystemDoubleClickMicroseconds = ReadDoubleClickMicroseconds();
    private object? _lastPressKey;
    private ulong _lastPressTimestamp;
    private Point _lastPressPoint;
    private int _lastClickCount;

    private Gesture? _gesture;

    private static ulong ReadDoubleClickMicroseconds()
    {
        try
        {
            var milliseconds = new Windows.UI.ViewManagement.UISettings().DoubleClickTime;
            return milliseconds > 0 ? milliseconds * 1000UL : 500_000;
        }
        catch (Exception exception) when (exception is NotImplementedException or COMException)
        {
            return 500_000;
        }
    }

    /// <summary>The double-click time in microseconds, the unit of <see cref="PointerPoint.Timestamp"/>.</summary>
    internal static ulong DoubleClickMicroseconds => SystemDoubleClickMicroseconds;

    private int ClickCount(object key, PointerInput input)
    {
        var position = input.WindowPosition;
        var repeated = Equals(_lastPressKey, key) && input.Timestamp - _lastPressTimestamp <= DoubleClickMicroseconds &&
            Math.Abs(position.X - _lastPressPoint.X) <= 4 && Math.Abs(position.Y - _lastPressPoint.Y) <= 4;
        _lastClickCount = repeated ? _lastClickCount + 1 : 1;
        _lastPressKey = key;
        _lastPressTimestamp = input.Timestamp;
        _lastPressPoint = position;
        return _lastClickCount;
    }

    /// <summary>Routes a press as WinUI does: from the hit element up to the scheduler until a handler takes it.</summary>
    internal bool RoutePointerPressed(UIElement hit, PointerInput input)
    {
        for (DependencyObject? element = hit; element is not null && !ReferenceEquals(element, this); element = VisualTreeHelper.GetParent(element))
        {
            var handled = element switch
            {
                Border { Tag: AppointmentTarget } frame => PressAppointment(frame, input),
                Canvas { Tag: Lane } surface => PressLane(surface, input),
                FrameworkElement { Tag: DayTarget } day => PressEmptyDay(day, input),
                _ => false
            };
            if (handled)
                return true;
        }
        return false;
    }

    private void OnAppointmentPressed(object? sender, PointerRoutedEventArgs e)
    {
        if (sender is Border frame && PressAppointment(frame, new PointerInput(e)))
            e.Handled = true;
    }

    private bool PressAppointment(Border frame, PointerInput input)
    {
        if (frame.Tag is not AppointmentTarget target)
            return false;
        var appointment = target.Appointment;
        var position = input.PositionIn(frame);
        var selectedBefore = _selection.Any(selected => Same(selected, appointment));
        if (input.IsRightButton)
        {
            if (!appointment.IsLocked)
                RememberSelectionResource(target);
            if (!appointment.IsLocked && !selectedBefore)
                SelectAppointment(appointment, false);
            AppointmentContextRequested?.Invoke(this, new AppointmentContextRequestedArgs { Appointment = appointment, Position = input.WindowPosition });
            return true;
        }
        if (!input.IsLeftButton)
            return false;
        var clickCount = ClickCount(appointment.Id ?? appointment, input);
        if (appointment.IsLocked)
            return true;
        RememberSelectionResource(target);
        var multiple = (input.Modifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Windows)) != 0;
        if (multiple || !selectedBefore)
            SelectAppointment(appointment, multiple);
        else
            Focus(FocusState.Pointer);
        if (clickCount == 2 && !multiple)
        {
            AppointmentEditRequested?.Invoke(this, new AppointmentEditRequestedArgs { Appointment = appointment });
            return true;
        }
        if (IsReadOnly || target.Lane is not { } lane || multiple && selectedBefore)
            return true;
        var kind = GestureKind.Move;
        if (selectedBefore)
        {
            var axis = lane.Horizontal ? position.X : position.Y;
            var length = lane.Horizontal ? frame.ActualWidth : frame.ActualHeight;
            if (axis < 5)
                kind = GestureKind.ResizeStart;
            else if (axis > length - 5)
                kind = GestureKind.ResizeEnd;
        }
        BeginGesture(input, frame, lane, kind, clickCount, appointment, target.Displayed);
        return true;
    }

    private void OnAppointmentKeyDown(object? sender, KeyRoutedEventArgs e)
    {
        if (sender is Border frame && HandleAppointmentKey(frame, e.Key))
            e.Handled = true;
    }

    internal bool HandleAppointmentKey(Border frame, VirtualKey key)
    {
        if (frame.Tag is not AppointmentTarget { Appointment.IsLocked: false } target || key != VirtualKey.Enter)
            return false;
        RememberSelectionResource(target);
        SelectAppointment(target.Appointment, false);
        AppointmentEditRequested?.Invoke(this, new AppointmentEditRequestedArgs { Appointment = target.Appointment });
        return true;
    }

    private void RememberSelectionResource(AppointmentTarget target)
    {
        if (target.ResourceId is { } resourceId)
            _selectionResourceIds[target.Appointment] = resourceId;
        else
            _selectionResourceIds.Remove(target.Appointment);
    }

    private void AttachEmptyDay(FrameworkElement control, DateTime date, ResourceModel? resource)
    {
        control.Tag = new DayTarget(date, resource);
        control.PointerPressed += OnEmptyDayPressed;
    }

    private void OnEmptyDayPressed(object? sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement control && PressEmptyDay(control, new PointerInput(e)))
            e.Handled = true;
    }

    private bool PressEmptyDay(FrameworkElement control, PointerInput input)
    {
        if (IsReadOnly || control.Tag is not DayTarget target || !input.IsLeftButton)
            return false;
        SelectSlot(target.Date, target.Resource, true, ClickCount(target, input), input.WindowPosition);
        return true;
    }

    private void AttachLane(Lane lane)
    {
        lane.Surface.Tag = lane;
        lane.Surface.PointerPressed += OnLanePressed;
        lane.Surface.AllowDrop = true;
        lane.Surface.DragOver += OnLaneDragOver;
        lane.Surface.Drop += OnLaneDrop;
    }

    private void OnLanePressed(object? sender, PointerRoutedEventArgs e)
    {
        if (sender is Canvas surface && PressLane(surface, new PointerInput(e)))
            e.Handled = true;
    }

    private bool PressLane(Canvas surface, PointerInput input)
    {
        if (IsReadOnly || surface.Tag is not Lane lane || !input.IsLeftButton)
            return false;
        Focus(FocusState.Pointer);
        var slot = SlotTime(lane, input.PositionIn(lane.Surface));
        BeginGesture(input, surface, lane, GestureKind.Create, ClickCount((lane.Resource?.Id, slot), input), null);
        return true;
    }

    private DateTime SlotTime(Lane lane, Point position)
    {
        var axis = lane.Horizontal ? position.X : position.Y;
        var limit = lane.DayCount * 1440;
        var minutes = Math.Clamp(Math.Floor(axis / lane.PixelsPerMinute / SnapMinutes) * SnapMinutes, 0, limit - SnapMinutes);
        return lane.Origin.AddMinutes(minutes);
    }

    private void BeginGesture(PointerInput input, UIElement capture, Lane lane, GestureKind kind, int clickCount,
        AppointmentModel? appointment, AppointmentModel? displayed = null)
    {
        CancelGesture();
        var slot = SlotTime(lane, input.PositionIn(lane.Surface));
        var start = displayed?.Start ?? (appointment is null ? slot : ShownTime(appointment.Start, lane));
        var end = displayed?.End ?? (appointment is null ? slot.AddMinutes(SnapMinutes) : ShownTime(appointment.End, lane));
        _gesture = new Gesture
        {
            PointerId = input.PointerId, Pointer = input.Pointer, Capture = capture, Source = lane, Target = lane,
            PressPoint = input.PositionIn(this), PressTime = slot, Kind = kind, ClickCount = clickCount, Appointment = appointment,
            OriginalStart = start, OriginalEnd = end, Start = start, End = end
        };
        if (input.Pointer is { } pointer)
            capture.CapturePointer(pointer);
    }

    private void OnGesturePointerMoved(object? sender, PointerRoutedEventArgs e)
    {
        if (_gesture is not null && MovePointer(new PointerInput(e)))
            e.Handled = true;
    }

    internal bool MovePointer(PointerInput input)
    {
        if (_gesture is not { } gesture || gesture.PointerId != input.PointerId || IsReadOnly)
            return false;
        var position = input.PositionIn(this);
        var distanceX = position.X - gesture.PressPoint.X;
        var distanceY = position.Y - gesture.PressPoint.Y;
        if (!gesture.Started && distanceX * distanceX + distanceY * distanceY < 16)
            return false;
        gesture.Started = true;
        var target = gesture.Source;
        if (gesture.Kind == GestureKind.Move)
            target = _lanes.FirstOrDefault(lane => lane.Horizontal == gesture.Source.Horizontal && Contains(lane.Surface, input.PositionIn(lane.Surface))) ?? gesture.Source;
        var slot = SlotTime(target, input.PositionIn(target.Surface));
        var delta = slot - gesture.PressTime;
        gesture.Target = target;
        switch (gesture.Kind)
        {
            case GestureKind.Create:
                gesture.Start = slot < gesture.PressTime ? slot : gesture.PressTime;
                gesture.End = (slot > gesture.PressTime ? slot : gesture.PressTime).AddMinutes(SnapMinutes);
                break;
            case GestureKind.Move:
                gesture.Start = gesture.OriginalStart + delta;
                gesture.End = gesture.Start + (gesture.OriginalEnd - gesture.OriginalStart);
                break;
            case GestureKind.ResizeStart:
                gesture.Start = gesture.OriginalStart + delta;
                if (gesture.Start > gesture.End.AddMinutes(-SnapMinutes))
                    gesture.Start = gesture.End.AddMinutes(-SnapMinutes);
                break;
            case GestureKind.ResizeEnd:
                gesture.End = gesture.OriginalEnd + delta;
                if (gesture.End < gesture.Start.AddMinutes(SnapMinutes))
                    gesture.End = gesture.Start.AddMinutes(SnapMinutes);
                break;
        }
        ShowGhost(gesture);
        return true;
    }

    private static bool Contains(FrameworkElement element, Point position) =>
        position.X >= 0 && position.Y >= 0 && position.X <= element.ActualWidth && position.Y <= element.ActualHeight;

    private void ShowGhost(Gesture gesture)
    {
        var lane = gesture.Target ?? gesture.Source;
        if (gesture.Ghost is null)
        {
            gesture.Ghost = new Border
            {
                Background = Brush("DaisyBase200Brush"), BorderBrush = Brush("DaisyPrimaryBrush"),
                BorderThickness = new Thickness(SelectionThickness), CornerRadius = new CornerRadius(EventCornerRadius),
                Opacity = 0.85, IsHitTestVisible = false
            };
            Canvas.SetZIndex(gesture.Ghost, 100);
        }
        if (!ReferenceEquals(gesture.Ghost.Parent, lane.Surface))
        {
            (gesture.Ghost.Parent as Panel)?.Children.Remove(gesture.Ghost);
            lane.Surface.Children.Add(gesture.Ghost);
        }
        gesture.Ghost.Child = Label(FormatTime(gesture.Start) + " – " + FormatTime(gesture.End));
        var begin = (gesture.Start - lane.Origin).TotalMinutes * lane.PixelsPerMinute;
        var length = Math.Max(ChipHeight, (gesture.End - gesture.Start).TotalMinutes * lane.PixelsPerMinute);
        gesture.Ghost.Width = lane.Horizontal ? length : lane.Surface.Width - 4;
        gesture.Ghost.Height = lane.Horizontal ? ChipHeight : length;
        Canvas.SetLeft(gesture.Ghost, lane.Horizontal ? begin : 2);
        Canvas.SetTop(gesture.Ghost, lane.Horizontal ? 4 : begin);
    }

    private void OnGesturePointerReleased(object? sender, PointerRoutedEventArgs e)
    {
        if (_gesture is not null && ReleasePointer(new PointerInput(e)))
            e.Handled = true;
    }

    internal bool ReleasePointer(PointerInput input)
    {
        if (_gesture is not { } gesture || gesture.PointerId != input.PointerId)
            return false;
        CancelGesture();
        if (IsReadOnly)
            return false;
        var lane = gesture.Target ?? gesture.Source;
        var position = input.WindowPosition;
        if (!gesture.Started)
        {
            if (gesture.Kind == GestureKind.Create && TryStoredTime(gesture.PressTime, lane, out var slot))
                SelectSlot(slot, lane.Resource, false, gesture.ClickCount, position);
            return true;
        }
        if (!TryStoredTime(gesture.Start, lane, out var start) || !TryStoredTime(gesture.End, lane, out var end, isEnd: true) || end <= start)
            return true;
        if (gesture.Kind == GestureKind.Create)
            AppointmentCreateRequested?.Invoke(this, new AppointmentCreateArgs { Start = start, End = end, ResourceId = lane.Resource?.Id, Position = position });
        else if (gesture.Appointment is { } appointment)
        {
            if (!TryStoredTime(gesture.OriginalStart, gesture.Source, out var originalStart) ||
                !TryStoredTime(gesture.OriginalEnd, gesture.Source, out var originalEnd, isEnd: true))
                return true;
            // Daily spans and a Planner without weekends draw copies that differ from the stored times:
            // apply the gesture to the stored times as a shift. A hidden-day axis clips the drawn end, so a move
            // shifts both ends by the start's offset.
            if (appointment.SpanMode == AppointmentSpanMode.Daily || lane.Days is not null)
            {
                var startShift = start - originalStart;
                var endShift = gesture.Kind == GestureKind.Move && lane.Days is not null ? startShift : end - originalEnd;
                start = appointment.Start + (gesture.Kind == GestureKind.ResizeEnd ? TimeSpan.Zero : startShift);
                end = appointment.End + (gesture.Kind == GestureKind.ResizeStart ? TimeSpan.Zero : endShift);
            }
            RaiseEditGesture(gesture, appointment, lane, start, end, input.Modifiers);
        }
        return true;
    }

    private void RaiseEditGesture(Gesture gesture, AppointmentModel appointment, Lane lane, DateTime start, DateTime end, VirtualKeyModifiers modifiers)
    {
        if (!IsValidGestureRange(appointment, start, end, gesture.Kind != GestureKind.Move))
            return;
        var withShift = modifiers.HasFlag(VirtualKeyModifiers.Shift);
        var copy = (modifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Windows)) != 0;
        var multi = !lane.Horizontal && _selection.Count > 1;
        if (gesture.Kind == GestureKind.Move)
        {
            var delta = start - appointment.Start;
            if (multi)
            {
                var items = _selection.Where(static selected => !selected.IsLocked)
                    .Select(selected => GroupMoveItem(selected, gesture.Source, lane, delta)).ToArray();
                if (items.All(item => IsValidGestureRange(item.Appointment, item.NewStart, item.NewEnd, false)))
                    AppointmentsMoveRequested?.Invoke(this, new AppointmentsMoveArgs { Items = items, IsCopy = copy, WithShift = withShift });
            }
            else
                AppointmentMoveRequested?.Invoke(this, new AppointmentMoveArgs
                {
                    Appointment = appointment, NewStart = start, NewEnd = end,
                    FromResourceId = gesture.Source.Resource?.Id, ToResourceId = lane.Resource?.Id, IsCopy = copy, WithShift = withShift
                });
        }
        else if (multi)
        {
            var startDelta = start - appointment.Start;
            var endDelta = end - appointment.End;
            var items = _selection.Where(static selected => !selected.IsLocked).Select(selected => new AppointmentResizeItem
            {
                Appointment = selected,
                NewStart = selected.Start + startDelta,
                NewEnd = selected.End + endDelta
            }).ToArray();
            if (items.All(item => IsValidGestureRange(item.Appointment, item.NewStart, item.NewEnd, true)))
                AppointmentsResizeRequested?.Invoke(this, new AppointmentsResizeArgs { Items = items, WithShift = withShift });
        }
        else
            AppointmentResizeRequested?.Invoke(this, new AppointmentResizeArgs { Appointment = appointment, NewStart = start, NewEnd = end, WithShift = withShift });
    }

    private bool IsValidGestureRange(AppointmentModel appointment, DateTime start, DateTime end, bool resizing)
    {
        var duration = appointment is { SpanMode: AppointmentSpanMode.Daily, IsAllDay: false }
            ? end.TimeOfDay - start.TimeOfDay : end - start;
        return end > start && duration > TimeSpan.Zero &&
            (!resizing || duration >= TimeSpan.FromMinutes(SnapMinutes));
    }

    private AppointmentMoveItem GroupMoveItem(AppointmentModel appointment, Lane source, Lane target, TimeSpan delta)
    {
        var resources = _lanes.Select(static lane => lane.Resource).OfType<ResourceModel>()
            .DistinctBy(static resource => resource.Id).ToArray();
        var sourceIndex = Array.FindIndex(resources, resource => Equals(resource.Id, source.Resource?.Id));
        var targetIndex = Array.FindIndex(resources, resource => Equals(resource.Id, target.Resource?.Id));
        var itemIndex = _selectionResourceIds.TryGetValue(appointment, out var selectedResourceId)
            ? Array.FindIndex(resources, resource => Equals(resource.Id, selectedResourceId) && appointment.ResourceIds.Contains(resource.Id))
            : -1;
        if (itemIndex < 0)
            itemIndex = Array.FindIndex(resources, resource => appointment.ResourceIds.Contains(resource.Id));
        var movedIndex = itemIndex + targetIndex - sourceIndex;
        var from = itemIndex >= 0 ? resources[itemIndex].Id : null;
        var to = itemIndex >= 0 && sourceIndex >= 0 && targetIndex >= 0 && movedIndex >= 0 && movedIndex < resources.Length ? resources[movedIndex].Id : from;
        return new AppointmentMoveItem
        {
            Appointment = appointment, NewStart = appointment.Start + delta, NewEnd = appointment.End + delta,
            FromResourceId = from, ToResourceId = to
        };
    }

    private static DateTime ShownTime(DateTime time, Lane lane)
    {
        if (!lane.Horizontal)
            return time;
        var shown = TimeZoneConverter.ToZone(time, lane.Resource?.TimeZoneId);
        return lane.Days is { } days ? AxisTime(shown, days) : shown;
    }

    /// <param name="isEnd">An end on a hidden-day boundary closes the earlier date.</param>
    private static bool TryStoredTime(DateTime time, Lane lane, out DateTime stored, bool isEnd = false)
    {
        stored = time;
        if (!lane.Horizontal)
            return true;
        var shown = lane.Days is { } days ? RealTime(time, days, isEnd) : time;
        return TimeZoneConverter.TryFromZone(shown, lane.Resource?.TimeZoneId, out stored);
    }

    private void SelectSlot(DateTime start, ResourceModel? resource, bool allDay, int clicks, Point position)
    {
        _selection.Clear();
        CommitSelection();
        var slot = new EmptySlotSelectedArgs { Start = start, ResourceId = resource?.Id, IsAllDay = allDay, Position = position };
        EmptySlotSelected?.Invoke(this, slot);
        if (SingleClickCreates ? clicks == 1 : clicks == 2)
            AppointmentCreateRequested?.Invoke(this, new AppointmentCreateArgs
            {
                Start = start, End = allDay ? start.AddDays(1) : start.AddHours(1),
                ResourceId = resource?.Id, IsAllDay = allDay, Position = position
            });
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        if (_gesture?.PointerId == e.Pointer.PointerId)
            CancelGesture();
        base.OnPointerCaptureLost(e);
    }

    private void CancelGesture()
    {
        var gesture = _gesture;
        _gesture = null;
        if (gesture?.Ghost?.Parent is Panel panel)
            panel.Children.Remove(gesture.Ghost);
        if (gesture?.Pointer is { } pointer)
            gesture.Capture.ReleasePointerCapture(pointer);
    }

    // Qualified: on Android the base View declares its own DragEventArgs.
    private void OnLaneDragOver(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (sender is Canvas surface)
            e.AcceptedOperation = DragOverLane(surface, e.DataView, e.GetPosition(surface));
        e.Handled = true;
    }

    internal DataPackageOperation DragOverLane(Canvas surface, DataPackageView data, Point position) =>
        !IsReadOnly && surface.Tag is Lane lane && ExternalDropHandler.TryGetItem(data, out _) &&
        TryStoredTime(SlotTime(lane, position), lane, out _)
            ? DataPackageOperation.Copy : DataPackageOperation.None;

    private void OnLaneDrop(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (sender is not Canvas surface || IsReadOnly || !ExternalDropHandler.TryGetItem(e.DataView, out _))
            return;
        e.Handled = true;
        if (!DropOnLane(surface, e.DataView, e.GetPosition(surface)))
            e.AcceptedOperation = DataPackageOperation.None;
    }

    /// <summary>Raises <see cref="ExternalAppointmentDropped"/>; false when the slot does not exist in the lane's zone.</summary>
    internal bool DropOnLane(Canvas surface, DataPackageView data, Point position)
    {
        if (IsReadOnly || surface.Tag is not Lane lane || !ExternalDropHandler.TryGetItem(data, out var item) ||
            !TryStoredTime(SlotTime(lane, position), lane, out var start))
            return false;
        ExternalDropHandler.Release(item);
        ExternalAppointmentDropped?.Invoke(this, new ExternalDropArgs { Item = item, Start = start, End = start + item.DefaultDuration, ResourceId = lane.Resource?.Id });
        return true;
    }
}
