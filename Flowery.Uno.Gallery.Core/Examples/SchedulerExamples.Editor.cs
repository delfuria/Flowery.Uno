using Flowery.Controls.Scheduler.Models;
using Flowery.Uno.Gallery.Localization;

namespace Flowery.Uno.Gallery.Examples;

public partial class SchedulerExamples
{
    private sealed record TimeChoice(TimeSpan Time, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ColorChoice(Color? Color, string Label)
    {
        public Brush Fill => new SolidColorBrush(Color ?? Microsoft.UI.Colors.Transparent);
        public override string ToString() => Label;
    }

    // The editor fields as values, so a culture change that reformats a date does not count as an edit.
    private sealed record EditorFields(string Subject, DateTime? Start, TimeSpan? StartTime, DateTime? End, TimeSpan? EndTime,
        int SpanMode, string Description, bool? AllDay, int Resource, Color? Color);

    private CultureInfo _editorCulture = CultureInfo.InvariantCulture;
    private IReadOnlyList<AppointmentModel> _conflicts = Array.Empty<AppointmentModel>();
    private EditorFields? _openedFields;
    // WinUI routes no press to the page before the scheduler: a press that opened the editor reaches the dismiss handler
    // afterwards, so it is skipped until the dispatcher runs again.
    private bool _isOpeningEditor;

    private void OpenEditor(AppointmentModel appointment, bool isNew)
    {
        _editing = appointment;
        _isNew = isNew;
        SubjectInput.Text = appointment.Subject;
        _editorCulture = DemoScheduler.FormattingCulture;
        StartInput.Text = DemoScheduler.FormatDate(appointment.Start);
        EndInput.Text = DemoScheduler.FormatDate(appointment.End);
        StartTimeInput.ItemsSource = null;
        EndTimeInput.ItemsSource = null;
        ColorInput.ItemsSource = null;
        SpanModeInput.ItemsSource = null;
        DescriptionInput.Text = appointment.Description ?? string.Empty;
        AllDayInput.IsChecked = appointment.IsAllDay;
        ResourceInput.ItemsSource = new[] { Text("Unassigned") }.Concat(_resources.Select(static resource => resource.Name)).ToArray();
        ResourceInput.SelectedIndex = Array.FindIndex(_resources, resource => appointment.ResourceIds.Contains(resource.Id)) + 1;
        _editingResourceIndex = ResourceInput.SelectedIndex;
        EditorError.Text = string.Empty;
        _conflicts = Array.Empty<AppointmentModel>();
        RefreshEditorChoices();
        // The editor needs the height more than the description and the options.
        if (FoldsIntro)
            IsIntroVisible = false;
        EditorPanel.Visibility = Visibility.Visible;
        _openedFields = ReadEditorFields();
        _isOpeningEditor = true;
        if (DispatcherQueue?.TryEnqueue(EndOpeningEditor) != true)
            _isOpeningEditor = false;
    }

    private void EndOpeningEditor() => _isOpeningEditor = false;

    private EditorFields ReadEditorFields() => new(SubjectInput.Text ?? string.Empty, ParseEditorDate(StartInput.Text),
        (StartTimeInput.SelectedItem as TimeChoice)?.Time, ParseEditorDate(EndInput.Text), (EndTimeInput.SelectedItem as TimeChoice)?.Time,
        SpanModeInput.SelectedIndex, DescriptionInput.Text ?? string.Empty, AllDayInput.IsChecked, ResourceInput.SelectedIndex,
        (ColorInput.SelectedItem as ColorChoice)?.Color);

    private DateTime? ParseEditorDate(string? text) =>
        DateTime.TryParseExact(text, "d", _editorCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Light dismiss, which a press on the scheduler and Escape run: closes the editor while its fields are as they
    /// were opened. With changes it stays open until Save or Cancel.</summary>
    public void DismissEditorIfUnchanged()
    {
        if (EditorPanel.Visibility == Visibility.Visible && !_isOpeningEditor && ReadEditorFields() == _openedFields)
            EditorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnSchedulerPressed(object sender, PointerRoutedEventArgs e) => DismissEditorIfUnchanged();

    // The scheduler handles Escape itself, so this listens to handled keys too; an Escape the editor used, such as one
    // that closed an open select, is no dismiss.
    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && !(e.Handled && IsInEditor(e.OriginalSource as DependencyObject)))
            DismissEditorIfUnchanged();
    }

    private bool IsInEditor(DependencyObject? element)
    {
        for (; element != null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, EditorPanel))
                return true;
        }
        return false;
    }

    private void RefreshEditorChoices()
    {
        if (_editing is null)
            return;
        var startTime = (StartTimeInput.SelectedItem as TimeChoice)?.Time ?? _editing.Start.TimeOfDay;
        var endTime = (EndTimeInput.SelectedItem as TimeChoice)?.Time ?? _editing.End.TimeOfDay;
        var color = ColorInput.SelectedItem is ColorChoice selected ? selected.Color : _editing.BackColor;
        var mode = SpanModeInput.SelectedIndex < 0 ? (int)_editing.SpanMode : SpanModeInput.SelectedIndex;
        if (DateTime.TryParseExact(StartInput.Text, "d", _editorCulture, DateTimeStyles.None, out var startDate))
            StartInput.Text = DemoScheduler.FormatDate(startDate);
        if (DateTime.TryParseExact(EndInput.Text, "d", _editorCulture, DateTimeStyles.None, out var endDate))
            EndInput.Text = DemoScheduler.FormatDate(endDate);
        _editorCulture = DemoScheduler.FormattingCulture;
        StartInput.PlaceholderText = _editorCulture.DateTimeFormat.ShortDatePattern;
        EndInput.PlaceholderText = _editorCulture.DateTimeFormat.ShortDatePattern;
        FillTimeChoices(StartTimeInput, startTime);
        FillTimeChoices(EndTimeInput, endTime);
        SpanModeInput.ItemsSource = new[] { Text("Continuous"), Text("Daily") };
        SpanModeInput.SelectedIndex = mode;
        ColorPanel.Visibility = DemoScheduler.AppointmentColors is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        var colors = new List<ColorChoice> { new(null, Text("NoColor")) };
        foreach (var choice in DemoScheduler.AppointmentColors ?? Array.Empty<Color>())
            if (!colors.Any(item => item.Color == choice))
                colors.Add(new ColorChoice(choice, choice.ToString()));
        if (color is { } existing && !colors.Any(item => item.Color == existing))
            colors.Add(new ColorChoice(existing, existing.ToString()));
        ColorInput.ItemsSource = colors;
        ColorInput.SelectedIndex = colors.FindIndex(item => item.Color == color);
        ShowConflicts();
    }

    private void FillTimeChoices(DaisySelect select, TimeSpan selected)
    {
        var times = DemoScheduler.GetTimeSlots().Append(selected).Distinct().Order().ToArray();
        select.ItemsSource = times.Select(time => new TimeChoice(time, DemoScheduler.FormatTime(DateTime.Today + time))).ToArray();
        select.SelectedIndex = Array.IndexOf(times, selected);
        select.IsEnabled = AllDayInput.IsChecked != true;
    }

    private void OnAllDayChanged(object? sender, RoutedEventArgs e)
    {
        StartTimeInput.IsEnabled = AllDayInput.IsChecked != true;
        EndTimeInput.IsEnabled = AllDayInput.IsChecked != true;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => SaveEditor(false);
    private void OnSaveAnyway(object? sender, RoutedEventArgs e) => SaveEditor(true);

    private void SaveEditor(bool allowOverlap)
    {
        if (_editing is null || DemoScheduler.IsReadOnly)
            return;
        if (string.IsNullOrWhiteSpace(SubjectInput.Text) ||
            !DateTime.TryParseExact(StartInput.Text, "d", _editorCulture, DateTimeStyles.None, out var start) ||
            !DateTime.TryParseExact(EndInput.Text, "d", _editorCulture, DateTimeStyles.None, out var end) ||
            StartTimeInput.SelectedItem is not TimeChoice startTime || EndTimeInput.SelectedItem is not TimeChoice endTime)
        {
            EditorError.Text = Text("InvalidInput");
            return;
        }
        var candidate = _editing.Clone();
        candidate.Subject = SubjectInput.Text.Trim();
        candidate.IsAllDay = AllDayInput.IsChecked == true;
        candidate.SpanMode = (AppointmentSpanMode)Math.Max(0, SpanModeInput.SelectedIndex);
        candidate.Start = candidate.IsAllDay ? start : start + startTime.Time;
        candidate.End = candidate.IsAllDay ? (end > start ? end : start.AddDays(1)) : end + endTime.Time;
        if (candidate.End <= candidate.Start || candidate.SpanMode == AppointmentSpanMode.Daily &&
            !candidate.IsAllDay && endTime.Time <= startTime.Time)
        {
            EditorError.Text = Text("InvalidInput");
            return;
        }
        candidate.Description = DescriptionInput.Text;
        if (ColorPanel.Visibility == Visibility.Visible && ColorInput.SelectedItem is ColorChoice color)
            candidate.BackColor = color.Color;
        if (_isNew || ResourceInput.SelectedIndex != _editingResourceIndex)
        {
            candidate.ResourceIds.Clear();
            if (ResourceInput.SelectedIndex > 0)
                candidate.ResourceIds.Add(_resources[ResourceInput.SelectedIndex - 1].Id);
        }
        _conflicts = DemoScheduler.GetConflictingAppointments(candidate);
        ShowConflicts();
        if (!allowOverlap && _conflicts.Count > 0)
            return;
        if (_isNew)
            _appointments.Add(candidate);
        else
            _appointments[_appointments.IndexOf(_editing)] = candidate;
        EditorPanel.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void ShowConflicts()
    {
        OverlapWarning.Text = _conflicts.Count == 0 ? string.Empty : string.Format(CultureInfo.GetCultureInfo(GalleryLocalization.CurrentCultureName),
            Text("Overlap"), string.Join(", ", _conflicts.Select(static appointment => appointment.Subject)));
        SaveAnywayButton.Visibility = _conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
