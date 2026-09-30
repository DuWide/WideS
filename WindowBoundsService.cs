using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DevCockpit;

/// <summary>
/// Геометрия окон без системной рамки (WindowStyle=None / WindowChrome):
/// разворачивание строго в рабочую область текущего монитора (панель задач с любой стороны,
/// автоскрытие, мониторы разного размера и DPI), полноэкранный режим чтения и удержание
/// обычного окна на видимом мониторе. Все расчёты — в физических пикселях: приложение
/// объявлено Per-Monitor V2 DPI aware (app.manifest).
/// </summary>
public static class WindowBoundsService
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmWindowPosChanging = 0x0046;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;

    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private const uint AbmGetAutoHideBarEx = 0x0000000B;
    private const int AutoHideTaskbarReserve = 2;

    private sealed class WindowEntry
    {
        public required Window Window { get; init; }
        public required HwndSourceHook Hook { get; init; }
        public double DesignMinWidth { get; set; }
        public double DesignMinHeight { get; set; }
        public IntPtr LastMonitor { get; set; }
        public bool FullScreen { get; set; }
        public bool FitPending { get; set; }
        public int FitAttempts { get; set; }
    }

    private static readonly Dictionary<IntPtr, WindowEntry> Entries = new();
    private static readonly Dictionary<IntPtr, (long Tick, int Edges)> AutoHideCache = new();

    /// <summary>
    /// Подключает корректное разворачивание к окну. Можно вызывать повторно и до создания HWND.
    /// </summary>
    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized -= Window_SourceInitialized;
            window.SourceInitialized += Window_SourceInitialized;
            return;
        }

        if (Entries.ContainsKey(handle))
        {
            return;
        }

        var source = HwndSource.FromHwnd(handle);
        if (source is null)
        {
            return;
        }

        HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            WndProc(hwnd, msg, wParam, lParam);
        var entry = new WindowEntry
        {
            Window = window,
            Hook = hook,
            DesignMinWidth = window.MinWidth,
            DesignMinHeight = window.MinHeight
        };
        Entries[handle] = entry;
        source.AddHook(hook);

        window.StateChanged += Window_StateChanged;
        window.LocationChanged += Window_LocationChanged;
        window.DpiChanged += Window_DpiChanged;
        window.Closed += (_, _) =>
        {
            window.StateChanged -= Window_StateChanged;
            window.LocationChanged -= Window_LocationChanged;
            window.DpiChanged -= Window_DpiChanged;
            try
            {
                source.RemoveHook(hook);
            }
            catch
            {
                // HwndSource уже освобождён вместе с окном.
            }

            Entries.Remove(handle);
        };

        AdaptMinSizeToMonitor(handle, entry, force: true);
    }

    /// <summary>
    /// Полноэкранный режим: развернуть окно на весь монитор (поверх панели задач) или вернуть обычное разворачивание.
    /// </summary>
    public static void SetFullScreen(Window window, bool fullScreen)
    {
        Attach(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !Entries.TryGetValue(handle, out var entry) || entry.FullScreen == fullScreen)
        {
            return;
        }

        entry.FullScreen = fullScreen;
        entry.FitAttempts = 0;
        if (window.WindowState == WindowState.Maximized)
        {
            // Пересчитать размер развернутого окна под новый режим без смены состояния.
            FitMaximized(handle, entry);
        }
    }

    public static bool IsFullScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && Entries.TryGetValue(handle, out var entry) && entry.FullScreen;
    }

    /// <summary>
    /// Обычное (не развернутое) окно: не больше рабочей области своего монитора и с видимым заголовком.
    /// Если окно пришлось уменьшить или оно было вне экранов, оно центрируется на ближайшем мониторе.
    /// </summary>
    public static void EnsureVisible(Window window, bool centerIfAdjusted = true)
    {
        if (window.WindowState != WindowState.Normal)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return;
        }

        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            return;
        }

        var work = info.rcWork;
        var width = Math.Min(rect.Width, work.Width);
        var height = Math.Min(rect.Height, work.Height);
        var resized = width != rect.Width || height != rect.Height;
        var titleVisible = IsPointOnAnyWorkArea(rect.Left + rect.Width / 2, rect.Top + 8) &&
                           IsPointOnAnyWorkArea(rect.Left + Math.Min(rect.Width - 1, 60), rect.Top + 8);
        if (!resized && titleVisible)
        {
            return;
        }

        int left;
        int top;
        if (centerIfAdjusted)
        {
            left = work.Left + (work.Width - width) / 2;
            top = work.Top + (work.Height - height) / 2;
        }
        else
        {
            left = Math.Clamp(rect.Left, work.Left, work.Right - width);
            top = Math.Clamp(rect.Top, work.Top, work.Bottom - height);
        }

        SetWindowPos(handle, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>
    /// Снимок положения окна для сохранения между запусками: обычный (не развернутый) прямоугольник
    /// в пикселях и признак «развернуто». Формат: "left,top,right,bottom,maximized".
    /// </summary>
    public static string CapturePlacement(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return "";
        }

        var placement = new WindowPlacementNative { length = Marshal.SizeOf<WindowPlacementNative>() };
        if (!GetWindowPlacement(handle, ref placement))
        {
            return "";
        }

        var r = placement.rcNormalPosition;
        if (r.Width <= 0 || r.Height <= 0)
        {
            return "";
        }

        var maximized = window.WindowState == WindowState.Maximized ||
                        (window.WindowState == WindowState.Minimized && (placement.flags & WpfRestoreToMaximized) != 0);
        return string.Join(",", r.Left, r.Top, r.Right, r.Bottom, maximized ? 1 : 0);
    }

    /// <summary>Признак «было развернуто» из сохранённого снимка (нужно выставить WindowState до показа окна).</summary>
    public static bool IsPlacementMaximized(string? value)
    {
        var parts = value?.Split(',');
        return parts is { Length: 5 } && parts[4] == "1";
    }

    /// <summary>
    /// Восстановить сохранённое положение до показа окна (вызывать из SourceInitialized).
    /// Потом EnsureVisible подгоняет его под текущие мониторы: если монитор отключили или сменилось
    /// разрешение, окно окажется на ближайшем видимом экране.
    /// </summary>
    public static bool RestorePlacement(Window window, string? value)
    {
        var parts = value?.Split(',');
        if (parts is not { Length: 5 } ||
            !int.TryParse(parts[0], out var left) || !int.TryParse(parts[1], out var top) ||
            !int.TryParse(parts[2], out var right) || !int.TryParse(parts[3], out var bottom) ||
            right - left < 100 || bottom - top < 100)
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var placement = new WindowPlacementNative { length = Marshal.SizeOf<WindowPlacementNative>() };
        if (!GetWindowPlacement(handle, ref placement))
        {
            return false;
        }

        placement.flags = 0;
        placement.showCmd = window.IsVisible ? SwShowNormal : SwHide;
        placement.rcNormalPosition = new RectNative { Left = left, Top = top, Right = right, Bottom = bottom };
        return SetWindowPlacement(handle, ref placement);
    }

    /// <summary>Рабочая область монитора под точкой экрана (в DIP для окна <paramref name="window"/>).</summary>
    public static Rect GetWorkAreaForScreenPoint(Window window, int screenX, int screenY)
    {
        var monitor = MonitorFromPoint(new PointNative { X = screenX, Y = screenY }, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            return SystemParameters.WorkArea;
        }

        var scale = GetScale(new WindowInteropHelper(window).Handle);
        var work = info.rcWork;
        return new Rect(work.Left / scale, work.Top / scale, work.Width / scale, work.Height / scale);
    }

    /// <summary>
    /// Перемещает ещё не показанное окно на указанный монитор (в физических пикселях) с учётом его DPI:
    /// сначала позиция (WPF пересчитает размер под новый DPI), затем окончательный прямоугольник.
    /// </summary>
    public static void MoveToMonitor(Window window, IntPtr monitor, bool center, int offset = 40)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !TryGetMonitorInfo(monitor, out var info))
        {
            return;
        }

        var work = info.rcWork;
        if (MonitorFromWindow(handle, MonitorDefaultToNearest) != monitor)
        {
            SetWindowPos(handle, IntPtr.Zero, work.Left + offset, work.Top + offset, 0, 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
        }

        if (!GetWindowRect(handle, out var rect))
        {
            return;
        }

        var width = Math.Min(rect.Width, Math.Max(320, work.Width - 20));
        var height = Math.Min(rect.Height, Math.Max(240, work.Height - 20));
        var left = center ? work.Left + (work.Width - width) / 2 : work.Left + offset;
        var top = center ? work.Top + (work.Height - height) / 2 : work.Top + offset;
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(handle, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    public static IntPtr GetPrimaryMonitor() =>
        MonitorFromPoint(new PointNative { X = 0, Y = 0 }, MonitorDefaultToPrimary);

    public static IntPtr GetMonitorForWindow(Window? window)
    {
        var handle = window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
        return handle == IntPtr.Zero ? GetPrimaryMonitor() : MonitorFromWindow(handle, MonitorDefaultToNearest);
    }

    public static IntPtr GetMonitorForScreenPoint(int x, int y) =>
        MonitorFromPoint(new PointNative { X = x, Y = y }, MonitorDefaultToNearest);

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= Window_SourceInitialized;
            Attach(window);
        }
    }

    private static void Window_StateChanged(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (!Entries.TryGetValue(handle, out var entry))
        {
            return;
        }

        // После смены состояния проверяем итоговую геометрию, когда Windows закончит позиционирование.
        window.Dispatcher.InvokeAsync(() =>
        {
            if (!Entries.ContainsKey(handle))
            {
                return;
            }

            entry.FitAttempts = 0;
            if (window.WindowState == WindowState.Maximized)
            {
                FitMaximized(handle, entry);
            }
            else if (window.WindowState == WindowState.Normal)
            {
                EnsureVisible(window, centerIfAdjusted: false);
            }
        }, DispatcherPriority.Background);
    }

    private static void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (sender is Window window &&
            Entries.TryGetValue(new WindowInteropHelper(window).Handle, out var entry))
        {
            AdaptMinSizeToMonitor(new WindowInteropHelper(window).Handle, entry, force: false);
        }
    }

    private static void Window_DpiChanged(object? sender, System.Windows.DpiChangedEventArgs e)
    {
        if (sender is Window window &&
            Entries.TryGetValue(new WindowInteropHelper(window).Handle, out var entry))
        {
            AdaptMinSizeToMonitor(new WindowInteropHelper(window).Handle, entry, force: true);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (!Entries.TryGetValue(hwnd, out var entry))
        {
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WmGetMinMaxInfo:
                // handled не ставим: дальше WPF применит MinWidth/MaxWidth окна к ptMinTrackSize/ptMaxTrackSize.
                ApplyMaximizedBounds(hwnd, entry, lParam);
                break;
            case WmWindowPosChanging:
                ClampMaximizedWindowPos(hwnd, entry, lParam);
                break;
            case WmWindowPosChanged:
                ScheduleFitMaximized(hwnd, entry);
                break;
            case WmDisplayChange:
                ScheduleDisplayRecheck(hwnd, entry);
                break;
            case WmSettingChange when wParam.ToInt64() == SpiSetWorkArea:
                ScheduleDisplayRecheck(hwnd, entry);
                break;
        }

        return IntPtr.Zero;
    }

    private static void ScheduleDisplayRecheck(IntPtr hwnd, WindowEntry entry)
    {
        AutoHideCache.Clear();
        entry.Window.Dispatcher.InvokeAsync(() =>
        {
            if (!Entries.ContainsKey(hwnd))
            {
                return;
            }

            AdaptMinSizeToMonitor(hwnd, entry, force: true);
            entry.FitAttempts = 0;
            if (entry.Window.WindowState == WindowState.Maximized)
            {
                FitMaximized(hwnd, entry);
            }
            else
            {
                EnsureVisible(entry.Window, centerIfAdjusted: true);
            }
        }, DispatcherPriority.Background);
    }

    private static void ApplyMaximizedBounds(IntPtr hwnd, WindowEntry entry, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            return;
        }

        var target = GetMaximizedRect(monitor, info, entry.FullScreen);
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        // ptMaxPosition задаётся относительно монитора; Windows переносит его на фактический монитор.
        mmi.ptMaxPosition.X = target.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = target.Top - info.rcMonitor.Top;

        // ptMaxSize Windows трактует относительно ОСНОВНОГО монитора: если значение не меньше размера
        // основного монитора, к нему прибавляется разница размеров мониторов. Из-за этого раньше окно
        // на большом втором мониторе разворачивалось шире/выше экрана. Для «весь монитор» передаём размер
        // основного монитора (Windows сам подставит нужный), иначе — точный размер рабочей области;
        // оставшиеся случаи дожимает WM_WINDOWPOSCHANGING.
        if (target.Width >= info.rcMonitor.Width && target.Height >= info.rcMonitor.Height &&
            TryGetMonitorInfo(GetPrimaryMonitor(), out var primary))
        {
            mmi.ptMaxSize.X = primary.rcMonitor.Width;
            mmi.ptMaxSize.Y = primary.rcMonitor.Height;
        }
        else
        {
            mmi.ptMaxSize.X = target.Width;
            mmi.ptMaxSize.Y = target.Height;
        }

        Marshal.StructureToPtr(mmi, lParam, false);
    }

    private static void ClampMaximizedWindowPos(IntPtr hwnd, WindowEntry entry, IntPtr lParam)
    {
        if (!IsZoomed(hwnd))
        {
            return;
        }

        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((pos.flags & SwpNoSize) != 0 || (pos.flags & SwpNoMove) != 0)
        {
            return;
        }

        var proposed = new RectNative { Left = pos.x, Top = pos.y, Right = pos.x + pos.cx, Bottom = pos.y + pos.cy };
        var monitor = MonitorFromRect(ref proposed, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            return;
        }

        var target = GetMaximizedRect(monitor, info, entry.FullScreen);
        if (proposed.Left >= target.Left && proposed.Top >= target.Top &&
            proposed.Right <= target.Right && proposed.Bottom <= target.Bottom)
        {
            return;
        }

        pos.x = target.Left;
        pos.y = target.Top;
        pos.cx = target.Width;
        pos.cy = target.Height;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    /// <summary>
    /// Развернутое окно перенесли на другой монитор (Win+Shift+стрелка, смена DPI, отключение монитора):
    /// после завершения перемещения подгоняем его под рабочую область нового монитора.
    /// </summary>
    private static void ScheduleFitMaximized(IntPtr hwnd, WindowEntry entry)
    {
        if (entry.FitPending || !IsZoomed(hwnd))
        {
            return;
        }

        entry.FitPending = true;
        entry.Window.Dispatcher.InvokeAsync(() =>
        {
            entry.FitPending = false;
            if (Entries.ContainsKey(hwnd))
            {
                FitMaximized(hwnd, entry);
            }
        }, DispatcherPriority.Background);
    }

    private static void FitMaximized(IntPtr hwnd, WindowEntry entry)
    {
        if (!IsZoomed(hwnd) || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            return;
        }

        var target = GetMaximizedRect(monitor, info, entry.FullScreen);
        if (rect.Left == target.Left && rect.Top == target.Top && rect.Right == target.Right && rect.Bottom == target.Bottom)
        {
            entry.FitAttempts = 0;
            return;
        }

        // Защита от зацикливания, если Windows не принимает прямоугольник (ограничения размеров окна).
        if (++entry.FitAttempts > 3)
        {
            return;
        }

        SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>
    /// MinWidth/MinHeight из разметки рассчитаны на обычный экран. На маленьком мониторе с масштабом
    /// 150–200% они больше рабочей области — и развернутое окно вылезало бы за край. Временно уменьшаем.
    /// </summary>
    private static void AdaptMinSizeToMonitor(IntPtr hwnd, WindowEntry entry, bool force)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (!force && monitor == entry.LastMonitor)
        {
            return;
        }

        entry.LastMonitor = monitor;
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            return;
        }

        var scale = GetScale(hwnd);
        var workWidth = Math.Floor(info.rcWork.Width / scale);
        var workHeight = Math.Floor(info.rcWork.Height / scale);
        var minWidth = Math.Min(entry.DesignMinWidth, workWidth);
        var minHeight = Math.Min(entry.DesignMinHeight, workHeight);
        var window = entry.Window;
        if (Math.Abs(window.MinWidth - minWidth) > 0.5)
        {
            window.MinWidth = minWidth;
        }

        if (Math.Abs(window.MinHeight - minHeight) > 0.5)
        {
            window.MinHeight = minHeight;
        }
    }

    private static RectNative GetMaximizedRect(IntPtr monitor, MonitorInfo info, bool fullScreen)
    {
        if (fullScreen)
        {
            return info.rcMonitor;
        }

        var rect = info.rcWork;
        var edges = GetAutoHideEdges(monitor, info.rcMonitor);
        // Автоскрываемая панель задач не уменьшает рабочую область; оставляем ей 2 px, иначе она не выедет.
        if ((edges & (1 << AbeTop)) != 0 && rect.Top == info.rcMonitor.Top) rect.Top += AutoHideTaskbarReserve;
        if ((edges & (1 << AbeBottom)) != 0 && rect.Bottom == info.rcMonitor.Bottom) rect.Bottom -= AutoHideTaskbarReserve;
        if ((edges & (1 << AbeLeft)) != 0 && rect.Left == info.rcMonitor.Left) rect.Left += AutoHideTaskbarReserve;
        if ((edges & (1 << AbeRight)) != 0 && rect.Right == info.rcMonitor.Right) rect.Right -= AutoHideTaskbarReserve;
        return rect;
    }

    private const int AbeLeft = 0;
    private const int AbeTop = 1;
    private const int AbeRight = 2;
    private const int AbeBottom = 3;

    private static int GetAutoHideEdges(IntPtr monitor, RectNative monitorRect)
    {
        var now = Environment.TickCount64;
        if (AutoHideCache.TryGetValue(monitor, out var cached) && now - cached.Tick < 3000)
        {
            return cached.Edges;
        }

        var edges = 0;
        try
        {
            foreach (var edge in new[] { AbeLeft, AbeTop, AbeRight, AbeBottom })
            {
                var data = new AppBarData
                {
                    cbSize = (uint)Marshal.SizeOf<AppBarData>(),
                    uEdge = (uint)edge,
                    rc = monitorRect
                };
                if (SHAppBarMessage(AbmGetAutoHideBarEx, ref data) != IntPtr.Zero)
                {
                    edges |= 1 << edge;
                }
            }
        }
        catch
        {
            edges = 0;
        }

        AutoHideCache[monitor] = (now, edges);
        return edges;
    }

    private static bool IsPointOnAnyWorkArea(int x, int y)
    {
        var monitor = MonitorFromPoint(new PointNative { X = x, Y = y }, MonitorDefaultToNull);
        if (monitor == IntPtr.Zero || !TryGetMonitorInfo(monitor, out var info))
        {
            return false;
        }

        var work = info.rcWork;
        return x >= work.Left && x < work.Right && y >= work.Top && y < work.Bottom;
    }

    private static double GetScale(IntPtr hwnd)
    {
        try
        {
            var dpi = hwnd == IntPtr.Zero ? 0u : GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96d : 1d;
        }
        catch
        {
            return 1d;
        }
    }

    private static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointNative ptReserved;
        public PointNative ptMaxSize;
        public PointNative ptMaxPosition;
        public PointNative ptMinTrackSize;
        public PointNative ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public int dwFlags;
    }

    private const int WpfRestoreToMaximized = 0x0002;
    private const int SwHide = 0;
    private const int SwShowNormal = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementNative
    {
        public int length;
        public int flags;
        public int showCmd;
        public PointNative ptMinPosition;
        public PointNative ptMaxPosition;
        public RectNative rcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacementNative lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacementNative lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RectNative rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RectNative lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PointNative pt, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RectNative lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);
}
