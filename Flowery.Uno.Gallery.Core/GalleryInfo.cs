using System.Reflection;
using Flowery.Controls;

namespace Flowery.Uno.Gallery
{
    /// <summary>
    /// Describes the Flowery.Uno library the Gallery was built with.
    /// </summary>
    public static class GalleryInfo
    {
        /// <summary>
        /// The library version, e.g. <c>v1.2.1</c>. Pre-release suffixes are kept; build metadata (<c>+commit</c>) is removed.
        /// </summary>
        public static string Version { get; } = ResolveVersion();

        private static string ResolveVersion()
        {
            var assembly = typeof(DaisyButton).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString(3)
                          ?? "0.0.0";

            var metadataStart = version.IndexOf('+');
            if (metadataStart >= 0)
            {
                version = version[..metadataStart];
            }

            return "v" + version;
        }
    }
}
