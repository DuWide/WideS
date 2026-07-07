using System.Diagnostics;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace DevCockpit;

public partial class MainWindow
{
    private readonly PortalService _portalService = new();

    private void InitializePortal()
    {
        if (!_settings.PortalEnabled)
        {
            return;
        }

        RestartPortal();
    }

    private void RestartPortal()
    {
        _portalService.Stop();
        if (!_settings.PortalEnabled)
        {
            return;
        }

        var port = _settings.PortalPort > 0 ? _settings.PortalPort : 7788;
        _portalService.Start(port, BuildPortalState, HandlePortalNote, HandlePortalTask);
        if (_portalService.IsRunning)
        {
            AddLog("OK", $"Портал: {string.Join(" | ", _portalService.BoundUrls)}");
        }
        else if (!string.IsNullOrWhiteSpace(_portalService.LastError))
        {
            AddLog("WARN", $"Портал не запущен: {_portalService.LastError}");
        }
    }

    private ProjectProfile? PortalProject() => _focusProject ?? _selectedProject;

    private string PrimaryPortalUrl()
    {
        if (_portalService.BoundUrls.Count > 0)
        {
            return _portalService.BoundUrls
                .FirstOrDefault(u => u.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                                     || u.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                ?? _portalService.BoundUrls[0];
        }

        var port = _settings.PortalPort > 0 ? _settings.PortalPort : 7788;
        return $"http://127.0.0.1:{port}/";
    }

    private void OpenPortalInBrowser()
    {
        if (!_portalService.IsRunning)
        {
            WpfMessageBox.Show(this,
                "Сначала включите портал в настройках и нажмите «Сохранить портал».",
                "WideS");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(PrimaryPortalUrl()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"Не удалось открыть браузер:\n{ex.Message}", "WideS");
        }
    }

    private PortalState BuildPortalState()
    {
        var project = PortalProject();

        return new PortalState
        {
            UserName = _settings.UserName,
            PortalUrl = PrimaryPortalUrl(),
            ActiveProject = project?.Name,
            ActiveSince = _focusStartedAt?.ToString("HH:mm"),
            HasActiveProject = project is not null,
            TasksInProgress = _tasks.Tasks
                .Where(t => IsTaskRunning(t))
                .Select(t => ToPortalTaskRow(t))
                .ToList(),
            TasksProject = project is null
                ? []
                : _tasks.Tasks
                    .Where(t => !t.IsDone && t.WorkspaceId == project.Id)
                    .OrderBy(t => t.StartAt)
                    .Take(20)
                    .Select(t => ToPortalTaskRow(t))
                    .ToList(),
            TasksCommon = _tasks.Tasks
                .Where(t => !t.IsDone && t.WorkspaceId is null)
                .OrderBy(t => t.StartAt)
                .Take(20)
                .Select(t => ToPortalTaskRow(t))
                .ToList(),
            Connections = project is null
                ? []
                : _connections.Connections
                    .Where(c => c.WorkspaceId == project.Id)
                    .OrderByDescending(c => c.IsPinned)
                    .ThenBy(c => c.Name)
                    .Select(c => new PortalConnectionRow
                    {
                        Name = c.Name,
                        Type = c.Type,
                        Address = c.Address
                    })
                    .ToList(),
            Notes = project is null
                ? []
                : _notes.Notes
                    .Where(n => n.WorkspaceId == project.Id)
                    .OrderByDescending(n => n.UpdatedAt)
                    .Take(12)
                    .Select(n => new PortalNoteRow
                    {
                        Title = n.Title,
                        Preview = Preview(n.Text, 120),
                        UpdatedAt = n.UpdatedAt.ToString("dd.MM HH:mm")
                    })
                    .ToList()
        };
    }

    private PortalTaskRow ToPortalTaskRow(TaskItem task) => new()
    {
        Title = task.Title,
        Status = TaskStatusText(task),
        Project = ProjectName(task.WorkspaceId)
    };

    private string ProjectName(Guid? workspaceId)
    {
        if (workspaceId is null)
        {
            return "Общие";
        }

        return _projects.Projects.FirstOrDefault(p => p.Id == workspaceId)?.Name ?? "Проект";
    }

    private void NotifyPortalIncoming(string title, string message)
    {
        if (_settings.ToastNotificationsEnabled)
        {
            TaskNotificationService.ShowPortalMessage(title, message);
        }

        if (_trayIcon is not null)
        {
            _trayIcon.ShowBalloonTip(4000, title, message, Forms.ToolTipIcon.Info);
        }
    }

    private void HandlePortalNote(PortalNoteRequest request)
    {
        Dispatcher.Invoke(() =>
        {
            var project = PortalProject();
            var note = new NoteItem
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Заметка с портала" : request.Title.Trim(),
                Text = string.IsNullOrWhiteSpace(request.Author)
                    ? request.Text.Trim()
                    : $"[{request.Author.Trim()}]\n{request.Text.Trim()}",
                Category = "Портал",
                WorkspaceId = project?.Id,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            _notes.Notes.Add(note);
            _notesStore.Save(_notes);
            var author = string.IsNullOrWhiteSpace(request.Author) ? "Гость" : request.Author.Trim();
            AddLog("OK", $"Заметка с портала: {note.Title}");
            NotifyPortalIncoming("WideS · заметка", $"{author}: {note.Title}");
            _portalService.NotifyUpdate();
        });
    }

    private void HandlePortalTask(PortalTaskRequest request)
    {
        Dispatcher.Invoke(() =>
        {
            var project = PortalProject();
            var task = new TaskItem
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Задача с портала" : request.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Author)
                    ? request.Description.Trim()
                    : $"[{request.Author.Trim()}]\n{request.Description.Trim()}",
                Status = "Новая",
                WorkspaceId = project?.Id,
                CreatedAt = DateTime.Now,
                StartAt = DateTime.Now,
                EndAt = DateTime.Now.AddHours(1),
                ReminderAt = null
            };
            _tasks.Tasks.Add(task);
            _tasksStore.Save(_tasks);
            var author = string.IsNullOrWhiteSpace(request.Author) ? "Гость" : request.Author.Trim();
            AddLog("OK", $"Задача с портала: {task.Title}");
            NotifyPortalIncoming("WideS · задача", $"{author}: {task.Title}");
            _portalService.NotifyUpdate();
        });
    }

    private void ShutdownPortalService()
    {
        _portalService.Dispose();
    }
}
