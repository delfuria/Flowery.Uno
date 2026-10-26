using System;
using Flowery.Theming;
using Windows.System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
#if __ANDROID__
using Windows.UI.ViewManagement;
#endif

namespace Flowery.Controls
{
    /// <summary>
    /// A modal dialog control styled after DaisyUI's Modal component (Uno/WinUI).
    /// </summary>
    public partial class DaisyModal : DaisyBaseContentControl
    {
        private readonly Grid _root;
        private readonly Grid _dialogHost;
        private readonly Border _dialogBorder;
        private readonly ContentPresenter _contentPresenter;
        private readonly Grid _dialogLayout;
        private readonly Grid _dialogOverlay;
        private readonly Grid _grabBarContainer;
        private readonly Border _grabBar;
        private readonly TranslateTransform _dialogTransform = new TranslateTransform();
        private bool _isDragging;
        private Point _dragStartPosition;
        private Point _dragStartOffset;
        private bool _isSettingRoot;
        private Rect _occludedRect = Rect.Empty;
        private Rect _visibleBounds = Rect.Empty;
        private Thickness _inputPaneInsets;
#if __ANDROID__
        private InputPane? _inputPane;
#endif
        public DaisyModal()
        {
            // Ensure control stretches to fill parent
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            IsTabStop = true; // Required for KeyDown events
            // Modal starts closed - don't block hit-testing on elements behind it
            IsHitTestVisible = false;

            _contentPresenter = new ContentPresenter
            {
                Margin = new Thickness(24)
            };

            _dialogBorder = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _grabBar = new Border
            {
                Width = 40,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6
            };

            _grabBarContainer = new Grid
            {
                Height = 24,
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed
            };
            _grabBarContainer.Children.Add(_grabBar);

            _dialogLayout = new Grid();
            _dialogLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _dialogLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_grabBarContainer, 0);
            Grid.SetRow(_contentPresenter, 1);
            _dialogLayout.Children.Add(_grabBarContainer);
            _dialogLayout.Children.Add(_contentPresenter);
            _dialogBorder.Child = _dialogLayout;

            _dialogHost = new Grid
            {
                // Default background (will be updated by theme)
                Background = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // Add shadow for depth
                Shadow = new ThemeShadow(),
                Translation = new System.Numerics.Vector3(0, 0, 32)
            };
            _dialogHost.RenderTransform = _dialogTransform;
            _dialogHost.Children.Add(_dialogBorder);

            _dialogOverlay = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };
            _dialogHost.Children.Add(_dialogOverlay);

            _root = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Handle clicking outside to close
            _root.Tapped += (s, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, _root) && ShouldCloseOnOutsideClick())
                {
                    IsOpen = false;
                }
            };

            _root.Children.Add(_dialogHost);

            _isSettingRoot = true;
            base.Content = _root;
            _isSettingRoot = false;

            // Handle ESC key
            KeyDown += OnKeyDown;

            _grabBarContainer.PointerPressed += OnDialogPointerPressed;
            _grabBarContainer.PointerMoved += OnDialogPointerMoved;
            _grabBarContainer.PointerReleased += OnDialogPointerReleased;
            _grabBarContainer.PointerCanceled += OnDialogPointerCanceled;
            _grabBarContainer.PointerCaptureLost += OnDialogPointerCaptureLost;

            SizeChanged += OnSizeChanged;
        }

        protected override void OnLoaded()
        {
            base.OnLoaded();
            // OnUnloaded removes both handlers, so a modal that is loaded again needs them back.
            KeyDown -= OnKeyDown;
            KeyDown += OnKeyDown;
            SizeChanged -= OnSizeChanged;
            SizeChanged += OnSizeChanged;
            ApplyCornerRadius();
            UpdateDialogBounds();
            ApplyAll();
            AttachInputPane();
        }

        protected override void OnUnloaded()
        {
            base.OnUnloaded();
            KeyDown -= OnKeyDown;
            SizeChanged -= OnSizeChanged;
            DetachInputPane();
        }

        // With the soft input mode adjustNothing, Uno's default on Android, the on-screen keyboard covers the window
        // instead of shrinking it. The dialog has no scroll viewer that would pad for the keyboard, so the modal lays it
        // out above the covered part itself. With adjustResize the window ends above the keyboard and nothing is covered.
        private void AttachInputPane()
        {
#if __ANDROID__
            if (_inputPane is not null)
                return;

            _inputPane = InputPane.GetForCurrentView();
            _inputPane.Showing += OnInputPaneShowing;
            _inputPane.Hiding += OnInputPaneHiding;

            // A modal loaded while the keyboard is open, such as a dialog opened from a focused field.
            if (_inputPane.OccludedRect.Height > 0)
                ApplyInputPaneOcclusion(_inputPane.OccludedRect, ApplicationView.GetForCurrentView().VisibleBounds);
#endif
        }

        private void DetachInputPane()
        {
#if __ANDROID__
            if (_inputPane is null)
                return;

            _inputPane.Showing -= OnInputPaneShowing;
            _inputPane.Hiding -= OnInputPaneHiding;
            _inputPane = null;
#endif
            ApplyInputPaneOcclusion(Rect.Empty, Rect.Empty);
        }

