using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Flowery.Controls
{
    /// <summary>
    /// Hosts a sidebar entry so that keyboard and UI Automation can select it, like a tap does.
    /// </summary>
    internal sealed partial class SidebarItemHost : ContentControl
    {
        private readonly SidebarItem _item;
        private readonly Action _select;

        public SidebarItemHost(Border container, SidebarItem item, Action select)
        {
            _item = item;
            _select = select;
            Content = container;
            IsTabStop = true;
            UseSystemFocusVisuals = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
        }

        internal string DisplayName => _item.DisplayName;

        internal void Select() => _select();

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            // Keys from a focused child, such as the favorite button, belong to that child.
            if (ReferenceEquals(e.OriginalSource, this) && e.Key is VirtualKey.Enter or VirtualKey.Space)
            {
                e.Handled = true;
                _select();
                return;
            }

            base.OnKeyDown(e);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SidebarItemHostAutomationPeer(this);
    }

    internal sealed partial class SidebarItemHostAutomationPeer(SidebarItemHost owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        // Uno declares the base result as nullable; WinUI does not.
        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface)!;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetClassNameCore() => nameof(SidebarItemHost);

        protected override string GetNameCore() => owner.DisplayName;

        public void Invoke() => owner.Select();
    }
}
