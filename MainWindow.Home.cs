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
    private void ShowHome()
    {
        EnterView("home");
        SetTitle("Главная", "Всё под рукой — проекты, заметки, задачи");
        var root = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        left.Children.Add(HomeHeroCard());
        left.Children.Add(HomeQuickActions());
        left.Children.Add(Text(WorkModeService.DescribeEffects(_settings.WorkMode), 12, (WpfBrush)FindResource("MutedBrush"), new Thickness(8, 0, 8, 8)));
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var activeProjects = _projects.Projects.Where(p => !p.Status.Equals("Archive", StringComparison.OrdinalIgnoreCase)).ToList();
        var openTasks = _tasks.Tasks.Count(t => !t.IsDone);
        var openNotes = _notes.Notes.Count(n => n.WorkspaceId is null);

        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(HomeRecentCard());
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        grid.Children.Add(HomeMiniCard("Избранное", $"{PinnedCount()} закреплено", ShowFavorites));
        grid.Children.Add(HomeMiniCard("Подключения", $"{_connections.Connections.Count} записей", ShowConnections));
        grid.Children.Add(HomeMiniCard("Заметки", $"{openNotes} заметок", ShowNotes));
        grid.Children.Add(HomeMiniCard("Проекты", $"{activeProjects.Count} активных", ShowProjects));
        grid.Children.Add(HomeMiniCard("Буфер", $"{_clipboardHistory.Items.Count} элементов", ShowClipboardHistory));
        grid.Children.Add(HomeMiniCard("Пульс", $"CPU {_pulseSnapshot.CpuPercent:0}% · RAM {_pulseSnapshot.MemoryPercent:0}%", ShowPulse));
        right.Children.Add(grid);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        ContentHost.Content = root;
    }
    private Border HomeHeroCard()
    {
        var card = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = (WpfBrush)FindResource("AccentBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = new Thickness(6),
            MinHeight = 190,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var stack = new StackPanel();
        stack.Children.Add(Text("Сейчас", 13, (WpfBrush)FindResource("AccentBrush"), new Thickness(0, 0, 0, 8), FontWeights.SemiBold));
        stack.Children.Add(Text(NowDashboardText(), 15, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 0, 0, 14)));
        var nextTask = _tasks.Tasks.Where(t => !t.IsDone).OrderBy(t => t.StartAt).FirstOrDefault();
        var actions = new WrapPanel();
        if (nextTask is not null)
        {
            if (IsTaskRunning(nextTask))
            {
                actions.Children.Add(ActionButton("Пауза", () => PauseTask(nextTask), false));
            }
            else if (IsTaskPaused(nextTask))
            {
                actions.Children.Add(ActionButton("Продолжить", () => StartTask(nextTask, openEditor: false)));
            }
            else
            {
                actions.Children.Add(ActionButton("Начать", () => StartTask(nextTask)));
            }
        }
        actions.Children.Add(ActionButton("Задачи", ShowTasks, false));
        stack.Children.Add(actions);
        card.Child = stack;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject)) ShowTasks();
        };
        return card;
    }
    private Border HomeQuickActions()
    {
        var card = Card("Действия");
        card.Width = double.NaN;
        card.Margin = new Thickness(8);
        var wrap = new WrapPanel();
        foreach (var action in WorkModeService.HomeQuickActions(_settings.WorkMode))
        {
            wrap.Children.Add(ActionButton(action.Label, () => RunHomeQuickAction(action.Key), action.Key is "note" or "task" or "backup"));
        }
        card.Child = WithTitle($"Быстрые действия · {WorkModeService.DisplayName(_settings.WorkMode)}", wrap);
        return card;
    }
    private Border HomeMiniCard(string title, string text, Action action)
    {
        var card = Card(title);
        card.Width = double.NaN;
        card.MinHeight = 130;
        card.Margin = new Thickness(8);
        card.Child = WithTitle(title, Text(text, 13, (WpfBrush)FindResource("MutedBrush"), new Thickness()));
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                action();
            }
        };
        return card;
    }
    private void ShowFavorites()
    {
        EnterView("favorites");
        _viewScope = "favorites";
        SetTitle("Избранное", "Закрепленные проекты, заметки, подключения, задачи и ссылки");
        var panel = new StackPanel();
        foreach (var project in _projects.Projects.Where(p => p.IsPinned).OrderByDescending(p => p.CreatedAt))
            panel.Children.Add(FavoriteUnifiedRow("Проект", project.Name, UiHelpers.ProjectStatusDisplay(project.Status), () => ShowProjectDetail(project)));
        foreach (var note in _notes.Notes.Where(n => n.IsPinned).OrderByDescending(n => n.CreatedAt))
            panel.Children.Add(FavoriteUnifiedRow("Заметка", note.Title, note.Category, () => ViewNote(note)));
        foreach (var connection in _connections.Connections.Where(c => c.IsPinned).OrderByDescending(c => c.CreatedAt))
            panel.Children.Add(FavoriteUnifiedRow("Подключение", connection.Name, connection.Address, () => Connect(connection)));
        foreach (var task in _tasks.Tasks.Where(t => t.IsPinned && !t.IsDone).OrderByDescending(t => t.CreatedAt))
            panel.Children.Add(FavoriteUnifiedRow("Задача", task.Title, TaskStatusText(task), () => EditTask(task)));
        foreach (var agent in _aiAgents.Agents.Where(a => a.IsPinned).OrderByDescending(a => a.CreatedAt))
            panel.Children.Add(FavoriteUnifiedRow("Ссылка", agent.Name, agent.Url, () => OpenUrl(agent.Url, agent.Name)));
        if (panel.Children.Count == 0)
            panel.Children.Add(UiHelpers.EmptyState("Избранное пусто", "Нажмите ★ на карточке.", "На главную", ShowHome));
        ContentHost.Content = panel;
    }
    private Border HomeRecentCard()
    {
        var card = Card("Недавнее");
        card.Width = double.NaN;
        card.Margin = new Thickness(8);
        var stack = new StackPanel();
        var items = BuildRecentItems().Take(5).ToList();
        if (items.Count == 0)
        {
            stack.Children.Add(Muted("Пока ничего не открывали."));
        }
        else
        {
            foreach (var (label, action) in items)
            {
                var row = ListRow(label, action);
                row.Margin = new Thickness(0, 2, 0, 2);
                stack.Children.Add(row);
            }
        }

        card.Child = WithTitle("Недавнее", stack);
        return card;
    }
    private IEnumerable<(string Label, Action Action)> BuildRecentItems()
    {
        var items = new List<(DateTime At, string Label, Action Action)>();

        foreach (var project in _projects.Projects.Where(p => p.LastOpenedAt.HasValue))
        {
            items.Add((project.LastOpenedAt!.Value, $"Проект: {project.Name}", () => ShowProjectDetail(project)));
        }

        foreach (var note in _notes.Notes.OrderByDescending(n => n.UpdatedAt).Take(8))
        {
            items.Add((note.UpdatedAt, $"Заметка: {note.Title}", () => ViewNote(note)));
        }

        foreach (var task in _tasks.Tasks.Where(t => !t.IsDone).OrderByDescending(t => t.StartAt).Take(8))
        {
            items.Add((task.StartAt, $"Задача: {task.Title}", () => EditTask(task)));
        }

        return items.OrderByDescending(x => x.At).Select(x => (x.Label, x.Action));
    }
    private void OpenUrl(string url, string name)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            AddLog("OK", $"Открыт AI агент: {name}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"AI агент: {ex.Message}");
        }
    }
}