#if __ANDROID__
        private void OnInputPaneShowing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            // Raised again whenever the height of the keyboard changes. The visible bounds leave out the status bar,
            // which the modal covers with its backdrop.
            ApplyInputPaneOcclusion(args.OccludedRect, ApplicationView.GetForCurrentView().VisibleBounds);
        }

        private void OnInputPaneHiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            ApplyInputPaneOcclusion(Rect.Empty, Rect.Empty);
        }
#endif

        /// <summary>
        /// Keeps the dialog out of the part of the window that the on-screen keyboard covers: the backdrop stays, and the
        /// dialog is centered and limited to the part between the top of the visible bounds and the keyboard. The modal
        /// keeps both rectangles and lays the dialog out again whenever its own size changes.
        /// </summary>
        /// <param name="occludedRect">The covered part, in screen coordinates on Android (where the keyboard is
        /// measured) and in window coordinates elsewhere; empty while the keyboard is hidden.</param>
        /// <param name="visibleBounds">The part of the window outside the system bars, in window coordinates; empty when
        /// there are none.</param>
        internal void ApplyInputPaneOcclusion(Rect occludedRect, Rect visibleBounds)
        {
            _occludedRect = occludedRect;
            _visibleBounds = visibleBounds;
            UpdateInputPaneInsets();
        }

        private void UpdateInputPaneInsets()
        {
            var insets = new Thickness();
            if (!_occludedRect.IsEmpty && _occludedRect.Height > 0 && ActualHeight > 0 && XamlRoot is not null)
            {
                var windowBounds = TransformToVisual(null).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
                var bottom = Math.Clamp(GetBottomForInputPane(windowBounds) - _occludedRect.Top, 0, ActualHeight);
                var top = bottom > 0 && !_visibleBounds.IsEmpty
                    ? Math.Clamp(_visibleBounds.Top - windowBounds.Top, 0, ActualHeight - bottom)
                    : 0;
                insets = new Thickness(0, top, 0, bottom);
            }

            if (insets == _inputPaneInsets)
                return;

            _inputPaneInsets = insets;
            _root.Padding = insets;
            UpdateDialogBounds();
            ClampDialogOffset();
        }

        private double GetBottomForInputPane(Rect windowBounds)
        {
#if __ANDROID__
            // Uno measures the keyboard against the screen, so compare with the modal's place there, as its own scroll
            // presenters do; the window starts below the screen's top in split-screen or a freeform window.
            var location = new int[2];
            GetLocationOnScreen(location);
            return global::Uno.UI.ViewHelper.PhysicalToLogicalPixels(location[1]) + ActualHeight;
#else
            return windowBounds.Bottom;
#endif
        }

        /// <summary>
        /// The part of the modal that the on-screen keyboard and the status bar above it take while the keyboard is
        /// shown; the dialog is centered in the rest. Zero while the keyboard is hidden.
        /// </summary>
        protected Thickness DialogAreaInsets => _inputPaneInsets;

        protected override void OnThemeChanged(string themeName)
        {
            base.OnThemeChanged(themeName);
            ApplyAll();
        }

        private void ApplyAll()
        {
            var resources = Application.Current?.Resources;
            if (resources == null) return;
            DaisyTokenDefaults.EnsureDefaults(resources);
            ApplyTheme(resources);
        }

        private void ApplyTheme(ResourceDictionary resources)
        {
            // Backdrop: Check for user-defined DaisyModalBackdropBrush, else use default from resources
            var backdropBrush = DaisyResourceLookup.TryGetControlBrush(this, "DaisyModal", "BackdropBrush")
                ?? DaisyResourceLookup.GetBrush(resources, "DaisyModalBackdropBrush", new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));
            _root.Background = backdropBrush;

            // Dialog background: Check for user-defined DaisyModalBackground, else fall back to DaisyBase100Brush
            var dialogBg = DaisyResourceLookup.TryGetControlBrush(this, "DaisyModal", "Background")
                ?? DaisyResourceLookup.GetBrush(resources, "DaisyBase100Brush", new SolidColorBrush(Colors.White));
            _dialogHost.Background = dialogBg;

            // Dialog foreground: Check for user-defined DaisyModalForeground, else fall back to DaisyBaseContentBrush
            var dialogFg = DaisyResourceLookup.TryGetControlBrush(this, "DaisyModal", "Foreground")
                ?? DaisyResourceLookup.GetBrush(resources, "DaisyBaseContentBrush", new SolidColorBrush(Colors.Black));
            Foreground = dialogFg;
            ApplyContentForeground(dialogFg);

            if (_grabBar != null)
            {
                var grabBarBrush = DaisyResourceLookup.TryGetControlBrush(this, "DaisyModal", "GrabBarBrush")
                    ?? DaisyResourceLookup.GetBrush(resources, "DaisyBase300Brush", new SolidColorBrush(Colors.LightGray));
                _grabBar.Background = grabBarBrush;
            }
        }

        protected override FrameworkElement? GetNeumorphicHostElement()
        {
            return _dialogHost;
        }

        protected virtual double DialogBoundsMargin => 16;

        protected Panel DialogOverlayLayer => _dialogOverlay;

        protected Point DialogOffset
        {
            get => new Point(_dialogTransform.X, _dialogTransform.Y);
            set
            {
                _dialogTransform.X = value.X;
                _dialogTransform.Y = value.Y;
            }
        }

        /// <summary>
        /// Controls whether tapping outside the dialog closes the modal.
        /// </summary>
        protected virtual bool ShouldCloseOnOutsideClick() => true;

        private void ApplyContentForeground(Brush? contentBrush)
        {
            if (contentBrush == null)
                return;

            var textBlockStyle = new Style(typeof(TextBlock));
            textBlockStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, contentBrush));

            _contentPresenter.Resources["DaisyModalTextBlockStyle"] = textBlockStyle;
            _contentPresenter.Resources[typeof(TextBlock)] = textBlockStyle;
        }

        private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape && IsOpen)
            {
                IsOpen = false;
                e.Handled = true;
            }
        }

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(
                nameof(IsOpen),
                typeof(bool),
                typeof(DaisyModal),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public static readonly DependencyProperty IsDraggableProperty =
            DependencyProperty.Register(
                nameof(IsDraggable),
                typeof(bool),
                typeof(DaisyModal),
                new PropertyMetadata(false, OnIsDraggableChanged));

        /// <summary>
        /// Gets or sets whether the dialog can be dragged by pointer.
        /// </summary>
        public bool IsDraggable
        {
            get => (bool)GetValue(IsDraggableProperty);
            set => SetValue(IsDraggableProperty, value);
        }

        public static readonly DependencyProperty TopLeftRadiusProperty =
            DependencyProperty.Register(
                nameof(TopLeftRadius),
                typeof(double),
                typeof(DaisyModal),
                new PropertyMetadata(16.0, OnCornerRadiusChanged));

        public double TopLeftRadius
        {
            get => (double)GetValue(TopLeftRadiusProperty);
            set => SetValue(TopLeftRadiusProperty, value);
        }

        public static readonly DependencyProperty TopRightRadiusProperty =
            DependencyProperty.Register(
                nameof(TopRightRadius),
                typeof(double),
                typeof(DaisyModal),
                new PropertyMetadata(16.0, OnCornerRadiusChanged));

        public double TopRightRadius
        {
            get => (double)GetValue(TopRightRadiusProperty);
            set => SetValue(TopRightRadiusProperty, value);
        }

        public static readonly DependencyProperty BottomLeftRadiusProperty =
            DependencyProperty.Register(
                nameof(BottomLeftRadius),
                typeof(double),
                typeof(DaisyModal),
                new PropertyMetadata(16.0, OnCornerRadiusChanged));

        public double BottomLeftRadius
        {
            get => (double)GetValue(BottomLeftRadiusProperty);
            set => SetValue(BottomLeftRadiusProperty, value);
        }

        public static readonly DependencyProperty BottomRightRadiusProperty =
            DependencyProperty.Register(
                nameof(BottomRightRadius),
                typeof(double),
                typeof(DaisyModal),
                new PropertyMetadata(16.0, OnCornerRadiusChanged));

        public double BottomRightRadius
        {
            get => (double)GetValue(BottomRightRadiusProperty);
            set => SetValue(BottomRightRadiusProperty, value);
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            if (_isSettingRoot)
                return;

            if (ReferenceEquals(newContent, _root))
                return;

            _contentPresenter.Content = newContent;

            if (!ReferenceEquals(base.Content, _root))
            {
                _isSettingRoot = true;
                base.Content = _root;
                _isSettingRoot = false;
            }
        }

        /// <summary>
        /// Sets the dialog host size. Useful for derived classes that calculate their own sizing.
        /// </summary>
        /// <param name="width">Dialog width.</param>
        /// <param name="height">Dialog height.</param>
        protected void SetDialogSize(double width, double height)
        {
            _dialogHost.Width = width;
            _dialogHost.Height = height;
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DaisyModal modal)
            {
                var isOpen = (bool)e.NewValue;
                modal._root.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
                // Enable/disable hit-testing so closed modal doesn't block pointer events
                modal.IsHitTestVisible = isOpen;

                if (isOpen)
                {
                    // Try to focus the modal so it captures key events (ESC)
                    modal.Focus(FocusState.Programmatic);
                    modal.UpdateDialogBounds();
                }
                else
                {
                    modal.StopDrag();
                }
            }
        }

        private static void OnIsDraggableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DaisyModal modal)
            {
                modal.UpdateGrabBarState();
                if (e.NewValue is bool isDraggable && !isDraggable)
                {
                    modal.StopDrag();
                }
            }
        }

        private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DaisyModal modal)
            {
                modal.ApplyCornerRadius();
            }
        }

        private void ApplyCornerRadius()
        {
            var radius = new CornerRadius(
                TopLeftRadius,
                TopRightRadius,
                BottomRightRadius,
                BottomLeftRadius);
            _dialogBorder.CornerRadius = radius;
            _dialogHost.CornerRadius = radius;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            UpdateDialogBounds();

            // A turned screen or a resized window moves the modal against the keyboard. Its place on the screen is
            // final only after this layout pass, and Android drops layout changes requested inside it.
            if (!_occludedRect.IsEmpty)
                DispatcherQueue?.TryEnqueue(UpdateInputPaneInsets);
        }

        private void UpdateDialogBounds()
        {
            if (_dialogHost == null)
                return;

            var width = ActualWidth;
            var height = ActualHeight - _inputPaneInsets.Top - _inputPaneInsets.Bottom;
            if (width <= 0 || height <= 0)
                return;

            var margin = Math.Max(0, DialogBoundsMargin);
            _dialogHost.MaxWidth = Math.Max(0, width - (margin * 2));
            _dialogHost.MaxHeight = Math.Max(0, height - (margin * 2));
        }

        private void UpdateGrabBarState()
        {
            if (_grabBarContainer == null)
                return;

            _grabBarContainer.Visibility = IsDraggable ? Visibility.Visible : Visibility.Collapsed;
            _grabBarContainer.IsHitTestVisible = IsDraggable;
        }

        private void OnDialogPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!IsDraggable)
                return;

            var point = e.GetCurrentPoint(_root);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            _isDragging = true;
            _dragStartPosition = point.Position;
            _dragStartOffset = new Point(_dialogTransform.X, _dialogTransform.Y);
            _grabBarContainer.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnDialogPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
                return;

            var point = e.GetCurrentPoint(_root).Position;
            var deltaX = point.X - _dragStartPosition.X;
            var deltaY = point.Y - _dragStartPosition.Y;

            var targetX = _dragStartOffset.X + deltaX;
            var targetY = _dragStartOffset.Y + deltaY;

            if (TryGetDragBounds(out var minX, out var maxX, out var minY, out var maxY))
            {
                if (minX <= maxX)
                    targetX = Math.Clamp(targetX, minX, maxX);
                if (minY <= maxY)
                    targetY = Math.Clamp(targetY, minY, maxY);
            }

            _dialogTransform.X = targetX;
            _dialogTransform.Y = targetY;
            e.Handled = true;
        }

        private void OnDialogPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            StopDrag();
        }

        private void OnDialogPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            StopDrag();
        }

        private void OnDialogPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            StopDrag();
        }

        private void StopDrag()
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            _grabBarContainer.ReleasePointerCaptures();
        }

        private bool TryGetDragBounds(out double minX, out double maxX, out double minY, out double maxY)
        {
            var margin = Math.Max(0, DialogBoundsMargin);
            minX = maxX = minY = maxY = 0;

            // The dialog is centered in the part above the on-screen keyboard and may already be limited to it.
            var rootWidth = _root.ActualWidth;
            var rootHeight = _root.ActualHeight - _inputPaneInsets.Top - _inputPaneInsets.Bottom;
            var hostWidth = _dialogHost.ActualWidth;
            var hostHeight = Math.Min(_dialogHost.ActualHeight, _dialogHost.MaxHeight);

            if (rootWidth <= 0 || rootHeight <= 0 || hostWidth <= 0 || hostHeight <= 0)
                return false;

            minX = (2 * margin - rootWidth + hostWidth) / 2;
            maxX = (rootWidth - 2 * margin - hostWidth) / 2;
            minY = (2 * margin - rootHeight + hostHeight) / 2;
            maxY = (rootHeight - 2 * margin - hostHeight) / 2;

            return true;
        }

        /// <summary>
        /// Brings a dragged or resized dialog back into the bounds, for example when the keyboard has made them smaller.
        /// </summary>
        protected void ClampDialogOffset()
        {
            if (!TryGetDragBounds(out var minX, out var maxX, out var minY, out var maxY))
                return;

            if (minX <= maxX)
                _dialogTransform.X = Math.Clamp(_dialogTransform.X, minX, maxX);
            if (minY <= maxY)
                _dialogTransform.Y = Math.Clamp(_dialogTransform.Y, minY, maxY);
        }

    }
}
