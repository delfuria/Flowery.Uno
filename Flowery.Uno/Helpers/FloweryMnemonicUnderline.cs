using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Flowery.Helpers
{
    /// <summary>
    /// Draws the mnemonic underline of a <see cref="TextBlock"/> as a composition child visual.
    /// Uno's Skia text renderer does not draw <c>TextDecorations</c>, so the underlined run alone shows no line there.
    /// </summary>
    /// <remarks>
    /// The line covers the access character on the first text line. It is hidden when that character is not on the
    /// first line (wrapped or trimmed text) and for right-to-left text.
    /// </remarks>
    internal sealed class FloweryMnemonicUnderline
    {
        // Typical UI font metrics: baseline at 80 % of the line height, underline 0.06 em below it, 0.07 em thick.
        private const double BASELINE_RATIO = 0.8;
        private const double OFFSET_BELOW_BASELINE_EM = 0.06;
        private const double THICKNESS_EM = 0.07;

        private static readonly ConditionalWeakTable<TextBlock, FloweryMnemonicUnderline> s_underlines = new();

        private readonly TextBlock _textBlock;
        private readonly TextBlock _probe = new() { TextWrapping = TextWrapping.NoWrap };
        private readonly SpriteVisual _visual;
        private readonly CompositionColorBrush _brush;
        private SolidColorBrush? _observedBrush;
        private long _observedBrushToken;
        private string _displayText = string.Empty;
        private int _mnemonicIndex = -1;

        private FloweryMnemonicUnderline(TextBlock textBlock)
        {
            _textBlock = textBlock;

            var compositor = ElementCompositionPreview.GetElementVisual(textBlock).Compositor;
            _brush = compositor.CreateColorBrush(Colors.Transparent);
            _visual = compositor.CreateSpriteVisual();
            _visual.Brush = _brush;
            _visual.Size = Vector2.Zero;
            ElementCompositionPreview.SetElementChildVisual(textBlock, _visual);

            textBlock.SizeChanged += (_, _) => UpdatePlacement();
            textBlock.RegisterPropertyChangedCallback(TextBlock.ForegroundProperty, (_, _) => UpdateColor());
            foreach (var property in new[]
            {
                TextBlock.FontFamilyProperty,
                TextBlock.FontSizeProperty,
                TextBlock.FontWeightProperty,
                TextBlock.FontStyleProperty,
                TextBlock.FontStretchProperty,
                TextBlock.CharacterSpacingProperty,
                TextBlock.LineHeightProperty,
                TextBlock.LineStackingStrategyProperty,
                TextBlock.PaddingProperty,
                TextBlock.TextAlignmentProperty,
                FrameworkElement.FlowDirectionProperty
            })
            {
                textBlock.RegisterPropertyChangedCallback(property, (_, _) => UpdatePlacement());
            }

            UpdateColor();
        }

        /// <summary>
        /// Shows the underline below <paramref name="mnemonicIndex"/> of <paramref name="displayText"/>,
        /// or hides it when the index is negative.
        /// </summary>
        internal static void Update(TextBlock textBlock, string displayText, int mnemonicIndex)
        {
            if (mnemonicIndex < 0)
            {
                if (s_underlines.TryGetValue(textBlock, out var existing))
                {
                    existing.SetMnemonic(displayText, -1);
                }

                return;
            }

            s_underlines.GetValue(textBlock, block => new FloweryMnemonicUnderline(block)).SetMnemonic(displayText, mnemonicIndex);
        }

        private void SetMnemonic(string displayText, int mnemonicIndex)
        {
            _displayText = displayText;
            _mnemonicIndex = mnemonicIndex;
            UpdatePlacement();
        }

        private void UpdatePlacement()
        {
            if (_mnemonicIndex < 0 || _mnemonicIndex >= _displayText.Length
                || _textBlock.FlowDirection == FlowDirection.RightToLeft)
            {
                _visual.Size = Vector2.Zero;
                return;
            }

            var fullWidth = MeasureText(_displayText).Width;
            var throughMnemonic = MeasureText(_displayText[..(_mnemonicIndex + 1)]);
            var mnemonic = MeasureText(_displayText.Substring(_mnemonicIndex, 1));

            var padding = _textBlock.Padding;
            var contentWidth = _textBlock.ActualWidth - padding.Left - padding.Right;
            if (throughMnemonic.Width > contentWidth + 0.5)
            {
                _visual.Size = Vector2.Zero;
                return;
            }

            var alignmentOffset = _textBlock.TextAlignment switch
            {
                TextAlignment.Center => (contentWidth - fullWidth) / 2,
                TextAlignment.Right or TextAlignment.End => contentWidth - fullWidth,
                _ => 0d
            };

            var fontSize = _textBlock.FontSize;
            var thickness = Math.Max(1d, Math.Round(fontSize * THICKNESS_EM));
            var x = padding.Left + Math.Max(0d, alignmentOffset) + throughMnemonic.Width - mnemonic.Width;
            var y = padding.Top + mnemonic.Height * BASELINE_RATIO + fontSize * OFFSET_BELOW_BASELINE_EM;

            _visual.Offset = new Vector3((float)x, (float)y, 0f);
            _visual.Size = new Vector2((float)mnemonic.Width, (float)thickness);
        }

        private Size MeasureText(string text)
        {
            _probe.Text = text;
            _probe.FontFamily = _textBlock.FontFamily;
            _probe.FontSize = _textBlock.FontSize;
            _probe.FontWeight = _textBlock.FontWeight;
            _probe.FontStyle = _textBlock.FontStyle;
            _probe.FontStretch = _textBlock.FontStretch;
            _probe.CharacterSpacing = _textBlock.CharacterSpacing;
            _probe.LineHeight = _textBlock.LineHeight;
            _probe.LineStackingStrategy = _textBlock.LineStackingStrategy;
            _probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _probe.DesiredSize;
        }

        private void UpdateColor()
        {
            var foreground = _textBlock.Foreground;
            if (!ReferenceEquals(foreground, _observedBrush))
            {
                _observedBrush?.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _observedBrushToken);
                _observedBrush = foreground as SolidColorBrush;
                if (_observedBrush != null)
                {
                    _observedBrushToken = _observedBrush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => UpdateColor());
                }
            }

            _brush.Color = foreground switch
            {
                SolidColorBrush solid => WithOpacity(solid.Color, solid.Opacity),
                GradientBrush { GradientStops.Count: > 0 } gradient => WithOpacity(gradient.GradientStops[0].Color, gradient.Opacity),
                _ => Colors.Transparent
            };
        }

        private static Color WithOpacity(Color color, double opacity) =>
            Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0d, 1d)), color.R, color.G, color.B);
    }
}
