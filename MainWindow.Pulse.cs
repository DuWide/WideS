using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace DevCockpit;

public partial class MainWindow
{
    private readonly SystemPulseService _pulseService = new();
    private readonly System.Windows.Threading.DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private SystemPulseSnapshot _pulseSnapshot = SystemPulseSnapshot.Empty;
    private bool _pulseUpdating;
    private TextBlock? _pulseCpuText;
    private TextBlock? _pulseMemoryText;
    private TextBlock? _pulseDiskText;
    private TextBlock? _pulseNetworkText;
    private WpfProgressBar? _pulseCpuBar;
    private WpfProgressBar? _pulseMemoryBar;
    private WpfProgressBar? _pulseDiskBar;
    private TextBlock? _pulseEnvironmentText;
    private WrapPanel? _pulseToolsPanel;

    private void InitializePulse()
    {
        _pulseTimer.Tick += async (_, _) => await UpdatePulseAsync();
        _pulseTimer.Start();
        _ = UpdatePulseAsync();
    }

    private async Task UpdatePulseAsync()
    {
        if (_pulseUpdating) return;
        _pulseUpdating = true;
        try
        {
            _pulseSnapshot = await _pulseService.CaptureAsync();
            UpdatePulseVisuals();
        }
        catch
        {
            // A transient adapter/process failure should not affect the cockpit.
        }
        finally
        {
            _pulseUpdating = false;
        }
    }

