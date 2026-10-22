using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Flowery.Controls
{
    /// <summary>
    /// Draws the neumorphic effect on the surfaces of one control, one <see cref="DaisyNeumorphicHelper"/> per surface host.
    /// </summary>
    internal sealed class DaisyNeumorphicSurfaceSet
    {
        private readonly FrameworkElement _owner;
        private readonly Dictionary<FrameworkElement, DaisyNeumorphicHelper> _helpers = [];

        public DaisyNeumorphicSurfaceSet(FrameworkElement owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// Applies the effect to the given surfaces and removes it from hosts that are no longer listed.
        /// </summary>
        /// <param name="surfaces">The control's current surfaces.</param>
        /// <param name="enabled">Whether the control draws the effect.</param>
        /// <param name="mode">The control's effective mode; surfaces with a fixed mode ignore it.</param>
        /// <param name="intensity">The control's effective intensity.</param>
        /// <param name="elevation">The shadow elevation for the control's size.</param>
        /// <param name="ownerSurfaceBrush">The owner's own background while it paints a transparent one.</param>
        public void Update(
            IReadOnlyList<DaisyNeumorphicSurface> surfaces,
            bool enabled,
            DaisyNeumorphicMode mode,
            double intensity,
            double elevation,
            Brush? ownerSurfaceBrush = null)
        {
            RemoveStale(surfaces);

            foreach (var surface in surfaces)
            {
                var host = surface.Host;
                if (!_helpers.TryGetValue(host, out var helper))
                {
                    if (!enabled)
                        continue;

                    helper = new DaisyNeumorphicHelper(host);
                    _helpers[host] = helper;
                }

                var isOwner = ReferenceEquals(host, _owner);
                if (!isOwner)
                {
                    SyncOverrides(host);
                }

                helper.SurfaceBrushOverride = isOwner ? ownerSurfaceBrush : null;
                // A control that is its own surface has none without a background; a declared surface does.
                helper.RequiresBackground = isOwner;
                helper.Update(enabled, surface.FixedMode ?? mode, intensity, elevation);
            }
        }

        /// <summary>
        /// Removes the drawn layers while the control is unloaded; the next <see cref="Update"/> restores them.
        /// </summary>
        public void Suspend()
        {
            foreach (var helper in _helpers.Values)
            {
                helper.Suspend();
            }
        }

        /// <summary>
        /// Disposes all helpers.
        /// </summary>
        public void Clear()
        {
            foreach (var helper in _helpers.Values)
            {
                helper.Dispose();
            }

            _helpers.Clear();
        }

        private void RemoveStale(IReadOnlyList<DaisyNeumorphicSurface> surfaces)
        {
            if (_helpers.Count == 0)
                return;

            var stale = _helpers.Keys
                .Where(host => !surfaces.Any(surface => ReferenceEquals(surface.Host, host)))
                .ToList();

            foreach (var host in stale)
            {
                _helpers[host].Dispose();
                _helpers.Remove(host);
            }
        }

        /// <summary>
        /// Copies the shadow settings set on the owner to a surface host inside it, which the helper reads.
        /// </summary>
        private void SyncOverrides(FrameworkElement host)
        {
            SyncOverride(host, DaisyNeumorphic.OffsetProperty, DaisyNeumorphic.GetOffset(_owner));
            SyncOverride(host, DaisyNeumorphic.BlurRadiusProperty, DaisyNeumorphic.GetBlurRadius(_owner));
            SyncOverride(host, DaisyNeumorphic.DarkShadowColorProperty, DaisyNeumorphic.GetDarkShadowColor(_owner));
            SyncOverride(host, DaisyNeumorphic.LightShadowColorProperty, DaisyNeumorphic.GetLightShadowColor(_owner));
        }

        private void SyncOverride(FrameworkElement host, DependencyProperty property, object? value)
        {
            if (ShouldApplyOverride(property, value))
            {
                host.SetValue(property, value);
            }
            else
            {
                host.ClearValue(property);
            }
        }

        private bool ShouldApplyOverride(DependencyProperty property, object? value)
        {
            if (_owner.ReadLocalValue(property) != DependencyProperty.UnsetValue)
            {
                return true;
            }

            var defaultValue = property.GetMetadata(_owner.GetType())?.DefaultValue;
            if (defaultValue == null)
            {
                return value != null;
            }

            return !Equals(value, defaultValue);
        }
    }
}
