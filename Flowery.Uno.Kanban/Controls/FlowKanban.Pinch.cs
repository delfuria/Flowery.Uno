using System;
using System.Collections.Generic;
using System.Linq;
using Flowery.Helpers;
using Microsoft.UI.Xaml.Input;

namespace Flowery.Uno.Kanban.Controls
{
    /// <summary>
    /// Two fingers zoom the board through its sizes, as the zoom buttons and Ctrl+Plus or Minus do: spreading them
    /// zooms in, pinching zooms out, one size per <see cref="PinchStep"/> of their distance. Ctrl (Command) and the
    /// mouse wheel do the same, one size per notch. A pinch neither selects nor drags a card.
    /// </summary>
    public partial class FlowKanban
    {
        // The factor of the fingers' distance for one size, and the size that one notch of the wheel changes.
        private const double PinchStep = 1.25;

        private readonly FloweryPinchTracker _pinch = new();
        private int _pinchSteps;
        // The selection before the first finger of a touch landed; a card selects on press, which a pinch undoes.
        private List<(FlowTask Task, bool Selected)>? _selectionBeforeTouch;

        // Tests call the tracker as the pointer events do.
        internal FloweryPinchTracker Pinch => _pinch;

        /// <summary>Whether the last touch pinched: until the next press, a card that would start a drag does not.</summary>
        internal bool IsPinchTouch => _pinch.HasPinched;

        private void InitializePinch()
        {
            _pinch.Started += OnPinchStarted;
            _pinch.Changed += OnPinchChanged;
            _pinch.Attach(this);
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnZoomWheel), true);
        }

        /// <summary>Called by a card before a touch press selects it, so a pinch that follows can undo the selection.</summary>
        internal void RememberSelectionBeforeTouch()
        {
            // A later finger of the same touch keeps the selection before the first.
            if (_pinch.IsTouching)
                return;
            _selectionBeforeTouch = Board?.Columns.SelectMany(static column => column.Tasks)
                .Select(static task => (task, task.IsSelected)).ToList();
        }

        private void OnPinchStarted()
        {
            _pinchSteps = 0;
            if (_selectionBeforeTouch is { } selection)
            {
                foreach (var (task, selected) in selection)
                    task.IsSelected = selected;
                _selectionBeforeTouch = null;
            }
        }

        private void OnPinchChanged(double scale, Windows.Foundation.Point middle)
        {
            // Truncated: a size changes only once the fingers moved a whole step.
            var steps = (int)(Math.Log(scale) / Math.Log(PinchStep));
            for (; _pinchSteps < steps && CanZoomIn(); _pinchSteps++)
                ExecuteZoomIn();
            for (; _pinchSteps > steps && CanZoomOut(); _pinchSteps--)
                ExecuteZoomOut();
        }

        private void OnZoomWheel(object sender, PointerRoutedEventArgs e)
        {
            if (ZoomByWheel(e.KeyModifiers, e.GetCurrentPoint(this).Properties.MouseWheelDelta))
                e.Handled = true;
        }

        /// <summary>Ctrl (Command) and the wheel change the size by one per notch; returns whether they did.</summary>
        internal bool ZoomByWheel(Windows.System.VirtualKeyModifiers modifiers, int delta)
        {
            if ((modifiers & (Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Windows)) == 0 ||
                delta == 0 || !IsBoardViewActive)
                return false;
            if (delta > 0)
                ExecuteZoomIn();
            else
                ExecuteZoomOut();
            return true;
        }
    }
}
