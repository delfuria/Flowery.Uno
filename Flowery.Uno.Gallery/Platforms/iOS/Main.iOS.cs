using UIKit;

namespace Flowery.Uno.Gallery.iOS;

public class EntryPoint
{
    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        // Native renderer: the Application class is the UIApplicationDelegate
        UIApplication.Main(args, null, typeof(App));
    }
}
