using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;

namespace DevCockpit;

public static class WindowPlacementService
{
    private const int SwRestore = 9;

    public static void PlaceOnPrimary(Window window)
    {
        PlaceWindow(window, Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]);
    }

    public static void PlaceOnSecondary(Window window)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary) ??
                     Forms.Screen.PrimaryScreen ??
                     Forms.Screen.AllScreens[0];
        PlaceWindow(window, screen);
    }

    public static void MoveProcessToPrimaryAsync(Process? process, params string[] processNames)
    {
        MoveProcessWindowAsync(process, Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0], processNames);
    }

    public static void MoveProcessToSecondaryAsync(Process? process, params string[] processNames)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary) ??
                     Forms.Screen.PrimaryScreen ??
                     Forms.Screen.AllScreens[0];
        MoveProcessWindowAsync(process, screen, processNames);
    }

    private static void PlaceWindow(Window window, Forms.Screen screen)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = screen.WorkingArea.Left + 40;
        window.Top = screen.WorkingArea.Top + 40;
    }

    private static void MoveProcessWindowAsync(Process? process, Forms.Screen screen, params string[] processNames)
    {
        if (process is null && processNames.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(350);
                var handle = FindWindowHandle(process, processNames);
                if (handle == IntPtr.Zero) continue;

                ShowWindow(handle, SwRestore);
                MoveWindow(handle, screen.WorkingArea.Left + 30, screen.WorkingArea.Top + 30,
                    Math.Min(1400, screen.WorkingArea.Width - 60),
                    Math.Min(900, screen.WorkingArea.Height - 60),
                    true);
                return;
            }
        });
    }

    private static IntPtr FindWindowHandle(Process? process, string[] processNames)
    {
        try
        {
            if (process is not null)
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero && IsAllowedWindow(process.MainWindowHandle, process))
                {
                    return process.MainWindowHandle;
                }
            }
        }
        catch
        {
            // Process may have exited or be owned by another launcher.
        }

        if (process is not null)
        {
            return IntPtr.Zero;
        }

        foreach (var name in processNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                foreach (var candidate in Process.GetProcessesByName("explorer"))
                {
                    if (candidate.MainWindowHandle == IntPtr.Zero) continue;
                    if (!IsAllowedWindow(candidate.MainWindowHandle, candidate)) continue;
                    return candidate.MainWindowHandle;
                }
            }
            catch
            {
                // Ignore processes that cannot be inspected.
            }
        }

        foreach (var name in processNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                {
                    if (candidate.MainWindowHandle != IntPtr.Zero)
                    {
                        return candidate.MainWindowHandle;
                    }
                }
            }
            catch
            {
                // Ignore processes that cannot be inspected.
            }
        }

        return IntPtr.Zero;
    }

    private static bool IsAllowedWindow(IntPtr handle, Process process)
    {
        var className = GetWindowClassName(handle);
        if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            return className is "CabinetWClass" or "ExploreWClass";
        }

        return !string.IsNullOrWhiteSpace(className);
    }

    private static string GetWindowClassName(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
