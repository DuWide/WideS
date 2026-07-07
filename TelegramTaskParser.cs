using System.Globalization;
using System.Text.RegularExpressions;

namespace DevCockpit;

public sealed class TelegramParsedTask
{
    public string ExternalId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime StartAt { get; init; }
    public string ContactName { get; init; } = "";
    public string ContactPhone { get; init; } = "";
}

public static class TelegramTaskParser
{
    private static readonly Regex HeaderRegex = new(
        @"Новая\s+задача\s+для\s+исполнителя:\s*(\d+)\s+от\s+([\d.]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CustomerRegex = new(
        @"\*?\s*Заказчик:\s*(.+?)\s*\*?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DescriptionRegex = new(
        @"\*?\s*1\.\s*(.+?)\s*\*?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StartRegex = new(
        @"Начало\s+работы:\s*([\d.\s:]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StatusRegex = new(
        @"Состояние:\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ContactRegex = new(
        @"\*?\s*Контакт:\s*(.+?)(?:\s*тел:\s*([+\d\s()-]+))?\s*\*?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static TelegramParsedTask? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!text.Contains("Новая задача для исполнителя", StringComparison.OrdinalIgnoreCase)) return null;

        var header = HeaderRegex.Match(text);
        if (!header.Success) return null;

        var status = StatusRegex.Match(text);
        if (!status.Success) return null;
        if (!status.Groups[1].Value.Trim().Equals("В работе", StringComparison.OrdinalIgnoreCase)) return null;

        var customer = CustomerRegex.Match(text);
        if (!customer.Success) return null;

        var description = DescriptionRegex.Match(text);
        var start = StartRegex.Match(text);
        var contact = ContactRegex.Match(text);

        var startAt = DateTime.Now;
        if (start.Success)
        {
            var raw = start.Groups[1].Value.Trim();
            if (!DateTime.TryParseExact(raw, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out startAt) &&
                !DateTime.TryParseExact(raw, "dd.MM.yyyy H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out startAt))
            {
                DateTime.TryParse(raw, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out startAt);
            }
        }

        var title = customer.Groups[1].Value.Trim().Trim('"');
        var contactName = contact.Success ? contact.Groups[1].Value.Trim() : "";
        var phone = contact.Success ? contact.Groups[2].Value.Trim() : "";
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            var phoneIndex = contactName.IndexOf("тел:", StringComparison.OrdinalIgnoreCase);
            if (phoneIndex >= 0)
            {
                contactName = contactName[..phoneIndex].Trim();
            }
        }

        return new TelegramParsedTask
        {
            ExternalId = $"{header.Groups[1].Value.Trim()} от {header.Groups[2].Value.Trim()}",
            Title = title,
            Description = description.Success ? description.Groups[1].Value.Trim() : "",
            StartAt = startAt,
            ContactName = contactName,
            ContactPhone = phone
        };
    }
}
