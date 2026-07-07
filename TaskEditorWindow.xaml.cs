using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace DevCockpit;

public partial class TaskEditorWindow : Window
{
    private readonly List<ProjectProfile> _projects;
    private string _importance = "Green";
    public bool Saved { get; private set; }
    public TaskItem Task { get; private set; }

    public TaskEditorWindow(IEnumerable<ProjectProfile> projects, TaskItem? source = null)
    {
        InitializeComponent();
        _projects = projects.ToList();
        Task = source is null
            ? new TaskItem()
            : new TaskItem
            {
                Id = source.Id,
                Title = source.Title,
                Description = source.Description,
                Status = source.Status,
                StartAt = source.StartAt,
                EndAt = source.EndAt,
                Importance = source.Importance,
                WorkspaceId = source.WorkspaceId,
                IsDone = source.IsDone,
                IsPinned = source.IsPinned,
                ReminderAt = source.ReminderAt,
                LastNotifiedAt = source.LastNotifiedAt,
                Recurrence = source.Recurrence,
                ContactName = source.ContactName,
                ContactPhone = source.ContactPhone,
                TelegramKey = source.TelegramKey,
                TelegramExternalId = source.TelegramExternalId,
                CreatedAt = source.CreatedAt,
                WorkStartedAt = source.WorkStartedAt
            };

        ProjectBox.Items.Add("(без проекта)");
        foreach (var project in _projects) ProjectBox.Items.Add(project);
        ProjectBox.SelectedItem = _projects.FirstOrDefault(p => p.Id == Task.WorkspaceId) ?? ProjectBox.Items[0];

        RecurrenceBox.Items.Add("None");
        RecurrenceBox.Items.Add("Daily");
        RecurrenceBox.Items.Add("Weekly");
        RecurrenceBox.SelectedItem = string.IsNullOrWhiteSpace(Task.Recurrence) ? "None" : Task.Recurrence;

        TitleBox.Text = Task.Title;
        DescriptionBox.Text = Task.Description;
        ContactNameBox.Text = Task.ContactName;
        ContactPhoneBox.Text = Task.ContactPhone;
        InitDateTimeControls(Task.StartAt, StartDatePicker, StartHourBox, StartMinuteBox);

        SelectImportance(Task.Importance);
        EditorWindowHelper.HookConfirmClose(this, () => !Saved && IsDirty(), () => TrySave(showValidationErrors: true));
    }

    private bool IsDirty()
    {
        if (!string.Equals(TitleBox.Text.Trim(), Task.Title, StringComparison.Ordinal)) return true;
        if (!string.Equals(DescriptionBox.Text.Trim(), Task.Description, StringComparison.Ordinal)) return true;
        if (!string.Equals(ContactNameBox.Text.Trim(), Task.ContactName, StringComparison.Ordinal)) return true;
        if (!string.Equals(ContactPhoneBox.Text.Trim(), Task.ContactPhone, StringComparison.Ordinal)) return true;
        if (_importance != Task.Importance) return true;
        if ((RecurrenceBox.SelectedItem?.ToString() ?? "None") != (string.IsNullOrWhiteSpace(Task.Recurrence) ? "None" : Task.Recurrence)) return true;
        var projectId = ProjectBox.SelectedItem is ProjectProfile project ? project.Id : (Guid?)null;
        if (projectId != Task.WorkspaceId) return true;
        if (!TryReadDateTime(StartDatePicker, StartHourBox, StartMinuteBox, out var startAt)) return true;
        return startAt != Task.StartAt;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TrySave(showValidationErrors: true)) Close();
    }

    private bool TrySave(bool showValidationErrors)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            if (showValidationErrors) System.Windows.MessageBox.Show(this, "Укажите название задачи.", "WideS");
            return false;
        }

        if (!TryReadDateTime(StartDatePicker, StartHourBox, StartMinuteBox, out var startAt))
        {
            if (showValidationErrors) System.Windows.MessageBox.Show(this, "Укажите дату и время.", "WideS");
            return false;
        }

        Task.Title = TitleBox.Text.Trim();
        Task.Description = DescriptionBox.Text.Trim();
        Task.ContactName = ContactNameBox.Text.Trim();
        Task.ContactPhone = ContactPhoneBox.Text.Trim();
        Task.StartAt = startAt;
        Task.EndAt = startAt.AddHours(1);
        Task.Importance = _importance;
        Task.Recurrence = RecurrenceBox.SelectedItem?.ToString() ?? "None";
        Task.WorkspaceId = ProjectBox.SelectedItem is ProjectProfile project ? project.Id : null;
        if (!Task.Status.Equals("Выполняется", StringComparison.OrdinalIgnoreCase))
        {
            Task.ReminderAt = startAt;
            Task.LastNotifiedAt = null;
        }
        Saved = true;
        return true;
    }

    private void GreenImportance_Click(object sender, RoutedEventArgs e) => SelectImportance("Green");

    private void YellowImportance_Click(object sender, RoutedEventArgs e) => SelectImportance("Yellow");

    private void RedImportance_Click(object sender, RoutedEventArgs e) => SelectImportance("Red");

    private void SelectImportance(string? importance)
    {
        _importance = importance is "Yellow" or "Red" ? importance : "Green";
        MarkImportance(GreenImportance, _importance == "Green");
        MarkImportance(YellowImportance, _importance == "Yellow");
        MarkImportance(RedImportance, _importance == "Red");
    }

    private void MarkImportance(WpfButton button, bool selected)
    {
        button.Opacity = selected ? 1.0 : 0.7;
        button.BorderBrush = selected ? (WpfBrush)FindResource("TextBrush") : WpfBrushes.Transparent;
        button.BorderThickness = selected ? new Thickness(3) : new Thickness(2);
    }

    private static void InitDateTimeControls(DateTime value, System.Windows.Controls.DatePicker datePicker, System.Windows.Controls.ComboBox hourBox, System.Windows.Controls.ComboBox minuteBox)
    {
        if (hourBox.Items.Count == 0)
        {
            for (var hour = 0; hour < 24; hour++) hourBox.Items.Add(hour.ToString("00"));
            for (var minute = 0; minute < 60; minute += 5) minuteBox.Items.Add(minute.ToString("00"));
        }

        datePicker.SelectedDate = value.Date;
        hourBox.SelectedItem = value.Hour.ToString("00");
        var roundedMinute = (value.Minute / 5) * 5;
        minuteBox.SelectedItem = roundedMinute.ToString("00");
    }

    private static bool TryReadDateTime(System.Windows.Controls.DatePicker datePicker, System.Windows.Controls.ComboBox hourBox, System.Windows.Controls.ComboBox minuteBox, out DateTime value)
    {
        value = default;
        if (datePicker.SelectedDate is not DateTime date) return false;
        if (hourBox.SelectedItem is not string hourText || minuteBox.SelectedItem is not string minuteText) return false;
        if (!int.TryParse(hourText, out var hour) || !int.TryParse(minuteText, out var minute)) return false;

        value = date.Date.AddHours(hour).AddMinutes(minute);
        return true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => EditorWindowHelper.MinimizeWindow(this);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (EditorWindowHelper.ConfirmClose(this, !Saved && IsDirty(), () => TrySave(showValidationErrors: true)))
        {
            Close();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);

    private void Close_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);
}
