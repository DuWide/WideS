namespace DevCockpit;

public sealed class ProjectProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ProjectFolder { get; set; } = "";
    public string EditorPath { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
    public string ReleasesFolder { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastOpenedAt { get; set; }
    public bool IsPinned { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "(без названия)" : Name;
}

public sealed class ProjectStoreData
{
    public List<ProjectProfile> Projects { get; set; } = [];
}

public sealed class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Общее";
    public string Tags { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsImportant { get; set; }
    public bool IsPinned { get; set; }
    public Guid? WorkspaceId { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "(без заголовка)" : Title;
}

public sealed class NotesStoreData
{
    public List<NoteItem> Notes { get; set; } = [];
}

public sealed class ConnectionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "AnyDesk";
    public string Address { get; set; } = "";
    public string Login { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Guid? WorkspaceId { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "(без названия)" : Name;
}

public sealed class ConnectionsStoreData
{
    public List<ConnectionItem> Connections { get; set; } = [];
}

public sealed class AppSettingsData
{
    public string AnyDeskPath { get; set; } = "";
    public string UserName { get; set; } = "Олег";
    public string LoginPasswordEncrypted { get; set; } = "";
    public bool DisableLogin { get; set; }
    public List<string> BrowserCategories { get; set; } = ["AI Agents", "Основное", "Mail"];
    public List<string> BrowserCategoryOrder { get; set; } = [];
    public bool IsFirstRunConfigured { get; set; }
    public string DockPosition { get; set; } = "Center";
    public bool DockAutoHide { get; set; }
    public bool RunAtStartup { get; set; }
    public string AccentTheme { get; set; } = "Dark";
    public bool CompactSidebar { get; set; }
    public string WorkMode { get; set; } = "Work";
    public bool TelegramEnabled { get; set; }
    public string TelegramSource { get; set; } = "DesktopExport";
    public string TelegramDesktopExportPath { get; set; } = "";
    public string TelegramBotTokenEncrypted { get; set; } = "";
    public long TelegramLastUpdateId { get; set; }
    public long TelegramBotId { get; set; } = 778912409;
    public bool ToastNotificationsEnabled { get; set; } = true;
    public bool PortalEnabled { get; set; }
    public int PortalPort { get; set; } = 7788;
    public bool ClipboardScreenshotPrompt { get; set; } = true;
    public bool ClipboardHistoryEnabled { get; set; } = true;
}

public sealed record SelectOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed class ClipboardHistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "Текст";
    public string Preview { get; set; } = "";
    public string EncryptedContent { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string SourceApp { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public bool IsSensitive { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}

public sealed class ClipboardHistoryStoreData
{
    public List<ClipboardHistoryItem> Items { get; set; } = [];
}

public sealed class CommandRecipeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public bool UseShell { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class CommandRecipesStoreData
{
    public List<CommandRecipeItem> Recipes { get; set; } = [];
}

public sealed class AiAgentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "AI Agents";
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "(без названия)" : Name;
}

public sealed class AiAgentsStoreData
{
    public List<AiAgentItem> Agents { get; set; } = [];
}

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Новая";
    public DateTime StartAt { get; set; } = DateTime.Now;
    public DateTime EndAt { get; set; } = DateTime.Now.AddHours(1);
    public string Importance { get; set; } = "Green";
    public Guid? WorkspaceId { get; set; }
    public bool IsDone { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? ReminderAt { get; set; } = DateTime.Now;
    public DateTime? LastNotifiedAt { get; set; }
    public string Recurrence { get; set; } = "None";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string ContactName { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string TelegramKey { get; set; } = "";
    public string TelegramExternalId { get; set; } = "";
    public DateTime? WorkStartedAt { get; set; }
}

public sealed class TasksStoreData
{
    public List<TaskItem> Tasks { get; set; } = [];
}

public sealed class ActivityEntry
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? WorkspaceId { get; set; }
}

public sealed class ActivityStoreData
{
    public List<ActivityEntry> Entries { get; set; } = [];
}

public sealed class ProjectTemplateItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<string> Folders { get; set; } = [];
    public List<string> NoteTitles { get; set; } = [];
    public List<string> TaskTitles { get; set; } = [];
}

public sealed class ProjectTemplatesStoreData
{
    public List<ProjectTemplateItem> Templates { get; set; } = [];
}

public sealed class TextFileInfo
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public DateTime Modified { get; init; }
    public long Size { get; init; }
    public bool IsSuspicious { get; init; }
}

public sealed class XmlFolderInfo
{
    public string FullPath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public int Count { get; init; }
    public DateTime LastModified { get; init; }
}

public sealed class ScanResult
{
    public List<TextFileInfo> TextFiles { get; } = [];
    public List<string> Workspaces { get; } = [];
    public List<XmlFolderInfo> XmlFolders { get; } = [];
    public List<string> ResultFiles { get; } = [];
}
