using System.Windows;

namespace DevCockpit;

public partial class TaskReminderWindow : Window
{
    public TimeSpan? Snooze { get; private set; }
    public bool StartRequested { get; private set; }

    public TaskReminderWindow(TaskItem task)
    {
        InitializeComponent();
        TitleText.Text = task.Title;
        DescriptionText.Text = task.Description;
        SnoozeList.Items.Add("15 минут");
        SnoozeList.Items.Add("На час");
        SnoozeList.Items.Add("На день");
        SnoozeList.Items.Add("На неделю");
        SnoozeList.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 18;
            Top = area.Bottom - Height - 18;
        };
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        StartRequested = true;
        Close();
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        if (SnoozeList.Visibility != Visibility.Visible)
        {
            SnoozeList.Visibility = Visibility.Visible;
            Height = 360;
            return;
        }

        Snooze = SnoozeList.SelectedIndex switch
        {
            1 => TimeSpan.FromHours(1),
            2 => TimeSpan.FromDays(1),
            3 => TimeSpan.FromDays(7),
            _ => TimeSpan.FromMinutes(15)
        };
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
