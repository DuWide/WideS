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
    private void ShowDropZone()
    {
        EnterView("dropzone");
        SetTitle("DropZone", "Все файлы — в одну папку проекта");
        var card = Card("Перетащите файлы сюда");
        card.Width = double.NaN;
        card.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        card.MinHeight = 430;
        card.AllowDrop = true;
        card.DragOver += (_, e) => e.Effects = System.Windows.DragDropEffects.Copy;
        card.Drop += DropZone_Drop;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text("DropZone", 34, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 0, 0, 14), FontWeights.SemiBold));
        stack.Children.Add(Text("Одна папка на проект — без подпапок", 16, (WpfBrush)FindResource("MutedBrush"), new Thickness(0, 0, 0, 10)));
        stack.Children.Add(Text(DropTargetPreview(), 13, (WpfBrush)FindResource("AccentBrush"), new Thickness(0, 0, 0, 16)));
        var actions = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        actions.Children.Add(ActionButton("Открыть папку", OpenDropZoneFolder, false));
        actions.Children.Add(ActionButton("Очистить", ClearDropZoneFolder, false));
        actions.Children.Add(ActionButton("Выбрать проект", ShowProjects, false));
        if (_lastDropBatch.Count > 0) actions.Children.Add(ActionButton("Отменить drop", UndoLastDrop, false));
        stack.Children.Add(actions);
        if (_droppedFiles.Count > 0)
        {
            stack.Children.Add(Text("Добавлено:", 16, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 22, 0, 8), FontWeights.SemiBold));
            stack.Children.Add(Text(string.Join("\n", _droppedFiles.TakeLast(12)), 13, (WpfBrush)FindResource("MutedBrush"), new Thickness()));
        }
        card.Child = stack;
        ContentHost.Content = card;
    }
    private void ShowBackupContext()
    {
        EnterView("backup");
        SetTitle("Backup", "Защитить проект и подготовить материалы для AI");
        var panel = new WrapPanel();
        var selected = _selectedProject ?? _projects.Projects.FirstOrDefault();
        panel.Children.Add(CardText("Выбранный проект", selected is null ? "Проект не выбран." : $"{selected.Name}\n{selected.ProjectFolder}"));

        var progressBar = new System.Windows.Controls.ProgressBar
        {
            Width = 680,
            Height = 8,
            Margin = new Thickness(8, 4, 8, 4),
            Visibility = Visibility.Collapsed
        };
        var progressLabel = Muted("Готов к backup");
        WpfButton? backupBtn = null;
        WpfButton? contextBtn = null;
        WpfButton? openBtn = null;

        void SetBusy(bool busy, string label)
        {
            if (backupBtn is not null) backupBtn.IsEnabled = !busy;
            if (contextBtn is not null) contextBtn.IsEnabled = !busy;
            if (openBtn is not null) openBtn.IsEnabled = !busy;
            progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            progressLabel.Text = label;
        }

        var actions = Card("Действия");
        var wrap = new WrapPanel();
        backupBtn = ActionButton("Backup", () => _ = RunBackupAsync());
        contextBtn = ActionButton("Скопировать в AI", () => RunCopyForAiWithBusy(), false);
        openBtn = ActionButton("Открыть Backups", OpenBackups, false);
        wrap.Children.Add(backupBtn);
        wrap.Children.Add(contextBtn);
        wrap.Children.Add(openBtn);
        actions.Child = WithTitle("Действия", wrap);
        panel.Children.Add(actions);
        panel.Children.Add(progressBar);
        panel.Children.Add(progressLabel);
        panel.Children.Add(CardText("Последние backup", LastBackupsText(selected)));
        ContentHost.Content = panel;

        async Task RunBackupAsync()
        {
            var project = RequireProject();
            if (project is null) return;
            SetBusy(true, "Подготовка backup...");
            try
            {
                var progress = new Progress<BackupService.BackupProgress>(p =>
                {
                    progressBar.Maximum = Math.Max(1, p.Total);
                    progressBar.Value = p.Current;
                    progressLabel.Text = $"Архивация {p.Current}/{p.Total}: {Path.GetFileName(p.FileName)}";
                });
                var result = await BackupService.CreateBackupAsync(project, progress);
                WpfClipboard.SetText(result.ZipPath);
                AddLog("OK", $"Backup создан: {result.ZipPath} (+{result.NewFilesSincePrevious} новых файлов)");
                ShowBackupContext();
            }
            catch (Exception ex)
            {
                AddLog("ERR", $"Backup: {ex.Message}");
                SetBusy(false, "Ошибка backup");
            }
        }

        void RunCopyForAiWithBusy()
        {
            var project = RequireProject();
            if (project is null) return;
            SetBusy(true, "Подготовка текста для AI...");
            try
            {
                var window = new CopyForAiWindow(project, _notes.Notes) { Owner = this };
                if (window.ShowDialog() == true)
                {
                    AddLog("OK", window.SavedPath is null
                        ? "Текст для AI скопирован в буфер."
                        : $"Текст для AI скопирован. Файл: {window.SavedPath}");
                }
            }
            finally
            {
                SetBusy(false, "Готов к backup");
            }
        }
    }
    private void CopyForAiSelected()
    {
        var project = RequireProject();
        if (project is null) return;
        var window = new CopyForAiWindow(project, _notes.Notes) { Owner = this };
        if (window.ShowDialog() == true)
        {
            AddLog("OK", window.SavedPath is null
                ? "Текст для AI скопирован в буфер."
                : $"Текст для AI скопирован. Файл: {window.SavedPath}");
        }
    }
    private void OpenBackups()
    {
        var project = RequireProject();
        if (project is null) return;
        ShellHelper.OpenPath(Path.Combine(AppPaths.EnsureTodayWorkDay(project), "Backups"));
    }
    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        ImportFilesToDropZone(files);
    }
    private void ImportFilesToDropZone(string[] files)
    {
        if (!EnsureProjectSelected("DropZone")) return;
        var project = _selectedProject!;

        var targetDir = AppPaths.GetDropZoneFolder(project);
        _droppedFiles.Clear();
        _lastDropBatch.Clear();
        foreach (var file in files.Where(File.Exists))
        {
            var target = UniquePath(Path.Combine(targetDir, Path.GetFileName(file)));
            File.Copy(file, target);
            _droppedFiles.Add(target);
            _lastDropBatch.Add(target);
        }

        AddLog("OK", $"DropZone: добавлено файлов {_droppedFiles.Count} → {targetDir}");
        if (_currentViewKey == "dropzone") ShowDropZone();
    }
    private void OpenDropZoneFolder()
    {
        var project = RequireProject();
        if (project is null) return;
        var folder = AppPaths.GetDropZoneFolder(project);
        var explorer = ShellHelper.OpenPath(folder);
        WindowPlacementService.MoveProcessToPrimaryAsync(explorer, "explorer");
        AddLog("OK", $"DropZone: {folder}");
    }
    private void ClearDropZoneFolder()
    {
        var project = RequireProject();
        if (project is null) return;
        var folder = AppPaths.GetDropZoneFolder(project);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var files = Directory.GetFiles(folder);
        if (files.Length == 0)
        {
            AddLog("OK", "DropZone уже пуст.");
            return;
        }

        if (WpfMessageBox.Show(this, $"Удалить все файлы ({files.Length}) из DropZone?\n{folder}", "WideS",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var deleted = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch
            {
                // ignore
            }
        }

        _droppedFiles.Clear();
        _lastDropBatch.Clear();
        AddLog("OK", $"DropZone: удалено файлов {deleted}.");
        if (_currentViewKey == "dropzone") ShowDropZone();
    }
}
