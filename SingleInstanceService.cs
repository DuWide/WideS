using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace DevCockpit;

public static class SingleInstanceService
{
    private const string MutexName = "WideS.DevCockpit.SingleInstance.v1";
    private const string PipeName = "WideS.DevCockpit.Activate";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        return createdNew;
    }

    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // ignore
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    public static bool NotifyExistingInstance(string? payload = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(payload ?? "activate");
            return true;
        }
        catch
        {
            return ActivateExistingProcessWindow();
        }
    }

    public static void StartActivationListener(Window window, Action<string?> onActivate)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var payload = await reader.ReadLineAsync();
                    window.Dispatcher.Invoke(() => onActivate(payload));
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    private static bool ActivateExistingProcessWindow()
    {
        var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id == current.Id) continue;
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero) continue;
            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
            return true;
        }

        return false;
    }

    public static void ActivateWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
