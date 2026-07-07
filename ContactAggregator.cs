using System.Text.RegularExpressions;

namespace DevCockpit;

public sealed class ContactSummary
{
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Key { get; init; } = "";
    public int TaskCount { get; init; }
    public int OpenTaskCount { get; init; }
    public DateTime? LastActivityAt { get; init; }
    public IReadOnlyList<Guid> TaskIds { get; init; } = [];
}

public static class ContactAggregator
{
    public static IReadOnlyList<ContactSummary> Build(IEnumerable<TaskItem> tasks, string? search = null)
    {
        var query = search?.Trim();
        var groups = tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.ContactName) || !string.IsNullOrWhiteSpace(t.ContactPhone))
            .GroupBy(GetContactKey)
            .Select(g =>
            {
                var sample = g.OrderByDescending(t => t.CreatedAt).First();
                var name = g.Select(t => t.ContactName.Trim())
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
                var phone = g.Select(t => t.ContactPhone.Trim())
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
                var lastAt = g.Max(t => (DateTime?)t.CreatedAt);
                return new ContactSummary
                {
                    Key = g.Key,
                    Name = name,
                    Phone = phone,
                    TaskCount = g.Count(),
                    OpenTaskCount = g.Count(t => !t.IsDone),
                    LastActivityAt = lastAt,
                    TaskIds = g.Select(t => t.Id).ToList()
                };
            })
            .OrderByDescending(c => c.LastActivityAt ?? DateTime.MinValue)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            return groups;
        }

        return groups
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || c.Phone.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static bool TaskMatchesContact(TaskItem task, ContactSummary contact)
    {
        return GetContactKey(task) == contact.Key;
    }

    public static bool MatchesKey(TaskItem task, string key)
    {
        return GetContactKey(task) == key;
    }

    private static string GetContactKey(TaskItem task)
    {
        var phone = NormalizePhone(task.ContactPhone);
        if (!string.IsNullOrWhiteSpace(phone))
        {
            return "phone:" + phone;
        }

        var name = task.ContactName.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return "name:" + name.ToLowerInvariant();
        }

        return "empty";
    }

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "";
        }

        return Regex.Replace(phone, @"\D", "");
    }
}
