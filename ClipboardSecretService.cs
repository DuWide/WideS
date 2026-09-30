using System.IO;
using System.Windows;
using System.Windows.Threading;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.DataObject;

namespace DevCockpit;

/// <summary>
/// Копирование паролей в буфер обмена: без попадания в журнал Win+V и облачный буфер,
/// с автоочисткой через 25 секунд (только если в буфере всё ещё этот пароль).
/// </summary>
public static class ClipboardSecretService
{
    private static readonly TimeSpan ClearDelay = TimeSpan.FromSeconds(25);
    private static DispatcherTimer? _timer;
    private static string? _pendingSecret;

    public static void CopySecret(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        var data = new WpfDataObject();
        data.SetText(secret, System.Windows.TextDataFormat.UnicodeText);
        // Форматы-маркеры, которые понимает Windows: не сохранять в журнал буфера и не синхронизировать в облако.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        WpfClipboard.SetDataObject(data, true);

        _pendingSecret = secret;
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ClearDelay };
            _timer.Tick += (_, _) => ClearIfUnchanged();
        }

        _timer.Stop();
        _timer.Start();
    }

    private static void ClearIfUnchanged()
    {
        _timer?.Stop();
        var secret = _pendingSecret;
        _pendingSecret = null;
        if (secret is null)
        {
            return;
        }

        try
        {
            if (WpfClipboard.ContainsText() && WpfClipboard.GetText() == secret)
            {
                WpfClipboard.Clear();
            }
        }
        catch
        {
            // Буфер занят другим приложением — не критично, пробуем не навязчиво.
        }
    }
}
