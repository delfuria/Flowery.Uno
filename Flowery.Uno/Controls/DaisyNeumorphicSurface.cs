using Microsoft.UI.Xaml;

namespace Flowery.Controls
{
    /// <summary>
    /// Describes one element of a control that draws the neumorphic effect.
    /// </summary>
    /// <param name="Host">
    /// The element whose visible surface draws the effect: a <c>Border</c>, a <c>Panel</c> or a <c>Control</c>.
    /// When the host is the control itself and paints no visible background, the control has no surface and
    /// draws nothing. Any other host is a declared surface; without a background of its own, Raised and Flat
    /// draw a Base200 surface behind it.
    /// </param>
    /// <param name="FixedMode">A mode this surface always uses instead of the control's effective mode.</param>
    public readonly record struct DaisyNeumorphicSurface(
        FrameworkElement Host,
        DaisyNeumorphicMode? FixedMode = null);
}
