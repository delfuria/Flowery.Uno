using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Flowery.Uno.Photos.Localization;
using IOPath = System.IO.Path;

namespace Flowery.Uno.Photos
{
    public partial class App : Application
    {
        private MainWindow? _window;

        public App()
        {
            InitializeComponent();
            ConfigureCrashLogging();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                Launch();
            }
            catch (Exception ex)
            {
                LogFatal("OnLaunched", ex);
                throw;
            }
        }

        private void Launch()
        {
            // PhotosResources includes XamlControlsResources and the native Flowery dictionary.
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///PhotosResources.xaml")
            });
            DaisyResourceLookup.EnsureTokens();

            // Ensure the localization registers its resolver early.
            _ = PhotosLocalization.Instance;

            // Skia: PhotosSettings (saved language, theme and size) arrives with the library in phase 2.
            var startupFile = Environment.GetCommandLineArgs()
                .Skip(1)
                .FirstOrDefault(File.Exists);

            _window = new MainWindow(startupFile);
            FlowerySizeManager.MainWindow = _window;
            FloweryScaleManager.MainWindow = _window;

            _window.Activate();
        }

        internal static string GetStateDirectory()
        {
            var configuredRoot = Environment.GetEnvironmentVariable("FLOWERY_STATE_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                if (!IOPath.IsPathFullyQualified(configuredRoot))
                {
                    throw new ArgumentException("FLOWERY_STATE_ROOT must be an absolute directory path.");
                }

                return IOPath.GetFullPath(configuredRoot);
            }

            return IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flowery.Uno.Photos");
        }

        private void ConfigureCrashLogging()
        {
            UnhandledException += (_, e) =>
            {
                try
                {
                    LogFatal("Application.UnhandledException", e.Exception);
                }
                finally
                {
                    e.Handled = true;
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogFatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogFatal("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        private static void LogFatal(string source, Exception? ex)
        {
            var message = $"[{DateTimeOffset.Now:O}] {source}: {ex}\n";
            Debug.WriteLine(message);

            try
            {
                var dir = GetStateDirectory();
                Directory.CreateDirectory(dir);
                File.AppendAllText(IOPath.Combine(dir, "crash.log"), message);
            }
            catch
            {
                // Ignore file IO failures; Debug output is still useful.
            }
        }
    }
}
