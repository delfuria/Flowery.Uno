using Flowery.Uno.Photos.Services.Platform;
using Microsoft.UI.Dispatching;

namespace Flowery.Uno.Photos.Services
{
    /// <summary>
    /// System accent color for borders (window frame, hover, focus and selection frames).
    /// <see cref="Brush"/> is one shared instance whose color follows the system setting, so elements using it update live.
    /// The class keeps its Flowery.WinUI.Photos name; on macOS and Linux it reads that OS's accent.
    /// </summary>
    public static class WindowsAccent
    {
        private static DispatcherQueue? _dispatcher;

        public static SolidColorBrush Brush { get; } = new(ReadAccent());

        public static Color Color => Brush.Color;

        /// <summary>Raised on the UI thread after the accent color changed.</summary>
        public static event EventHandler? Changed;

        public static void Initialize(DispatcherQueue dispatcher)
        {
            if (_dispatcher != null)
                return;
            _dispatcher = dispatcher;
        }

        // Skia: there is no ColorValuesChanged event, so the window calls this when it is activated.
        public static void Refresh()
        {
            var accent = ReadAccent();
            if (accent == Brush.Color)
                return;
            Brush.Color = accent;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static Color ReadAccent()
        {
            if (PlatformServices.Current.GetSystemAccent() is { } accent)
                return Color.FromArgb(0xFF, accent.R, accent.G, accent.B);

            // No readable system accent: the theme's primary color.
            return DaisyResourceLookup.GetBrush("DaisyPrimaryBrush") is SolidColorBrush primary
                ? primary.Color
                : Color.FromArgb(0xFF, 0x33, 0x99, 0xFF);
        }
    }
}
