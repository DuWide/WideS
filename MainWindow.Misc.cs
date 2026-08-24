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
