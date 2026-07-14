using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBrush = System.Windows.Media.Brush;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace DevCockpit;

public partial class MainWindow
{
    private readonly JsonFileStore<ClipboardHistoryStoreData> _clipboardStore = new(AppPaths.ClipboardHistoryJson);
    private ClipboardHistoryStoreData _clipboardHistory = new();
    private string _clipboardFilter = "Все";
    private WpfTextBox? _clipboardSearch;

    private void ShowClipboardHistory()
    {
        EnterView("clipboard");
        _viewScope = "clipboard";
        SetTitle("Буфер", "История копирования собирается автоматически и хранится локально через DPAPI");

        System.Windows.Controls.Panel? contentPanel = null;
        var root = SectionWithActions(actions =>
        {
            _clipboardSearch = SearchBox("Найти в буфере", () =>
            {
                if (contentPanel is not null) RenderClipboardItems(contentPanel);
            });
            actions.Children.Add(_clipboardSearch);
            var filters = new[] { "Все", "Текст", "Ссылка", "Путь", "Код", "Команда", "Адрес", "Изображение", "Секрет" };
            var filterBox = UiHelpers.CreateToolbarComboBox(170);
            foreach (var filter in filters)
            {
                filterBox.Items.Add(filter);
            }
            filterBox.SelectedItem = filters.Contains(_clipboardFilter) ? _clipboardFilter : "Все";
            filterBox.SelectionChanged += (_, _) =>
            {
                _clipboardFilter = filterBox.SelectedItem?.ToString() ?? "Все";
                ShowClipboardHistory();
            };
            actions.Children.Add(filterBox);
            actions.Children.Add(ToolbarGap());
            AddViewModeButtons(actions, ShowClipboardHistory);
            actions.Children.Add(ActionButton("Очистить незакрепленное", ClearClipboardHistory, false));
        }, out var panel);

        contentPanel = panel;
        RenderClipboardItems(panel);
        ContentHost.Content = root;
    }

    private void RenderClipboardItems(System.Windows.Controls.Panel panel)
    {
        panel.Children.Clear();
        var query = _clipboardSearch is null ? "" : UiHelpers.EffectiveText(_clipboardSearch);
        var items = _clipboardHistory.Items
            .Where(x => _clipboardFilter == "Все" || x.Kind.Equals(_clipboardFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Preview.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Kind.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.SourceApp.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CapturedAt)
            .ToList();

        foreach (var item in items)
        {
            panel.Children.Add(ClipboardCard(item));
        }

        if (items.Count == 0)
        {
            panel.Children.Add(UiHelpers.EmptyState(
                "Буфер пока пуст",
                _settings.ClipboardHistoryEnabled
                    ? "Скопируйте текст, ссылку, путь или изображение — WideS подхватит его сам."
                    : "История буфера выключена в настройках.",
                "Настройки",
                ShowSettings));
        }

    }

    private FrameworkElement ClipboardCard(ClipboardHistoryItem item)
    {
        var title = $"{item.Kind}  ·  {item.CapturedAt:HH:mm}";
        if (IsListView(_viewScope) || IsTableView(_viewScope))
        {
            return ListRow(
                item.IsSensitive ? $"{title}  ·  защищено" : $"{title}  ·  {Preview(item.Preview, 95)}",
                () => CopyClipboardItem(item),
                ClipboardKindBrush(item.Kind),
                RowActionButton("copy", "Копировать", () => CopyClipboardItem(item)),
                FavoriteIconButton(item.IsPinned, () => ToggleClipboardPinned(item)),
                RowActionButton("delete", "Удалить", () => DeleteClipboardItem(item)));
        }

        var card = Card(title);
        card.Width = 360;
        card.MinHeight = item.Kind == "Изображение" ? 260 : 185;
        var layout = new Grid();
        var stack = BaseCardStack(title);
        layout.Children.Add(stack);
        layout.Children.Add(FavoriteIconButton(item.IsPinned, () => ToggleClipboardPinned(item)));

        stack.Children.Add(Muted(string.IsNullOrWhiteSpace(item.SourceApp)
            ? item.CapturedAt.ToString("dd.MM.yyyy HH:mm:ss")
            : $"{item.SourceApp} · {item.CapturedAt:dd.MM HH:mm:ss}"));

        if (item.Kind == "Изображение" && File.Exists(item.ImagePath))
        {
            var image = new System.Windows.Controls.Image
            {
                Source = LoadClipboardImage(item.ImagePath),
                Height = 128,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 12, 0, 10),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };
            stack.Children.Add(image);
        }
        else
        {
            stack.Children.Add(Text(item.Preview, 14,
                item.IsSensitive ? (WpfBrush)FindResource("WarnBrush") : (WpfBrush)FindResource("TextBrush"),
                new Thickness(0, 14, 0, 12)));
        }

        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Копировать", () => CopyClipboardItem(item), false));
        if (CanOpenClipboardItem(item))
        {
            actions.Children.Add(ActionButton("Открыть", () => OpenClipboardItem(item), false));
        }
        actions.Children.Add(IconButton("delete", () => DeleteClipboardItem(item), "Удалить", 30));
        stack.Children.Add(actions);
        card.Child = layout;
        return card;
    }

