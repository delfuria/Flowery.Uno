using System;
using System.Collections.Generic;
using Flowery.Helpers;
using Flowery.Theming;
using Microsoft.UI.Xaml;

namespace Flowery.Controls
{
    /// <summary>
    /// Draws the surfaces of a control that does not derive from <see cref="DaisyBaseContentControl"/>.
    /// The owner implements <see cref="IDaisyNeumorphicTarget"/> by forwarding to <see cref="Refresh"/>
    /// and <see cref="RequestRefresh"/>, and requests a refresh whenever it re-applies its theme or size.
    /// </summary>
    internal sealed class DaisyNeumorphicParticipant
    {
        private readonly FrameworkElement _owner;
        private readonly Func<IReadOnlyList<DaisyNeumorphicSurface>> _getSurfaces;
        private readonly Func<DaisySize> _getSize;
        private readonly DaisyNeumorphicSurfaceSet _surfaces;

        private DaisyNeumorphicParticipant(
            FrameworkElement owner,
            Func<IReadOnlyList<DaisyNeumorphicSurface>> getSurfaces,
            Func<DaisySize> getSize)
        {
            _owner = owner;
            _getSurfaces = getSurfaces;
            _getSize = getSize;
            _surfaces = new DaisyNeumorphicSurfaceSet(owner);

            _owner.Unloaded += OnOwnerUnloaded;
        }

        /// <summary>
        /// Creates the participant and attaches the neumorphic lifecycle to <paramref name="owner"/>.
        /// </summary>
        public static DaisyNeumorphicParticipant Create<TOwner>(
            TOwner owner,
            Func<IReadOnlyList<DaisyNeumorphicSurface>> getSurfaces,
            Func<DaisySize> getSize)
            where TOwner : FrameworkElement, IDaisyNeumorphicTarget
        {
            DaisyNeumorphicLifecycle.Attach(owner, refreshOnThemeChange: false);
            return new DaisyNeumorphicParticipant(owner, getSurfaces, getSize);
        }

        public void Refresh()
        {
            if (DaisyBaseContentControl.DisableNeumorphicAutoRefresh || !_owner.IsLoaded)
                return;

            var resolution = DaisyNeumorphic.Resolve(_owner);
            _surfaces.Update(
                _getSurfaces(),
                resolution.IsEnabled,
                resolution.Mode,
                resolution.Intensity,
                DaisyResourceLookup.GetDefaultElevation(_getSize()));
        }

        public void RequestRefresh()
        {
            if (DaisyBaseContentControl.DisableNeumorphicAutoRefresh || !_owner.IsLoaded)
                return;

            DaisyNeumorphicRefreshHelper.QueueRefresh(_owner);
        }

        private void OnOwnerUnloaded(object sender, RoutedEventArgs e) => _surfaces.Suspend();
    }
}
