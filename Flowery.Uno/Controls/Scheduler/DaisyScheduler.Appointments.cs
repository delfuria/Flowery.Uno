// Uno Platform adaptation of Xtreme.Components Scheduler (MIT).
// See ThirdPartyNotices/Xtreme.Components.txt for attribution and license.
using System.Runtime.InteropServices.WindowsRuntime;
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Flowery.Controls;

public partial class DaisyScheduler
{
    private Border CreateAppointment(AppointmentModel appointment, AppointmentSurface surface, Lane? lane = null,
        AppointmentModel? displayed = null, object? resourceId = null)
    {
        var shown = displayed ?? appointment;
        if (_occurrenceSources.TryGetValue(appointment, out var source))
            appointment = source;
        var background = shown.BackColor is { } back ? new SolidColorBrush(back) : Brush("DaisySchedulerEventBackgroundBrush");
        var bar = shown.BarColor is { } accent ? new SolidColorBrush(accent) : Brush("DaisyPrimaryBrush");
        var foreground = shown.BackColor is { } color ? Contrast(color) : Brush("DaisyBaseContentBrush");
        var context = new AppointmentContext(shown, surface, background, bar, foreground,
            Brush("DaisyBase300Brush"), EventCornerRadius, ShowTiles, Culture, TimePattern);
        var template = surface switch
        {
            AppointmentSurface.Chip => AppointmentChipTemplate,
            AppointmentSurface.Bar => AppointmentBarTemplate,
            _ => AppointmentTemplate
        };
        var content = AppointmentRenderer?.Invoke(context) ?? template?.LoadContent() as FrameworkElement ?? BuildAppointmentContent(context);
        content.DataContext = context;
        // The selection ring lies over the frame border and takes no layout space.
        var selection = new Border
        {
            BorderThickness = new Thickness(SelectionThickness), CornerRadius = new CornerRadius(EventCornerRadius),
            Margin = new Thickness(-LineThickness), IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        var layers = new Grid();
        layers.Children.Add(content);
        layers.Children.Add(selection);
        var frame = new Border
        {
            Background = background,
            BorderBrush = context.BorderBrush,
            BorderThickness = new Thickness(LineThickness),
            CornerRadius = new CornerRadius(EventCornerRadius),
            Child = layers,
            MinHeight = ChipHeight,
            Tag = new AppointmentTarget(appointment, lane, shown, lane?.Resource?.Id ?? resourceId),
            DataContext = context,
            IsTabStop = true
        };
        AutomationProperties.SetName(frame, appointment.Subject + ", " + context.TimeRange);
        frame.PointerPressed += OnAppointmentPressed;
        frame.KeyDown += OnAppointmentKeyDown;
        var tooltip = AppointmentTooltipRenderer?.Invoke(context) ?? AppointmentTooltipTemplate?.LoadContent() as FrameworkElement ?? BuildTooltip(context);
        tooltip.DataContext = context;
        SetSchedulerTooltip(frame, tooltip);
        _frames.Add((frame, selection, appointment));
        return frame;
    }

    private FrameworkElement BuildAppointmentContent(AppointmentContext context)
    {
        var main = WithDefinitions(new Grid { Margin = new Thickness(SpacingXS + LineThickness, SpacingXS / 2, SpacingXS + LineThickness, SpacingXS / 2) }, "Auto,Auto,*,Auto", "Auto,*,Auto");
        var multiline = context.Surface == AppointmentSurface.Timed;
        // Timed blocks stack their rows in a text panel; single-line chips use a line panel instead.
        var text = multiline ? ClipToBounds(new AppointmentTextPanel(SpacingXS / 2)) : null;
        var title = Label(context.Subject);
        title.Foreground = context.Foreground;
        title.FontWeight = FontWeights.SemiBold;
        if (CurrentView == SchedulerView.Calendar)
        {
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
        }
        if (text is not null)
        {
            text.Add(title, 0);
            var time = Label(context.TimeRange, true);
            time.Foreground = context.Foreground;
            text.Add(time, 1);
            if (!string.IsNullOrWhiteSpace(context.Note))
            {
                var note = Label(context.Note, true);
                note.Foreground = context.Foreground;
                if (CurrentView == SchedulerView.Calendar)
                {
                    note.TextWrapping = TextWrapping.Wrap;
                    note.TextTrimming = TextTrimming.None;
                }
                text.Add(note, 4);
            }
            Grid.SetColumn(text, 2);
            Grid.SetRow(text, 1);
            main.Children.Add(text);
        }

        var status = new StackPanel { Spacing = SpacingXS / 2, Orientation = multiline ? Orientation.Vertical : Orientation.Horizontal };
        if (multiline)
            status.Margin = new Thickness(0, 0, SpacingXS, 0);
        var flags = AppointmentFlags(context.Appointment);
        if (flags.Length > 0)
        {
            var label = Label(flags, true);
            label.Foreground = context.Foreground;
            status.Children.Add(label);
        }
        foreach (var indicator in context.Indicators)
        {
            var glyph = Label(indicator.Glyph, true);
            if (indicator.FontFamily is { } family)
                glyph.FontFamily = family;
            glyph.Foreground = indicator.Color is { } color ? new SolidColorBrush(color) : context.Foreground;
            SetSchedulerTooltip(glyph, indicator.Tooltip);
            status.Children.Add(glyph);
        }
        if (multiline)
        {
            Grid.SetRow(status, 1);
            main.Children.Add(status);
        }

        StackPanel? leftTiles = null;
        StackPanel? rightTiles = null;
        if (ShowTiles)
        {
            foreach (var side in Enum.GetValues<TileSide>())
            {
                var strip = new StackPanel { Spacing = 3, Orientation = TileLayout.IsVertical(side, multiline) ? Orientation.Vertical : Orientation.Horizontal };
                foreach (var (alignment, tiles) in TileLayout.Groups(context.Tiles, side, multiline))
                {
                    var group = new StackPanel { Spacing = 2, Orientation = strip.Orientation };
                    group.HorizontalAlignment = alignment switch { DecorationAlignment.Middle => HorizontalAlignment.Center, DecorationAlignment.Far => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
                    group.VerticalAlignment = alignment switch { DecorationAlignment.Middle => VerticalAlignment.Center, DecorationAlignment.Far => VerticalAlignment.Bottom, _ => VerticalAlignment.Top };
                    foreach (var tile in tiles)
                        group.Children.Add(CreateDecoration(tile, context, true));
                    strip.Children.Add(group);
                }
                if (text is not null && side is TileSide.Top or TileSide.Bottom)
                {
                    text.Add(strip, side == TileSide.Top ? 2 : 3, first: side == TileSide.Top);
                    if (side == TileSide.Bottom)
                        text.BottomRow = strip;
                }
                else if (!multiline && side is TileSide.Left or TileSide.Right)
                {
                    if (side == TileSide.Left)
                        leftTiles = strip;
                    else
                        rightTiles = strip;
                }
                else
                {
                    Grid.SetColumn(strip, side switch { TileSide.Left => 1, TileSide.Right => 3, _ => 2 });
                    Grid.SetRow(strip, side switch { TileSide.Top => 0, TileSide.Bottom => 2, _ => 1 });
                    main.Children.Add(strip);
                }
            }
        }
        if (!multiline)
        {
            // The start time is never trimmed; status, tiles and then the subject give way to it. It uses the
            // subject's font size.
            var line = new AppointmentLinePanel(SpacingXS) { VerticalAlignment = VerticalAlignment.Center };
            if (status.Children.Count > 0)
                line.Add(status, AppointmentLinePanel.Optional);
            if (leftTiles is { Children.Count: > 0 })
                line.Add(leftTiles, AppointmentLinePanel.Optional);
            if (!context.IsAllDay)
            {
                var start = Label(context.StartTime);
                start.Foreground = context.Foreground;
                start.FontWeight = FontWeights.SemiBold;
                start.TextTrimming = TextTrimming.None;
                line.Add(start, AppointmentLinePanel.Required);
            }
            // A subject is worth showing from its first two characters and an ellipsis on.
            var subject = context.Subject ?? string.Empty;
            var shortest = Label(subject.Length > 2 ? subject[..2] + "…" : subject);
            shortest.FontWeight = FontWeights.SemiBold;
            shortest.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            line.Add(title, AppointmentLinePanel.Fill, shortest.DesiredSize.Width);
            if (rightTiles is { Children.Count: > 0 })
                line.Add(rightTiles, AppointmentLinePanel.Optional);
            Grid.SetRow(line, 1);
            Grid.SetColumnSpan(line, 4);
            main.Children.Add(line);
        }
        var root = ClipToBounds(new Grid());
        root.Children.Add(new Border { BorderBrush = context.BarBrush, BorderThickness = new Thickness(SpacingXS, 0, 0, 0), Child = main });
        foreach (var adornment in context.Adornments)
        {
            var decoration = CreateDecoration(adornment, context, false);
            decoration.HorizontalAlignment = adornment.Position switch
            {
                DecorationPosition.Left or DecorationPosition.FloatLeft => HorizontalAlignment.Left,
                DecorationPosition.Right or DecorationPosition.FloatRight => HorizontalAlignment.Right,
                _ => adornment.Alignment switch { DecorationAlignment.Near => HorizontalAlignment.Left, DecorationAlignment.Far => HorizontalAlignment.Right, _ => HorizontalAlignment.Center }
            };
            decoration.VerticalAlignment = adornment.Position switch
            {
                DecorationPosition.Bottom => VerticalAlignment.Bottom,
                DecorationPosition.Top or DecorationPosition.FloatLeft or DecorationPosition.FloatRight => VerticalAlignment.Top,
                _ => adornment.Alignment switch { DecorationAlignment.Near => VerticalAlignment.Top, DecorationAlignment.Far => VerticalAlignment.Bottom, _ => VerticalAlignment.Center }
            };
            root.Children.Add(decoration);
        }
        return root;
    }

    private static string AppointmentFlags(AppointmentModel appointment)
    {
        var flags = (appointment.IsLocked ? "🔒" : "") + (appointment.IsComplete ? "✓" : "") +
            (appointment.IsCancelled ? "×" : "") + (appointment.IsTentative ? "?" : "") +
            (appointment.IsConfirmed ? "✓" : "") + (appointment.IsRecurring ? "↻" : "");
        foreach (var indicator in appointment.Indicators)
            if (!string.IsNullOrEmpty(indicator.Glyph))
                flags = flags.Replace(indicator.Glyph, string.Empty, StringComparison.Ordinal);
        return flags;
    }

    private Border CreateDecoration(AppointmentDecoration decoration, AppointmentContext context, bool tile)
    {
        var background = decoration.BackColor is { } back ? new SolidColorBrush(back) : tile ? Brush("DaisyBase300Brush") : Transparent;
        var foreground = context.Foreground;
        if (background is SolidColorBrush { Color.A: > 0 } fill)
        {
            var color = fill.Color;
            if (color.A < 255 && context.BackBrush is SolidColorBrush backdrop)
            {
                var opacity = color.A / 255d;
                color = Color.FromArgb(255,
                    (byte)Math.Round(color.R * opacity + backdrop.Color.R * (1 - opacity)),
                    (byte)Math.Round(color.G * opacity + backdrop.Color.G * (1 - opacity)),
                    (byte)Math.Round(color.B * opacity + backdrop.Color.B * (1 - opacity)));
            }
            foreground = Contrast(color);
        }
        FrameworkElement content;
        if (decoration.Image is { Length: > 0 } bytes)
            content = CreateOwnedImage(bytes, ChipHeight - 6);
        else
        {
            var label = Label(string.Join(" ", new[] { decoration.Glyph, decoration.Text }).Trim(), true);
            if (decoration.FontSize is { } fontSize)
                SetFontSize(label, fontSize);
            label.Foreground = decoration.ForeColor is { } color ? new SolidColorBrush(color) : foreground;
            label.FontWeight = decoration.Bold ? FontWeights.Bold : FontWeights.Normal;
            if (decoration.GlyphFontFamily is { } family)
                label.FontFamily = family;
            content = label;
        }
        var border = new Border
        {
            Child = content,
            Padding = new Thickness(SpacingXS - LineThickness, LineThickness, SpacingXS - LineThickness, LineThickness),
            CornerRadius = new CornerRadius(EventCornerRadius),
            Background = background,
            BorderBrush = decoration.BorderColor is { } outline ? new SolidColorBrush(outline) : Transparent,
            BorderThickness = new Thickness(decoration.BorderColor is null ? 0 : LineThickness),
            RenderTransform = new TranslateTransform { X = decoration.OffsetX, Y = decoration.OffsetY }
        };
        SetSchedulerTooltip(border, decoration.Tooltip);
        return border;
    }

    private FrameworkElement BuildTooltip(AppointmentContext context)
    {
        var stack = new StackPanel { Spacing = SpacingXS + SpacingXS / 2, MaxWidth = Token<double>("DaisySchedulerTooltipMaximumWidth") };
        var title = TooltipLabel(context.Subject);
        title.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(title);
        stack.Children.Add(TooltipLabel(context.Start.ToString("d", Culture) + "  " + context.TimeRange, true));
        foreach (var detail in context.Details)
            stack.Children.Add(TooltipLabel(detail.Label + ": " + detail.Value, true));
        if (context.Note is { Length: > 0 } note)
        {
            var label = TooltipLabel(note);
            label.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(label);
        }
        if (context.Memo is { Length: > 0 } memo)
            stack.Children.Add(TooltipLabel(memo, true));
        return stack;
    }

    private TextBlock TooltipLabel(string text, bool secondary = false)
    {
        var label = Label(text, secondary);
        // Inherit the scheduler tooltip palette, including for custom appointment fills.
        label.ClearValue(TextBlock.ForegroundProperty);
        return label;
    }

    private void SetSchedulerTooltip(FrameworkElement target, object? content)
    {
        if (content is null)
            return;
        var tooltip = content as ToolTip ?? new ToolTip { Content = content };
        tooltip.Style = SchedulerStyle("DaisySchedulerToolTipStyle");
        tooltip.Padding = Token<Thickness>("DaisySchedulerTooltipPadding");
        tooltip.FontFamily = FontFamily;
        tooltip.FontSize = Token<double>($"DaisySize{SizeKey}SecondaryFontSize");
        tooltip.MaxWidth = Token<double>("DaisySchedulerTooltipMaximumWidth");
        ToolTipService.SetToolTip(target, tooltip);
    }

    internal Style? SchedulerStyle(string key) => TryFindResource(key, out var resource) ? resource as Style : null;

    // A flyout presenter only takes a style; derive one that applies the padding token.
    private Style WithTooltipPadding(Style? style, Type targetType) => new(targetType)
    {
        BasedOn = style,
        Setters = { new Setter(Control.PaddingProperty, Token<Thickness>("DaisySchedulerTooltipPadding")) }
    };

    private static SolidColorBrush Contrast(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return new SolidColorBrush(luminance > 0.179 ? Colors.Black : Colors.White);
    }

    // WinUI's Image is sealed: decode the bytes while the image is loaded and release the bitmap afterwards.
    internal static Image CreateOwnedImage(byte[] bytes, double size)
    {
        var image = new Image { Width = size, Height = size, Tag = bytes };
        image.Loaded += OnOwnedImageLoaded;
        image.Unloaded += OnOwnedImageUnloaded;
        return image;
    }

    private static async void OnOwnedImageLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Image { Tag: byte[] bytes } image)
            return;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (image.IsLoaded)
                image.Source = bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or COMException or NotSupportedException)
        {
            // Undecodable host bytes leave the image empty instead of failing the view.
            image.Source = null;
        }
    }

    private static void OnOwnedImageUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Image image)
            image.Source = null;
    }
}
