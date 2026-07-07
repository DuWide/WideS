using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;

namespace DevCockpit;

public partial class MainWindow
{
    private void ShowHistory()
    {
        EnterView("history");
        SetTitle("История", "Запуски, действия и рабочие события");
        var root = SectionWithActions(actions =>
        {
            actions.Children.Add(ActionButton("Собрать отчет", BuildDailyReport));
            actions.Children.Add(ActionButton("Очистить историю", () =>
            {
                _activity.Entries.Clear();
                _activityStore.Save(_activity);
                AddLog("WARN", "История очищена.");
                ShowHistory();
            }, false));
        }, out var panel);

        foreach (var entry in _activity.Entries.OrderByDescending(x => x.At).Take(80))
        {
            panel.Children.Add(CardText($"{entry.At:dd.MM HH:mm} · {entry.Status}", entry.Message));
        }
        if (panel.Children.Count == 0) panel.Children.Add(CardText("Пусто", "История пока пустая."));
        ContentHost.Content = root;
    }
    private void BuildDailyReport()
    {
        var project = _selectedProject ?? _focusProject ?? _projects.Projects.FirstOrDefault();
        if (project is null)
        {
            WpfMessageBox.Show(this, "Сначала добавьте или выберите проект.", "WideS");
            return;
        }

        var since = DateTime.Today;
        var day = AppPaths.EnsureTodayWorkDay(project);
        var reportPath = Path.Combine(day, "Context", $"daily-report-{DateTime.Now:yyyy-MM-dd-HH-mm}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        var lines = new List<string>
        {
            $"Отчет за день: {DateTime.Now:dd.MM.yyyy}",
            $"Проект: {project.Name}",
            $"Папка: {project.ProjectFolder}",
            "",
            "Рабочая область:",
            _focusProject?.Id == project.Id && _focusStartedAt is not null ? $"Начата: {_focusStartedAt:HH:mm}" : "Рабочая область не запущена.",
            "",
            "Задачи:",
        };
        lines.AddRange(_tasks.Tasks.Where(t => t.WorkspaceId == project.Id && t.StartAt >= since).Select(t => $"- {(t.IsDone ? "[x]" : "[ ]")} {t.Title} ({t.StartAt:HH:mm}-{t.EndAt:HH:mm})"));
        lines.Add("");
        lines.Add("Заметки:");
        lines.AddRange(_notes.Notes.Where(n => n.WorkspaceId == project.Id && n.UpdatedAt >= since).Select(n => $"- {n.Title}"));
        lines.Add("");
        lines.Add("История:");
        lines.AddRange(_activity.Entries.Where(a => a.At >= since && (a.WorkspaceId == project.Id || a.WorkspaceId is null)).TakeLast(80).Select(a => $"- {a.At:HH:mm} {a.Status}: {a.Message}"));

        File.WriteAllLines(reportPath, lines);
        WpfClipboard.SetText(reportPath);
        AddLog("OK", $"Отчет за день создан: {reportPath}");
    }
    private string NowDashboardText()
    {
        var active = _tasks.Tasks.Where(t => !t.IsDone).OrderBy(t => t.StartAt).Take(5).ToList();
        var focus = _focusProject is null ? "Рабочая область не запущена" : $"Рабочая область: {_focusProject.Name} с {_focusStartedAt:HH:mm}";
        return focus + "\n" + (active.Count == 0 ? "Нет активных задач." : string.Join("\n", active.Select(t => $"{t.StartAt:HH:mm} {t.Title}")));
    }
    private void ShowLogView()
    {
        SetTitle("Последние действия", "Расширенный просмотр журнала");
        var card = Card("Лог");
        card.Width = 980;
        card.MinHeight = 520;
        var stack = BaseCardStack("Лог действий");
        var box = new WpfTextBox
        {
            Text = LogBox.Text,
            IsReadOnly = true,
            MinHeight = 430,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(box);
        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("Скопировать лог", () => Copy(LogBox.Text, "Лог скопирован.")));
        buttons.Children.Add(ActionButton("Очистить лог", () => { LogBox.Clear(); ShowLogView(); }, false));
        stack.Children.Add(buttons);
        card.Child = stack;
        ContentHost.Content = card;
    }
}
