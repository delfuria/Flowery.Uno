using System;
using Microsoft.UI.Xaml;

namespace Flowery.Controls
{
    /// <summary>
    /// The neumorphic part of a control's lifecycle: while the control is loaded it follows the global settings
    /// (and optionally theme changes), and it refreshes once after the first layout pass, when template parts and
    /// content built in Loaded are in the visual tree.
    /// </summary>
    internal sealed class DaisyNeumorphicLifecycle
    {
        private readonly FrameworkElement _owner;
        private readonly IDaisyNeumorphicTarget _target;
        private readonly bool _refreshOnThemeChange;
        private bool _subscribed;

        private DaisyNeumorphicLifecycle(FrameworkElement owner, IDaisyNeumorphicTarget target, bool refreshOnThemeChange)
        {
            _owner = owner;
            _target = target;
            _refreshOnThemeChange = refreshOnThemeChange;

            _owner.Loaded += OnOwnerLoaded;
            _owner.Unloaded += OnOwnerUnloaded;
        }

        /// <summary>
        /// Attaches the lifecycle to a control. The control's Loaded and Unloaded events keep it alive.
        /// </summary>
        /// <param name="owner">The control.</param>
        /// <param name="refreshOnThemeChange">
        /// False for controls that already request a refresh when they re-apply their theme.
        /// </param>
        public static void Attach<TOwner>(TOwner owner, bool refreshOnThemeChange)
            where TOwner : FrameworkElement, IDaisyNeumorphicTarget
        {
            _ = new DaisyNeumorphicLifecycle(owner, owner, refreshOnThemeChange);
        }

        private void OnOwnerLoaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed)
            {
                DaisyBaseContentControl.GlobalNeumorphicChanged += OnGlobalNeumorphicChanged;
                if (_refreshOnThemeChange)
                {
                    DaisyThemeManager.ThemeChanged += OnThemeChanged;
                }

                _subscribed = true;
            }

            // Content set in Loaded reaches the visual tree on the next layout pass. Refreshing earlier would find
            // an empty tree and put the layers on top of the content.
            _owner.LayoutUpdated += OnFirstLayout;
        }

        private void OnOwnerUnloaded(object sender, RoutedEventArgs e)
        {
            _owner.LayoutUpdated -= OnFirstLayout;

            if (!_subscribed)
                return;

            DaisyBaseContentControl.GlobalNeumorphicChanged -= OnGlobalNeumorphicChanged;
            if (_refreshOnThemeChange)
            {
                DaisyThemeManager.ThemeChanged -= OnThemeChanged;
            }

            _subscribed = false;
        }

        private void OnFirstLayout(object? sender, object e)
        {
            _owner.LayoutUpdated -= OnFirstLayout;
            _target.RequestNeumorphicRefresh();
        }

        private void OnGlobalNeumorphicChanged(object? sender, EventArgs e) => _target.RequestNeumorphicRefresh();

        private void OnThemeChanged(object? sender, string themeName) => _target.RequestNeumorphicRefresh();
    }
}
