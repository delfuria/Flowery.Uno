using Flowery.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flowery.Controls
{
    /// <summary>
    /// Displays WinForms-style mnemonic labels: <c>&amp;</c> marks the access character (underlined),
    /// <c>&amp;&amp;</c> is a literal <c>&amp;</c>. Display-only; applications must handle Alt+key themselves.
    /// </summary>
    /// <remarks>
    /// WinUI's <see cref="TextBlock"/> is sealed, so this control hosts a <see cref="TextBlock"/> instead of
    /// deriving from it. The inner text block inherits <c>Foreground</c> and the font properties.
    /// </remarks>
    public partial class DaisyMnemonicText : ContentControl
    {
        private readonly TextBlock _textBlock;
        private char? _mnemonicChar;

        /// <summary>
        /// Identifies the <see cref="Text"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(DaisyMnemonicText),
                new PropertyMetadata(null, OnMnemonicPropertyChanged));

        /// <summary>
        /// Gets or sets the label text, including <c>&amp;</c> markers.
        /// </summary>
        public string? Text
        {
            get => (string?)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="ShowMnemonic"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ShowMnemonicProperty =
            DependencyProperty.Register(
                nameof(ShowMnemonic),
                typeof(bool),
                typeof(DaisyMnemonicText),
                new PropertyMetadata(true, OnMnemonicPropertyChanged));

        /// <summary>
        /// Gets or sets whether the mnemonic character is underlined.
        /// </summary>
        public bool ShowMnemonic
        {
            get => (bool)GetValue(ShowMnemonicProperty);
            set => SetValue(ShowMnemonicProperty, value);
        }

        /// <summary>
        /// Gets the mnemonic character parsed from <see cref="Text"/>, or null when none.
        /// </summary>
        public char? MnemonicChar => _mnemonicChar;

        /// <summary>
        /// Initializes a new instance of the <see cref="DaisyMnemonicText"/> class.
        /// </summary>
        public DaisyMnemonicText()
        {
            IsTabStop = false;
            _textBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Content = _textBlock;
        }

        private static void OnMnemonicPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DaisyMnemonicText mnemonicText)
            {
                mnemonicText.ApplyMnemonicPresentation();
            }
        }

        private void ApplyMnemonicPresentation()
        {
            _mnemonicChar = FloweryMnemonicHelpers.ApplyMnemonicText(_textBlock, Text, ShowMnemonic).MnemonicChar;
        }
    }
}
