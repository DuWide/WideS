using System.Security.Cryptography;
using System.Text;

namespace DevCockpit;

internal static class RdpHelper
{
    private static readonly byte[] PasswordEntropy = [0x01, 0x00, 0x00, 0x00];
    private static readonly UnicodeEncoding RdpEncoding = new(false, false);

    public static (string Host, int Port) ParseAddress(string address)
    {
        address = address.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            return ("", 3389);
        }

        var host = address;
        var port = 3389;
        if (address.Contains(':'))
        {
            var parts = address.Split(':', 2);
            host = parts[0].Trim();
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }
        }

        return (host, port);
    }

    public static (string Username, string Domain) SplitLogin(string login)
    {
        login = login.Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            return ("", "");
        }

        var slash = login.IndexOf('\\');
        if (slash > 0)
        {
            return (login[(slash + 1)..], login[..slash]);
        }

        var at = login.IndexOf('@');
        if (at > 0)
        {
            return (login[..at], login[(at + 1)..]);
        }

        return (login, "");
    }

    public static string EncodePassword(string password)
    {
        var data = Encoding.Unicode.GetBytes(password + "\0");
        if (data.Length % 2 != 0)
        {
            data = data.Concat(new byte[] { 0, 0 }).ToArray();
        }

        var protectedBytes = ProtectedData.Protect(data, PasswordEntropy, DataProtectionScope.CurrentUser);
        for (var i = 0; i + 1 < protectedBytes.Length; i += 2)
        {
            (protectedBytes[i], protectedBytes[i + 1]) = (protectedBytes[i + 1], protectedBytes[i]);
        }

        return Convert.ToBase64String(protectedBytes);
    }

    public static string GetRdpFilePath(Guid connectionId)
    {
        var dir = Path.Combine(AppPaths.DataDirectory, "rdp");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{connectionId:N}.rdp");
    }

    public static void WriteRdpFile(string path, string host, int port, string login, string? password)
    {
        var (username, domain) = SplitLogin(login);
        var lines = new List<string>
        {
            "screen mode id:i:2",
            "use multimon:i:0",
            "session bpp:i:32",
            "compression:i:1",
            "keyboardhook:i:2",
            "displayconnectionbar:i:1",
            "disable wallpaper:i:1",
            "disable full window drag:i:1",
            "allow desktop composition:i:0",
            "allow font smoothing:i:0",
            "disable menu anims:i:1",
            "bitmapcachepersistenable:i:1",
            "autoreconnection enabled:i:1",
            "authentication level:i:2",
            "prompt for credentials:i:0",
            "negotiate security layer:i:1",
            "enablecredsspsupport:i:1",
            $"full address:s:{host}"
        };

        if (port != 3389)
        {
            lines.Add($"server port:i:{port}");
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            lines.Add($"domain:s:{domain}");
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            lines.Add($"username:s:{username}");
        }
        else if (!string.IsNullOrWhiteSpace(login))
        {
            lines.Add($"username:s:{login}");
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            lines.Add($"password 46:b:{EncodePassword(password)}");
        }

        var content = string.Join("\r\n", lines) + "\r\n";
        File.WriteAllText(path, content, RdpEncoding);
    }

    public static void WriteRdpFileWithoutPassword(string path, string host, int port, string login)
    {
        WriteRdpFile(path, host, port, login, null);
    }

    public static void DeleteRdpFile(Guid connectionId)
    {
        var path = GetRdpFilePath(connectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
