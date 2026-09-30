using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DevCockpit;

public static class EditorWindowHelper
{
    private static readonly Dictionary<Guid, Window> OpenWindows = new();

    public static void MinimizeWindow(Window window) => window.WindowState = WindowState.Minimized;

    public static void TitleBar_MouseLeftButtonDown(Window window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            RestoreFromMaximizedForDrag(window, e.GetPosition(window));
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove может бросить, если кнопка мыши уже отпущена.
        }
    }

    /// <summary>Стандарт Windows: drag с развёрнутого окна → restore под курсором, затем move.</summary>
    public static void RestoreFromMaximizedForDrag(Window window, System.Windows.Point mouseInWindow)
    {
        // PointToScreen возвращает физические пиксели, а Left/Top/Width/Height — DIP:
        // без пересчёта при масштабе 125–175% окно «отпрыгивало» от курсора.
        var screenPx = window.PointToScreen(mouseInWindow);
        var dpi = VisualTreeHelper.GetDpi(window);
        var cursorX = screenPx.X / dpi.DpiScaleX;
        var cursorY = screenPx.Y / dpi.DpiScaleY;
        // Рабочая область монитора под курсором, а не всегда основного (SystemParameters.WorkArea):
        // иначе окно со второго монитора перескакивало на первый.
        var wa = WindowBoundsService.GetWorkAreaForScreenPoint(window, (int)screenPx.X, (int)screenPx.Y);
        var restore = window.RestoreBounds;
        var widthRatio = window.ActualWidth > 0 ? mouseInWindow.X / window.ActualWidth : 0.5;
        var width = restore.IsEmpty ? window.Width : Math.Min(restore.Width, wa.Width);
        var height = restore.IsEmpty ? window.Height : Math.Min(restore.Height, wa.Height);

        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        window.Left = cursorX - (width * widthRatio);
        window.Top = Math.Max(cursorY - mouseInWindow.Y, wa.Top);

        if (window.Left < wa.Left) window.Left = wa.Left;
        if (window.Left + window.Width > wa.Right) window.Left = wa.Right - window.Width;
        if (window.Top < wa.Top) window.Top = wa.Top;
    }

    /// <summary>Показать окно на мониторе владельца и вывести на передний план только при открытии.</summary>
    public static void ShowNearOwner(Window window)
    {
        WindowPlacementService.PlaceNearOwner(window);
        window.Show();
        BringToFrontTemporarily(window);
    }

    /// <summary>Кратко Topmost + Activate, чтобы окно не оставалось «поверх всех» постоянно.</summary>
    public static void BringToFrontTemporarily(Window window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            window.Focus();
            window.Topmost = true;
            window.Topmost = false;

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
            }
        }
        catch
        {
            // ignore focus steal failures
        }
    }

    public static bool TryActivate(Guid entityId)
    {
        if (!OpenWindows.TryGetValue(entityId, out var window)) return false;
        try
        {
            if (!window.IsLoaded) return false;
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            BringToFrontTemporarily(window);
            return true;
        }
        catch
        {
            OpenWindows.Remove(entityId);
            return false;
        }
    }

    public static bool TryActivate<T>(Guid entityId) where T : Window
    {
        if (!OpenWindows.TryGetValue(entityId, out var window) || window is not T) return false;
        return TryActivate(entityId);
    }

    public static void Register(Guid entityId, Window window)
    {
        OpenWindows[entityId] = window;
        window.Closed += (_, _) => OpenWindows.Remove(entityId);
    }

    public static void CloseRegistered(Guid entityId)
    {
        if (!OpenWindows.TryGetValue(entityId, out var window)) return;
        try
        {
            window.Close();
        }
        catch
        {
            // ignore
        }
    }

    public static bool ConfirmClose(Window owner, bool isDirty, Func<bool> saveAction)
    {
        if (!isDirty) return true;

        var result = System.Windows.MessageBox.Show(
            owner,
            "Сохранить изменения?",
            "WideS",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => saveAction(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    public static void HookConfirmClose(Window window, Func<bool> isDirty, Func<bool> saveAction)
    {
        window.Closing += (_, e) =>
        {
            if (!ConfirmClose(window, isDirty(), saveAction))
            {
                e.Cancel = true;
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
