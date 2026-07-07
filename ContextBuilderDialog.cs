using System.Text;

namespace DevCockpit;

public sealed class ContextBuilderDialog : Form
{
    private static readonly HashSet<string> SuggestedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".bsl", ".xml", ".md", ".json", ".cs", ".sql"
    };

    private readonly ProjectProfile _project;
    private readonly CheckedListBox _files = new();
    private readonly TextBox _task = new();

    public string? CreatedContextPath { get; private set; }

    public ContextBuilderDialog(ProjectProfile project)
    {
        _project = project;
        Text = "Собрать context.txt";
        Width = 860;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Проверьте, нет ли в выбранных файлах логинов, паролей и доступов перед отправкой context.txt в AI.",
            ForeColor = Color.Khaki
        }, 0, 0);

        _files.Dock = DockStyle.Fill;
        _files.BackColor = Color.FromArgb(38, 38, 40);
        _files.ForeColor = Color.WhiteSmoke;
        root.Controls.Add(_files, 0, 1);

        _task.Multiline = true;
        _task.Text = "Опишите здесь задачу для Codex/Cursor.";
        WinFormsDialogHelpers.StyleTextBox(_task);
        root.Controls.Add(_task, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var build = WinFormsDialogHelpers.DarkButton("Собрать context.txt");
        var cancel = WinFormsDialogHelpers.DarkButton("Отмена");
        build.Click += (_, _) => BuildContext();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(build);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);

        LoadFiles();
    }

    private void LoadFiles()
    {
        if (!Directory.Exists(_project.ProjectFolder))
        {
            return;
        }

        foreach (var file in ProjectScanner.EnumerateFilesSafe(_project.ProjectFolder).OrderBy(x => x))
        {
            var info = new FileInfo(file);
            var ext = info.Extension;
            if (!SuggestedExtensions.Contains(ext))
            {
                continue;
            }

            var relative = ProjectScanner.Relative(_project.ProjectFolder, file);
            var isLarge = info.Length > 2 * 1024 * 1024;
            var suspicious = ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) && ProjectScanner.IsSuspiciousTextName(info.Name);
            var selected = !isLarge && !suspicious;
            var label = $"{relative} ({FormatBytes(info.Length)})";
            if (isLarge)
            {
                label += " [больше 2 МБ]";
            }
            if (suspicious)
            {
                label += " [похоже на доступы]";
            }

            _files.Items.Add(new ContextFileItem(file, label), selected);
        }
    }

    private void BuildContext()
    {
        if (_files.CheckedItems.Count == 0)
        {
            MessageBox.Show(this, "Выберите хотя бы один файл.", "WideS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            "Проверьте, нет ли в выбранных файлах логинов, паролей и доступов перед отправкой context.txt в AI.",
            "Проверка безопасности",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        var day = AppPaths.EnsureTodayWorkDay(_project);
        var contextDir = Path.Combine(day, "Context");
        Directory.CreateDirectory(contextDir);
        CreatedContextPath = Path.Combine(contextDir, $"context_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt");

        var sb = new StringBuilder();
        sb.AppendLine("=== PROJECT INFO ===");
        sb.AppendLine($"Название проекта: {_project.Name}");
        sb.AppendLine($"Папка проекта: {_project.ProjectFolder}");
        sb.AppendLine($"Дата сборки: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (ContextFileItem item in _files.CheckedItems)
        {
            sb.AppendLine($"=== FILE: {ProjectScanner.Relative(_project.ProjectFolder, item.FullPath)} ===");
            try
            {
                sb.AppendLine(File.ReadAllText(item.FullPath));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Ошибка чтения файла: {ex.Message}]");
            }
            sb.AppendLine();
        }

        sb.AppendLine("=== TASK ===");
        sb.AppendLine(_task.Text);
        File.WriteAllText(CreatedContextPath, sb.ToString(), Encoding.UTF8);
        Clipboard.SetText(CreatedContextPath);
        ShellHelper.OpenPath(CreatedContextPath);
        DialogResult = DialogResult.OK;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }

    private sealed record ContextFileItem(string FullPath, string Label)
    {
        public override string ToString() => Label;
    }
}
