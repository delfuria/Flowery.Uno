using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Windows.Foundation;

namespace Flowery.Helpers
{
    /// <summary>
    /// Follows the touch pointers on an element and reports a pinch while two fingers are down: the scale of their
    /// distance against the distance when the second finger landed, and the point between them. A third finger is
    /// ignored; the pinch ends when one of the two lifts, is canceled or loses its capture. WinUI has no pinch event
    /// that works with the native scroll views of Android, so the tracker reads the pointer events, also those that a
    /// scroll viewer has handled. Uno on Android reports no release for the finger that lifts last after a pinch, so
    /// the tracker forgets both fingers when the first lifts. The pointer events report the change once per dispatcher
    /// turn: a zoom that lays out the content between the fingers of one touch event would shift the position that Uno
    /// on Android reports for the next finger.
    /// </summary>
    internal sealed class FloweryPinchTracker
    {
        // Below this distance in DIPs the two fingers are taken as one spot, and the scale would jump.
        private const double MinimumDistance = 8;

        private readonly List<(uint Id, Point Position)> _touches = new();
        private double _startDistance;
        private bool _changeQueued;

        /// <summary>Raised when the second finger lands.</summary>
        public event Action? Started;

        /// <summary>Raised when one of the two fingers moves: the scale against the start and the point between the
        /// fingers in window coordinates.</summary>
        public event Action<double, Point>? Changed;

        /// <summary>Raised when one of the two fingers lifts or is canceled.</summary>
        public event Action? Ended;

        /// <summary>Whether two fingers are down.</summary>
        public bool IsPinching { get; private set; }

        /// <summary>Whether the last touch pinched: until the next press, what its fingers do is no tap, hold or drag
        /// of their own.</summary>
        public bool HasPinched { get; private set; }

        /// <summary>Whether a finger is down that the tracker follows: a press now is not the first of a touch.</summary>
        public bool IsTouching => _touches.Count > 0;

        public void Attach(UIElement element)
        {
            element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
            element.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
            element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnLifted), true);
            element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnLifted), true);
            element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnLifted), true);
        }

        private static bool IsTouch(PointerRoutedEventArgs e) => e.Pointer.PointerDeviceType == PointerDeviceType.Touch;

        private void OnPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!IsTouch(e))
            {
                if (_touches.Count == 0)
                    HasPinched = false;
                return;
            }
#if __ANDROID__
            var wasPinching = IsPinching;
#endif
            Press(e.Pointer.PointerId, e.GetCurrentPoint(null).Position);
#if __ANDROID__
            // The native scroll views take over a touch that moves; from the second finger on, until the last lifts,
            // they leave it to the pinch. Android clears the request itself when the touch ends.
            if (IsPinching && !wasPinching)
                (e.OriginalSource as Android.Views.View)?.Parent?.RequestDisallowInterceptTouchEvent(true);
#endif
        }

        private void OnMoved(object sender, PointerRoutedEventArgs e)
        {
            if (IsTouch(e) && Move(e.Pointer.PointerId, e.GetCurrentPoint(null).Position, deferChange: true))
                e.Handled = true;
        }

        private void OnLifted(object sender, PointerRoutedEventArgs e)
        {
            if (IsTouch(e))
                Lift(e.Pointer.PointerId);
        }

        /// <summary>A finger lands at a window position; runtime tests call it directly.</summary>
        public void Press(uint pointerId, Point position)
        {
            if (IndexOf(pointerId) >= 0 || _touches.Count >= 2)
                return;
            if (_touches.Count == 0)
                HasPinched = false;
            _touches.Add((pointerId, position));
            if (_touches.Count < 2 || IsPinching)
                return;
            IsPinching = true;
            HasPinched = true;
            _startDistance = Math.Max(MinimumDistance, Distance());
            Started?.Invoke();
        }

        /// <summary>A finger moves; returns whether it belongs to a pinch, whose move is no scroll or drag. With
        /// <paramref name="deferChange"/>, <see cref="Changed"/> follows on the next dispatcher turn, once for all
        /// fingers that moved until then.</summary>
        public bool Move(uint pointerId, Point position, bool deferChange = false)
        {
            var index = IndexOf(pointerId);
            if (index < 0)
                return false;
            _touches[index] = (pointerId, position);
            if (!IsPinching)
                return false;
            if (!deferChange)
                RaiseChanged();
            else if (!_changeQueued)
            {
                // Not High: Uno on Android then laid out the zoom only at the next input.
                _changeQueued = DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.Normal, RaiseQueuedChange) == true;
                if (!_changeQueued)
                    RaiseChanged();
            }
            return true;
        }

        private void RaiseQueuedChange()
        {
            _changeQueued = false;
            if (IsPinching)
                RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke(Math.Max(MinimumDistance, Distance()) / _startDistance, Middle());

        /// <summary>A finger lifts, or its touch is canceled; one of a pinch ends it for both.</summary>
        public void Lift(uint pointerId)
        {
            var index = IndexOf(pointerId);
            if (index < 0)
                return;
            if (!IsPinching)
            {
                _touches.RemoveAt(index);
                return;
            }
            // A move that the dispatcher has not reported yet still counts.
            if (_changeQueued)
            {
                _changeQueued = false;
                RaiseChanged();
            }
            // The other finger may never report its release; it pinched, and does nothing else until the next press.
            _touches.Clear();
            IsPinching = false;
            Ended?.Invoke();
        }

        private int IndexOf(uint pointerId) => _touches.FindIndex(touch => touch.Id == pointerId);

        private double Distance()
        {
            var (x, y) = (_touches[1].Position.X - _touches[0].Position.X, _touches[1].Position.Y - _touches[0].Position.Y);
            return Math.Sqrt(x * x + y * y);
        }

        private Point Middle() => new((_touches[0].Position.X + _touches[1].Position.X) / 2,
            (_touches[0].Position.Y + _touches[1].Position.Y) / 2);
    }
}
