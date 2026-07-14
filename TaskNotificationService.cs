using Microsoft.Toolkit.Uwp.Notifications;

namespace DevCockpit;

public sealed class TaskNotificationAction
{
    public Guid TaskId { get; init; }
    public string Action { get; init; } = "";
    public int SnoozeMinutes { get; init; } = 15;
}

public static class TaskNotificationService
{
    public const string AppUserModelId = "WideS.DevCockpit";
    public static event Action<TaskNotificationAction>? NotificationActivated;

    public static void Initialize()
    {
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            var args = ToastArguments.Parse(toastArgs.Argument);
            if (args.TryGetValue("source", out var source) && source == "portal")
            {
                NotificationActivated?.Invoke(new TaskNotificationAction { Action = "portal" });
                return;
            }

            if (!args.TryGetValue("taskId", out var taskIdRaw) || !Guid.TryParse(taskIdRaw, out var taskId))
            {
                NotificationActivated?.Invoke(new TaskNotificationAction { Action = "activate" });
                return;
            }

            var action = args.TryGetValue("action", out var actionRaw) ? actionRaw : "open";
            var snoozeMinutes = toastArgs.UserInput.TryGetValue("snoozeMinutes", out var snoozeRaw) &&
                                 int.TryParse(Convert.ToString(snoozeRaw), out var parsedMinutes)
                ? parsedMinutes
                : 15;
            NotificationActivated?.Invoke(new TaskNotificationAction
            {
                TaskId = taskId,
                Action = action,
                SnoozeMinutes = snoozeMinutes
            });
        };
    }

    public static void RegisterApp()
    {
        try
        {
            ToastNotificationManagerCompat.WasCurrentProcessToastActivated();
        }
        catch
        {
            // ignore if already registered
        }
    }

    public static void ShowReminder(TaskItem task)
    {
        var description = string.IsNullOrWhiteSpace(task.Description)
            ? "Напоминание о задаче"
            : task.Description;

        new ToastContentBuilder()
            .AddArgument("taskId", task.Id.ToString())
            .AddText(task.Title)
            .AddText(description)
            .AddComboBox(
                "snoozeMinutes",
                "Отложить на",
                "15",
                ("15", "15 минут"),
                ("60", "1 час"),
                ("360", "6 часов"),
                ("1440", "1 день"),
                ("10080", "1 неделю"))
            .AddButton(new ToastButton()
                .SetContent("Начать")
                .AddArgument("action", "start")
                .AddArgument("taskId", task.Id.ToString()))
            .AddButton(new ToastButton()
                .SetContent("Отложить")
                .AddArgument("action", "snooze")
                .AddArgument("taskId", task.Id.ToString()))
            .Show(toast =>
            {
                toast.Tag = task.Id.ToString();
                toast.Group = "WideS.Tasks";
            });
    }

    public static void ShowPortalMessage(string title, string message)
    {
        new ToastContentBuilder()
            .AddArgument("source", "portal")
            .AddText(title)
            .AddText(message)
            .Show(toast =>
            {
                toast.Tag = "portal";
                toast.Group = "WideS.Portal";
            });
    }

    public static void ClearReminder(Guid taskId)
    {
        ToastNotificationManagerCompat.History.Remove(taskId.ToString(), "WideS.Tasks");
    }
}