    private void CaptureCurrentClipboard()
    {
        if (!_settings.ClipboardHistoryEnabled) return;
        try
        {
            ClipboardHistoryItem? captured = null;
            var source = ClipboardOwnerProcessName();
            if (WpfClipboard.ContainsText())
            {
                captured = ClipboardHistoryService.CaptureText(_clipboardHistory, WpfClipboard.GetText(), source);
            }
            else if (WpfClipboard.ContainsImage())
            {
                var image = WpfClipboard.GetImage();
                if (image is not null) captured = ClipboardHistoryService.CaptureImage(_clipboardHistory, image, source);
            }

            if (captured is null) return;
            _clipboardStore.Save(_clipboardHistory);
            if (_currentViewKey == "clipboard") ShowClipboardHistory();
        }
        catch (ExternalException)
        {
            // The clipboard can be briefly locked by the source application.
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Буфер: {ex.Message}");
        }
    }

    private void CopyClipboardItem(ClipboardHistoryItem item)
    {
        if (item.Kind == "Изображение" && File.Exists(item.ImagePath))
        {
            var image = LoadClipboardImage(item.ImagePath);
            WpfClipboard.SetImage(image);
            AddLog("OK", "Изображение возвращено в буфер.");
            return;
        }

        var text = ClipboardHistoryService.GetText(item);
        if (string.IsNullOrEmpty(text)) return;
        WpfClipboard.SetText(text);
        AddLog("OK", $"Буфер: скопировано ({item.Kind.ToLowerInvariant()}).");
    }

    private void OpenClipboardItem(ClipboardHistoryItem item)
    {
        var value = ClipboardHistoryService.GetText(item);
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = value.Trim(), UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Буфер: не удалось открыть — {ex.Message}");
        }
    }

    private static bool CanOpenClipboardItem(ClipboardHistoryItem item) =>
        item.Kind is "Ссылка" or "Путь";

    private void ToggleClipboardPinned(ClipboardHistoryItem item)
    {
        item.IsPinned = !item.IsPinned;
        _clipboardStore.Save(_clipboardHistory);
        ShowClipboardHistory();
    }

    private void DeleteClipboardItem(ClipboardHistoryItem item)
    {
        ClipboardHistoryService.Delete(_clipboardHistory, item);
        _clipboardStore.Save(_clipboardHistory);
        ShowClipboardHistory();
    }

    private void ClearClipboardHistory()
    {
        if (System.Windows.MessageBox.Show(this,
                "Очистить всю незакрепленную историю буфера?",
                "WideS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        ClipboardHistoryService.ClearUnpinned(_clipboardHistory);
        _clipboardStore.Save(_clipboardHistory);
        ShowClipboardHistory();
    }

    private WpfBrush ClipboardKindBrush(string kind) => kind switch
    {
        "Ссылка" or "Адрес" => (WpfBrush)FindResource("AccentBrush"),
        "Путь" => (WpfBrush)FindResource("SuccessBrush"),
        "Код" or "Команда" => (WpfBrush)FindResource("PurpleBrush"),
        "Секрет" => (WpfBrush)FindResource("WarnBrush"),
        "Изображение" => (WpfBrush)FindResource("InfoBrush"),
        _ => (WpfBrush)FindResource("MutedBrush")
    };

    private static BitmapSource LoadClipboardImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string ClipboardOwnerProcessName()
    {
        try
        {
            var owner = GetClipboardOwner();
            if (owner == IntPtr.Zero) return "Windows";
            GetWindowThreadProcessId(owner, out var processId);
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "Windows";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
