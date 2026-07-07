using System.Windows;

namespace DevCockpit;

public static class EditorWindowHelper
{
    private static readonly Dictionary<Guid, Window> OpenWindows = new();

    public static void MinimizeWindow(Window window) => window.WindowState = WindowState.Minimized;

    public static void TitleBar_MouseLeftButtonDown(Window window, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        window.DragMove();
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
            window.Activate();
            window.Focus();
            return true;
        }
        catch
        {
            OpenWindows.Remove(entityId);
            return false;
        }
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
}
