namespace Flowery.Controls
{
    /// <summary>
    /// The resolved neumorphic settings of one element. See <see cref="DaisyNeumorphic.Resolve"/>.
    /// </summary>
    /// <param name="IsEnabled">Whether the element draws the effect.</param>
    /// <param name="Mode">The element's effective mode.</param>
    /// <param name="Intensity">The element's effective intensity (0.0 to 1.0).</param>
    internal readonly record struct DaisyNeumorphicResolution(bool IsEnabled, DaisyNeumorphicMode Mode, double Intensity);
}
