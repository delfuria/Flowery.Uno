using Microsoft.UI.Xaml.Controls;

namespace Flowery.Uno.Gallery.Examples
{
    public sealed partial class ThemingExamples : ScrollableExamplePage
    {
        public ThemingExamples()
        {
            InitializeComponent();

            MixedThemeDropdown.AddTheme("Business", "MyBusiness", false);
            MixedThemeDropdown.AddTheme("TheaterCinema", "DarkAndRed", true);
            MixedThemeDropdown.AddTheme("Corporate", "Office", true);
        }

        protected override ScrollViewer? ScrollViewer => MainScrollViewer;
    }
}
