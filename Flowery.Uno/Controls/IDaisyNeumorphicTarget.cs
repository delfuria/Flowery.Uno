namespace Flowery.Controls
{
    /// <summary>
    /// A control that draws the neumorphic effect itself and can be refreshed by
    /// <see cref="Flowery.Helpers.DaisyNeumorphicRefreshHelper"/>.
    /// </summary>
    internal interface IDaisyNeumorphicTarget
    {
        /// <summary>
        /// Re-applies the effect now.
        /// </summary>
        void RefreshNeumorphicEffect();

        /// <summary>
        /// Queues a deferred refresh while the control is loaded.
        /// </summary>
        void RequestNeumorphicRefresh();
    }
}
