using System.Text.RegularExpressions;

namespace DevCockpit;

public static class NoteConnectionDetector
{
    private static readonly Regex CompactAnyDeskLine = new(
        @"^(?<name>.+?)\s+(?<id>\d{9,10})\s+(?<password>\S+)(?:\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3}))?\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnyDeskLine = new(@"(?:anydesk|анидеск|any\s*desk|энидеск|эники)\s*[:#-]?\s*(\d[\d\s]{8,12}\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyDeskId = new(@"\b(\d{9,10})\b", RegexOptions.CultureInvariant);
    private static readonly Regex AddressLine = new(@"(?:адрес|address|host|сервер|server|rdp|ip)\s*[:#-]?\s*([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LoginLine = new(@"(?:логин|login|user(?:name)?|пользователь)\s*[:#-]?\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PasswordLine = new(@"(?:пароль|password|pass|pwd)\s*[:#-]?\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IpAddress = new(@"\b(\d{1,3}(?:\.\d{1,3}){3})(?::(\d{1,5}))?\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex HeaderLine = new(@"^(?:эники|anydesk|анидеск|энидеск)\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ConnectionItem> Detect(string text, string defaultName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var results = new List<ConnectionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || HeaderLine.IsMatch(line))
            {
                continue;
            }

            if (!TryParseCompactAnyDeskLine(line, out var compact))
            {
                continue;
            }

            var key = $"{compact.Address}|{compact.Name}";
            if (seen.Add(key))
            {
                results.Add(compact);
            }
        }

        if (results.Count > 0)
        {
            return results;
        }

        foreach (var block in SplitBlocks(text))
        {
            var item = ParseBlock(block, defaultName, results.Count);
            if (item is null)
            {
                continue;
            }

            var key = $"{item.Address}|{item.Name}";
            if (seen.Add(key))
            {
                results.Add(item);
            }
        }

        return results;
    }

    private static bool TryParseCompactAnyDeskLine(string line, out ConnectionItem item)
    {
        item = null!;
        var match = CompactAnyDeskLine.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"].Value.Trim();
        var id = match.Groups["id"].Value;
        var password = match.Groups["password"].Value.Trim();
        var ip = match.Groups["ip"].Success ? match.Groups["ip"].Value : "";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        item = new ConnectionItem
        {
            Name = name.Length > 64 ? name[..64] : name,
            Type = "AnyDesk",
            Address = id,
            Login = "",
            EncryptedPassword = string.IsNullOrWhiteSpace(password) ? "" : SecretService.Protect(password),
            Comment = string.IsNullOrWhiteSpace(ip) ? "Создано из заметки" : $"IP: {ip}",
            CreatedAt = DateTime.Now
        };
        return true;
    }

    private static IEnumerable<string> SplitBlocks(string text)
    {
        var blocks = Regex.Split(text, @"\r?\n\s*[-—=]{3,}\s*\r?\n");
        if (blocks.Length > 1)
        {
            return blocks.Where(b => !string.IsNullOrWhiteSpace(b));
        }

        return text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ConnectionItem? ParseBlock(string block, string defaultName, int index)
    {
        var address = ExtractAddress(block);
        var login = ExtractLogin(block);
        var password = ExtractPassword(block);
        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(login) && string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var type = DetectType(block, address ?? "");
        address = NormalizeAddress(address ?? "", type);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var name = ExtractName(block) ?? $"{defaultName} · {address}";
        if (index > 0)
        {
            name = $"{name} ({index + 1})";
        }

        return new ConnectionItem
        {
            Name = name,
            Type = type,
            Address = address,
            Login = login ?? "",
            EncryptedPassword = string.IsNullOrWhiteSpace(password) ? "" : SecretService.Protect(password),
            Comment = "Создано из заметки",
            CreatedAt = DateTime.Now
        };
    }

    private static string DetectType(string block, string address)
    {
        if (AnyDeskLine.IsMatch(block) || AnyDeskId.IsMatch(address))
        {
            return "AnyDesk";
        }

        if (block.Contains("rdp", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("mstsc", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("3389", StringComparison.OrdinalIgnoreCase))
        {
            return "RDP";
        }

        return IpAddress.IsMatch(address) ? "RDP" : "AnyDesk";
    }

    private static string NormalizeAddress(string address, string type)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "";
        }

        address = address.Trim().Trim('"', '\'');
        if (type == "AnyDesk")
        {
            var digits = new string(address.Where(char.IsDigit).ToArray());
            return digits.Length is >= 9 and <= 10 ? digits : address;
        }

        return address;
    }

    private static string? ExtractAddress(string block)
    {
        var line = AddressLine.Match(block);
        if (line.Success)
        {
            return line.Groups[1].Value.Trim();
        }

        var anyDesk = AnyDeskLine.Match(block);
        if (anyDesk.Success)
        {
            return new string(anyDesk.Groups[1].Value.Where(char.IsDigit).ToArray());
        }

        var ip = IpAddress.Match(block);
        if (ip.Success)
        {
            return ip.Groups[2].Success ? $"{ip.Groups[1].Value}:{ip.Groups[2].Value}" : ip.Groups[1].Value;
        }

        var id = AnyDeskId.Match(block);
        return id.Success ? id.Groups[1].Value : null;
    }

    private static string? ExtractLogin(string block)
    {
        var match = LoginLine.Match(block);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractPassword(string block)
    {
        var match = PasswordLine.Match(block);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractName(string block)
    {
        var firstLine = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        if (LoginLine.IsMatch(firstLine) || PasswordLine.IsMatch(firstLine) || AddressLine.IsMatch(firstLine))
        {
            return null;
        }

        if (CompactAnyDeskLine.IsMatch(firstLine))
        {
            return CompactAnyDeskLine.Match(firstLine).Groups["name"].Value.Trim();
        }

        return firstLine.Length > 64 ? firstLine[..64] : firstLine;
    }
}