    private void ShowPulse()
    {
        EnterView("pulse");
        SetTitle("Пульс", "Живое состояние компьютера, сети и рабочих приложений — без ручного ввода");

        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 20) };
        var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(4, 0, 4, 10) };
        metrics.Children.Add(PulseMetricCard("Процессор", "cpu", out _pulseCpuText, out _pulseCpuBar));
        metrics.Children.Add(PulseMetricCard("Память", "memory", out _pulseMemoryText, out _pulseMemoryBar));
        metrics.Children.Add(PulseMetricCard("Системный диск", "disk", out _pulseDiskText, out _pulseDiskBar));
        metrics.Children.Add(PulseNetworkCard());
        root.Children.Add(metrics);

        var details = new Grid { Margin = new Thickness(4) };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });

        var toolsCard = Card("Рабочие приложения");
        toolsCard.Width = double.NaN;
        toolsCard.MinHeight = 210;
        toolsCard.Margin = new Thickness(4, 0, 8, 0);
        var toolsStack = BaseCardStack("Сейчас запущено");
        toolsStack.Children.Add(Muted("WideS сам видит редакторы, 1C, терминалы и удаленные подключения."));
        _pulseToolsPanel = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        toolsStack.Children.Add(_pulseToolsPanel);
        toolsCard.Child = toolsStack;
        Grid.SetColumn(toolsCard, 0);
        details.Children.Add(toolsCard);

        var environmentCard = Card("Окружение");
        environmentCard.Width = double.NaN;
        environmentCard.MinHeight = 210;
        environmentCard.Margin = new Thickness(8, 0, 4, 0);
        var environmentStack = BaseCardStack("Окружение");
        _pulseEnvironmentText = Text("", 13, (WpfBrush)FindResource("MutedBrush"), new Thickness());
        environmentStack.Children.Add(_pulseEnvironmentText);
        var copySnapshot = ActionButton("Копировать сводку", CopyPulseSummary, false);
        copySnapshot.Margin = new Thickness(0, 16, 0, 0);
        environmentStack.Children.Add(copySnapshot);
        environmentCard.Child = environmentStack;
        Grid.SetColumn(environmentCard, 1);
        details.Children.Add(environmentCard);
        root.Children.Add(details);

        ContentHost.Content = root;
        UpdatePulseVisuals();
        _ = UpdatePulseAsync();
    }

    private Border PulseMetricCard(string title, string icon, out TextBlock value, out WpfProgressBar bar)
    {
        var card = Card(title);
        card.Width = double.NaN;
        card.MinHeight = 155;
        card.Margin = new Thickness(4);
        var stack = new StackPanel();
        var header = new DockPanel();
        var iconShell = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = (WpfBrush)FindResource("AccentSoftBgBrush"),
            BorderBrush = (WpfBrush)FindResource("AccentBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = MakeIcon(icon, 16)
        };
        DockPanel.SetDock(iconShell, Dock.Right);
        header.Children.Add(iconShell);
        header.Children.Add(Text(title, 13, (WpfBrush)FindResource("MutedBrush"), new Thickness(), FontWeights.SemiBold));
        stack.Children.Add(header);
        value = Text("—", 26, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 14, 0, 12), FontWeights.SemiBold);
        stack.Children.Add(value);
        bar = new WpfProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            BorderThickness = new Thickness(0),
            Foreground = (WpfBrush)FindResource("AccentBrush"),
            Background = (WpfBrush)FindResource("PanelBrush")
        };
        stack.Children.Add(bar);
        card.Child = stack;
        return card;
    }

    private Border PulseNetworkCard()
    {
        var card = Card("Сеть");
        card.Width = double.NaN;
        card.MinHeight = 155;
        card.Margin = new Thickness(4);
        var stack = new StackPanel();
        var header = new DockPanel();
        var indicator = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = (WpfBrush)FindResource("SuccessBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(indicator, Dock.Right);
        header.Children.Add(indicator);
        header.Children.Add(Text("Сеть", 13, (WpfBrush)FindResource("MutedBrush"), new Thickness(), FontWeights.SemiBold));
        stack.Children.Add(header);
        _pulseNetworkText = Text("—", 16, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 16, 0, 0), FontWeights.SemiBold);
        stack.Children.Add(_pulseNetworkText);
        card.Child = stack;
        return card;
    }

    private void UpdatePulseVisuals()
    {
        if (_pulseCpuText is null) return;
        var snap = _pulseSnapshot;
        _pulseCpuText.Text = $"{snap.CpuPercent:0}%";
        _pulseMemoryText!.Text = $"{snap.MemoryPercent:0}%  ·  {FormatBytes(snap.MemoryUsedBytes)}";
        _pulseDiskText!.Text = $"{snap.DiskPercent:0}%  ·  {FormatBytes((ulong)Math.Max(0, snap.DiskFreeBytes))} свободно";
        _pulseNetworkText!.Text = snap.InternetLatencyMs is { } latency
            ? $"{latency} мс\n↓ {FormatRate(snap.DownloadBytesPerSecond)}   ↑ {FormatRate(snap.UploadBytesPerSecond)}"
            : $"локальная сеть\n↓ {FormatRate(snap.DownloadBytesPerSecond)}   ↑ {FormatRate(snap.UploadBytesPerSecond)}";
        _pulseCpuBar!.Value = snap.CpuPercent;
        _pulseMemoryBar!.Value = snap.MemoryPercent;
        _pulseDiskBar!.Value = snap.DiskPercent;
        _pulseEnvironmentText!.Text =
            $"Компьютер: {Environment.MachineName}\n" +
            $"Сеть: {snap.NetworkName}\n" +
            $"Локальный IP: {snap.LocalAddress}\n" +
            $"Экраны: {snap.ScreenCount} · {snap.ScreenSummary}\n" +
            $"Процессов: {snap.ProcessCount}\n" +
            $"Аптайм: {snap.Uptime}";

        _pulseToolsPanel!.Children.Clear();
        if (snap.ActiveTools.Count == 0)
        {
            _pulseToolsPanel.Children.Add(Muted("Рабочие приложения не обнаружены."));
        }
        else
        {
            foreach (var tool in snap.ActiveTools)
            {
                _pulseToolsPanel.Children.Add(new Border
                {
                    Background = (WpfBrush)FindResource("AccentSoftBgBrush"),
                    BorderBrush = (WpfBrush)FindResource("AccentBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 8, 8),
                    Child = new TextBlock
                    {
                        Text = tool,
                        Foreground = (WpfBrush)FindResource("TextBrush"),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    }
                });
            }
        }
    }

    private void CopyPulseSummary()
    {
        var snap = _pulseSnapshot;
        Copy(
            $"{Environment.MachineName}\nCPU: {snap.CpuPercent:0}%\nRAM: {snap.MemoryPercent:0}% ({FormatBytes(snap.MemoryUsedBytes)})\n" +
            $"Disk: {snap.DiskPercent:0}% used, {FormatBytes((ulong)Math.Max(0, snap.DiskFreeBytes))} free\n" +
            $"Network: {snap.NetworkName}, {snap.LocalAddress}, latency {(snap.InternetLatencyMs?.ToString() ?? "n/a")} ms\n" +
            $"Screens: {snap.ScreenSummary}\nApps: {string.Join(", ", snap.ActiveTools)}",
            "Сводка состояния скопирована.");
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatRate(double bytesPerSecond) =>
        FormatBytes((ulong)Math.Max(0, bytesPerSecond)) + "/с";
}
