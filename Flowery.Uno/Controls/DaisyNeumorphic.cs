using System;
using System.Collections.Generic;
using Flowery.Helpers;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Flowery.Controls
{
    /// <summary>
    /// Provides attached properties for neumorphic visual effects.
    /// This allows any control to opt-in to neumorphic effects without requiring a specific base class.
    /// </summary>
    public static class DaisyNeumorphic
    {
        private static readonly HashSet<Type> SegmentContainerTypes = [typeof(DaisyJoin), typeof(DaisyButtonGroup)];
        private static (Color Dark, Color Light)? _themeShadowColors;

        #region IsEnabled

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool?),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(null, OnPropertyChanged));

        public static void SetIsEnabled(DependencyObject element, bool? value) => element.SetValue(IsEnabledProperty, value);
        public static bool? GetIsEnabled(DependencyObject element) => (bool?)element.GetValue(IsEnabledProperty);

        #endregion

        #region Mode

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.RegisterAttached(
                "Mode",
                typeof(DaisyNeumorphicMode?),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(null, OnPropertyChanged));

        public static void SetMode(DependencyObject element, DaisyNeumorphicMode? value) => element.SetValue(ModeProperty, value);
        public static DaisyNeumorphicMode? GetMode(DependencyObject element) => (DaisyNeumorphicMode?)element.GetValue(ModeProperty);

        #endregion

        #region Intensity

        public static readonly DependencyProperty IntensityProperty =
            DependencyProperty.RegisterAttached(
                "Intensity",
                typeof(double?),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(null, OnPropertyChanged));

        public static void SetIntensity(DependencyObject element, double? value) => element.SetValue(IntensityProperty, value);
        public static double? GetIntensity(DependencyObject element) => (double?)element.GetValue(IntensityProperty);

        #endregion

        #region BlurRadius

        public static readonly DependencyProperty BlurRadiusProperty =
            DependencyProperty.RegisterAttached(
                "BlurRadius",
                typeof(double),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(4.0, OnPropertyChanged));

        public static void SetBlurRadius(DependencyObject element, double value) => element.SetValue(BlurRadiusProperty, value);
        public static double GetBlurRadius(DependencyObject element) => (double)element.GetValue(BlurRadiusProperty);

        #endregion

        #region Offset

        public static readonly DependencyProperty OffsetProperty =
            DependencyProperty.RegisterAttached(
                "Offset",
                typeof(double),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(4.0, OnPropertyChanged));

        public static void SetOffset(DependencyObject element, double value) => element.SetValue(OffsetProperty, value);
        public static double GetOffset(DependencyObject element) => (double)element.GetValue(OffsetProperty);

        #endregion

        #region DarkShadowColor

        public static readonly DependencyProperty DarkShadowColorProperty =
            DependencyProperty.RegisterAttached(
                "DarkShadowColor",
                typeof(Color?),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(null, OnPropertyChanged));

        public static void SetDarkShadowColor(DependencyObject element, Color? value) => element.SetValue(DarkShadowColorProperty, value);
        public static Color? GetDarkShadowColor(DependencyObject element) => (Color?)element.GetValue(DarkShadowColorProperty);

        #endregion

        #region LightShadowColor

        public static readonly DependencyProperty LightShadowColorProperty =
            DependencyProperty.RegisterAttached(
                "LightShadowColor",
                typeof(Color?),
                typeof(DaisyNeumorphic),
                new PropertyMetadata(null, OnPropertyChanged));

        public static void SetLightShadowColor(DependencyObject element, Color? value) => element.SetValue(LightShadowColorProperty, value);
        public static Color? GetLightShadowColor(DependencyObject element) => (Color?)element.GetValue(LightShadowColorProperty);

        #endregion

        #region Segment Containers

        /// <summary>
        /// Gets whether <paramref name="type"/> is (or derives from) a container whose child controls are flat
        /// segments of one surface. A Daisy control inside it never draws the effect; a scope cannot override this.
        /// </summary>
        private static bool IsSegmentContainer(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SegmentContainerTypes.Contains(current))
                    return true;
            }

            return false;
        }

        #endregion

        #region Effective Values

        /// <summary>
        /// Gets the nearest <see cref="DaisyBaseContentControl.NeumorphicScopeEnabledProperty"/> value
        /// on the element or one of its ancestors.
        /// </summary>
        public static bool? GetScopeEnabled(DependencyObject element)
        {
            if (element.GetValue(DaisyBaseContentControl.NeumorphicScopeEnabledProperty) is bool own)
                return own;

            foreach (var ancestor in GetAncestors(element))
            {
                if (ancestor.GetValue(DaisyBaseContentControl.NeumorphicScopeEnabledProperty) is bool value)
                    return value;
            }

            return null;
        }

        /// <summary>
        /// Resolves whether the element draws the effect, and with which mode and intensity, in one walk up the tree.
        /// </summary>
        /// <remarks>
        /// <para>Enabled, in order of precedence: inside a segment container (structural, cannot be overridden),
        /// then the nearest scope, then the element's own <see cref="IsEnabledProperty"/>, then
        /// <see cref="DaisyBaseContentControl.GlobalNeumorphicEnabled"/>. A None mode is never enabled.</para>
        /// <para>Mode and intensity: the element's own value, then the nearest scope value, then the global default.
        /// The intensity is clamped to 0.0 to 1.0.</para>
        /// </remarks>
        internal static DaisyNeumorphicResolution Resolve(FrameworkElement element)
        {
            var scopeEnabled = element.GetValue(DaisyBaseContentControl.NeumorphicScopeEnabledProperty) as bool?;
            var scopeMode = element.GetValue(DaisyBaseContentControl.NeumorphicScopeModeProperty) as DaisyNeumorphicMode?;
            var scopeIntensity = element.GetValue(DaisyBaseContentControl.NeumorphicScopeIntensityProperty) as double?;

            // Decided by the nearest Daisy control above the element: segments end at their own context.
            bool? inSegment = null;

            foreach (var ancestor in GetAncestors(element))
            {
                if (inSegment == null && IsSegmentContainer(ancestor.GetType()))
                    inSegment = true;
                else if (inSegment == null && ancestor is IDaisyNeumorphicTarget)
                    inSegment = false;

                scopeEnabled ??= ancestor.GetValue(DaisyBaseContentControl.NeumorphicScopeEnabledProperty) as bool?;
                scopeMode ??= ancestor.GetValue(DaisyBaseContentControl.NeumorphicScopeModeProperty) as DaisyNeumorphicMode?;
                scopeIntensity ??= ancestor.GetValue(DaisyBaseContentControl.NeumorphicScopeIntensityProperty) as double?;

                if (inSegment != null && scopeEnabled != null && scopeMode != null && scopeIntensity != null)
                    break;
            }

            var mode = GetMode(element) ?? scopeMode ?? DaisyBaseContentControl.GlobalNeumorphicMode;
            var intensity = Math.Clamp(
                GetIntensity(element) ?? scopeIntensity ?? DaisyBaseContentControl.GlobalNeumorphicIntensity,
                0.0,
                1.0);
            var enabled = inSegment != true
                && (scopeEnabled ?? GetIsEnabled(element) ?? DaisyBaseContentControl.GlobalNeumorphicEnabled)
                && mode != DaisyNeumorphicMode.None;

            return new DaisyNeumorphicResolution(enabled, mode, intensity);
        }

        /// <summary>
        /// Gets the element's dark shadow color: its own value, then
        /// <see cref="DaisyBaseContentControl.GlobalNeumorphicDarkShadowColor"/>, then the color derived from the theme.
        /// </summary>
        internal static Color GetEffectiveDarkShadowColor(DependencyObject element) =>
            GetDarkShadowColor(element)
            ?? DaisyBaseContentControl.GlobalNeumorphicDarkShadowColor
            ?? GetThemeShadowColors().Dark;

        /// <summary>
        /// Gets the element's light shadow color: its own value, then
        /// <see cref="DaisyBaseContentControl.GlobalNeumorphicLightShadowColor"/>, then white.
        /// </summary>
        internal static Color GetEffectiveLightShadowColor(DependencyObject element) =>
            GetLightShadowColor(element)
            ?? DaisyBaseContentControl.GlobalNeumorphicLightShadowColor
            ?? GetThemeShadowColors().Light;

        private static IEnumerable<DependencyObject> GetAncestors(DependencyObject element)
        {
            var current = GetParent(element);
            while (current != null)
            {
                yield return current;
                current = GetParent(current);
            }
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
            if (parent == null && element is FrameworkElement fe)
            {
                parent = fe.Parent;
            }

            return parent;
        }

        #endregion

        #region Theme Shadow Colors

        /// <summary>
        /// Drops the cached theme shadow colors. Called by <see cref="DaisyThemeManager"/> before it raises ThemeChanged.
        /// </summary>
        internal static void InvalidateThemeShadowColors() => _themeShadowColors = null;

        /// <summary>
        /// Shadow colors derived from the current theme: the dark shadow is the theme's Base300 color
        /// darkened (stronger for dark themes), the light shadow is white.
        /// </summary>
        private static (Color Dark, Color Light) GetThemeShadowColors()
        {
            if (_themeShadowColors is { } cached)
                return cached;

            var base300 = Color.FromArgb(255, 128, 128, 128);
            if (Application.Current?.Resources is { } resources
                && resources.TryGetValue("DaisyBase300Color", out var value)
                && value is Color color)
            {
                base300 = color;
            }

            var themeName = DaisyThemeManager.CurrentThemeName;
            var isDark = themeName != null && DaisyThemeManager.IsDarkTheme(themeName);
            var colors = (Darken(base300, isDark ? 0.7 : 0.5), Color.FromArgb(255, 255, 255, 255));
            _themeShadowColors = colors;
            return colors;
        }

        /// <summary>
        /// Darkens a color by a factor (0.0 = no change, 1.0 = black). The result is always opaque.
        /// </summary>
        private static Color Darken(Color color, double factor)
        {
            factor = Math.Clamp(factor, 0.0, 1.0);
            return Color.FromArgb(
                255,
                (byte)(color.R * (1.0 - factor)),
                (byte)(color.G * (1.0 - factor)),
                (byte)(color.B * (1.0 - factor)));
        }

        #endregion

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IDaisyNeumorphicTarget target)
            {
                target.RequestNeumorphicRefresh();
            }
        }
    }
}
