using System.Diagnostics;
using System.Windows.Automation;
using WpfClipboard = System.Windows.Clipboard;

namespace DevCockpit;

public static class ConnectionService
{
    public static string Connect(ConnectionItem connection, AppSettingsData settings)
    {
        if (connection.Type.Equals("RDP", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectRdp(connection);
        }

        if (connection.Type.Equals("AnyDesk", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectAnyDesk(connection, settings);
        }

        WpfClipboard.SetText(connection.Address);
        return "Адрес подключения скопирован в буфер.";
    }

    private static string ConnectRdp(ConnectionItem connection)
    {
        var address = connection.Address.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            return "RDP адрес пуст.";
        }

        var (host, port) = RdpHelper.ParseAddress(address);
        if (string.IsNullOrWhiteSpace(host))
        {
            return "RDP адрес пуст.";
        }

        var login = connection.Login.Trim();
        var password = SecretService.Unprotect(connection.EncryptedPassword);

        try
        {
            var rdpPath = RdpHelper.GetRdpFilePath(connection.Id);

            if (!string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(password))
            {
                SaveRdpCredentials(host, login, password);
                RdpHelper.WriteRdpFileWithoutPassword(rdpPath, host, port, login);
                LaunchMstsc(rdpPath);
                return $"RDP: {host}:{port} ({login})";
            }

            if (!string.IsNullOrWhiteSpace(login))
            {
                RdpHelper.WriteRdpFileWithoutPassword(rdpPath, host, port, login);
                LaunchMstsc(rdpPath);
                return $"RDP: {host}:{port} ({login}, без пароля)";
            }

            LaunchMstsc($"/v:{host}:{port}");
            return $"Запущен mstsc для {host}:{port}.";
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(password))
            {
                WpfClipboard.SetText(password);
            }

            throw new InvalidOperationException($"RDP не удалось: {ex.Message}. Пароль скопирован в буфер.", ex);
        }
    }

    private static void LaunchMstsc(string argument)
    {
        var isFile = argument.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = isFile ? $"\"{argument}\"" : argument,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("mstsc.exe не запустился.");
        WindowPlacementService.MoveProcessToPrimaryAsync(process, "mstsc");
    }

    public static string DeleteRdpCredentials(ConnectionItem connection)
    {
        var address = connection.Address.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            return "RDP адрес пуст.";
        }

        var (host, _) = RdpHelper.ParseAddress(address);
        RdpHelper.DeleteRdpFile(connection.Id);

        var result = RunCmdKey($"/delete:TERMSRV/{host}");
        return result.ExitCode == 0
            ? $"Удалены RDP-данные для TERMSRV/{host}."
            : $"Не удалось удалить RDP-данные для TERMSRV/{host}: {result.Output}";
    }

    private static void SaveRdpCredentials(string host, string login, string password)
    {
        var result = RunCmdKey($"/generic:TERMSRV/{host} /user:{QuoteCmdKey(login)} /pass:{QuoteCmdKey(password)}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"cmdkey failed for TERMSRV/{host}: {result.Output}");
        }
    }

    private static string ConnectAnyDesk(ConnectionItem connection, AppSettingsData settings)
    {
        var anyDesk = ResolveAnyDesk(settings.AnyDeskPath);
        var password = SecretService.Unprotect(connection.EncryptedPassword);
        WpfClipboard.SetText(string.IsNullOrWhiteSpace(password) ? connection.Address : password);

        if (string.IsNullOrWhiteSpace(anyDesk))
        {
            return string.IsNullOrWhiteSpace(password)
                ? "AnyDesk.exe не найден. ID скопирован в буфер."
                : "AnyDesk.exe не найден. Пароль скопирован в буфер.";
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = anyDesk,
                Arguments = connection.Address,
                UseShellExecute = false
            });
            WindowPlacementService.MoveProcessToPrimaryAsync(process, "AnyDesk");
            TryEnableAnyDeskAutoLoginAsync();
            return string.IsNullOrWhiteSpace(password)
                ? $"Запущен AnyDesk для {connection.Address}. ID скопирован в буфер."
                : $"Запущен AnyDesk для {connection.Address}. Пароль скопирован в буфер для вставки.";
        }
        catch
        {
            var process = Process.Start(new ProcessStartInfo { FileName = anyDesk, UseShellExecute = true });
            WindowPlacementService.MoveProcessToPrimaryAsync(process, "AnyDesk");
            TryEnableAnyDeskAutoLoginAsync();
            return string.IsNullOrWhiteSpace(password)
                ? "AnyDesk открыт, ID скопирован в буфер."
                : "AnyDesk открыт, пароль скопирован в буфер для вставки.";
        }
    }

    private static (int ExitCode, string Output) RunCmdKey(string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "cmdkey.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, string.Join(" ", new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim());
    }

    private static string QuoteCmdKey(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void TryEnableAnyDeskAutoLoginAsync()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(500);
                if (TryEnableAnyDeskAutoLogin())
                {
                    return;
                }
            }
        });
    }

    private static bool TryEnableAnyDeskAutoLogin()
    {
        try
        {
            var desktop = AutomationElement.RootElement;
            var anyDeskWindows = desktop.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

            foreach (AutomationElement window in anyDeskWindows)
            {
                var name = window.Current.Name ?? "";
                if (!name.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var checkboxes = window.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));

                foreach (AutomationElement checkbox in checkboxes)
                {
                    var checkboxName = checkbox.Current.Name ?? "";
                    if (!IsAnyDeskAutoLoginText(checkboxName))
                    {
                        continue;
                    }

                    if (checkbox.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) &&
                        pattern is TogglePattern toggle &&
                        toggle.Current.ToggleState != ToggleState.On)
                    {
                        toggle.Toggle();
                    }
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsAnyDeskAutoLoginText(string text)
    {
        return text.Contains("автомат", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("auto", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("remember", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("unattended", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveAnyDesk(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnyDesk", "AnyDesk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AnyDesk", "AnyDesk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AnyDesk", "AnyDesk.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }
}
