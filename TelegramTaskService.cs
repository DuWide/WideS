using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DevCockpit;

public sealed class TelegramPollResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long LastUpdateId { get; init; }
    public List<TelegramIncomingTask> Tasks { get; init; } = [];
}

public sealed class TelegramIncomingTask
{
    public string TelegramKey { get; init; } = "";
    public TelegramParsedTask Parsed { get; init; } = new();
}

public sealed class TelegramTaskService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static TelegramPollResult Empty { get; } = new() { Success = true };

    public async Task<TelegramPollResult> PollAsync(AppSettingsData settings, CancellationToken cancellationToken = default)
    {
        if (!settings.TelegramEnabled)
        {
            return Empty;
        }

        var token = SecretService.Unprotect(settings.TelegramBotTokenEncrypted);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TelegramPollResult { Success = false, Error = "Telegram token не задан." };
        }

        try
        {
            if (settings.TelegramBotId > 0)
            {
                var meUrl = $"https://api.telegram.org/bot{token}/getMe";
                using var meResponse = await Http.GetAsync(meUrl, cancellationToken);
                var meJson = await meResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!TryReadBotId(meJson, out var botId) || botId != settings.TelegramBotId)
                {
                    return new TelegramPollResult
                    {
                        Success = false,
                        Error = $"Bot ID не совпадает. Ожидается {settings.TelegramBotId}."
                    };
                }
            }

            var offset = settings.TelegramLastUpdateId + 1;
            var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout=0&allowed_updates=%5B%22message%22%5D";
            using var response = await Http.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseUpdates(json, settings.TelegramLastUpdateId);
        }
        catch (Exception ex)
        {
            return new TelegramPollResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<TelegramPollResult> ImportDesktopExportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new TelegramPollResult { Success = false, Error = "Не выбран result.json из Telegram Desktop." };
        }

        if (!File.Exists(path))
        {
            return new TelegramPollResult { Success = false, Error = $"Файл экспорта не найден: {path}" };
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var tasks = new List<TelegramIncomingTask>();
            ReadDesktopExport(doc.RootElement, tasks);
            return new TelegramPollResult { Success = true, Tasks = tasks };
        }
        catch (JsonException ex)
        {
            return new TelegramPollResult { Success = false, Error = $"Некорректный JSON экспорта: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new TelegramPollResult { Success = false, Error = ex.Message };
        }
    }

    private static void ReadDesktopExport(JsonElement root, List<TelegramIncomingTask> tasks)
    {
        if (root.ValueKind != JsonValueKind.Object) return;

        if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            ReadDesktopChat(root, "chat", tasks);
        }

        if (root.TryGetProperty("chats", out var chats) &&
            chats.ValueKind == JsonValueKind.Object &&
            chats.TryGetProperty("list", out var list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (var chat in list.EnumerateArray())
            {
                ReadDesktopChat(chat, "chat", tasks);
            }
        }
    }

    private static void ReadDesktopChat(
        JsonElement chat,
        string fallbackChatKey,
        List<TelegramIncomingTask> tasks)
    {
        if (chat.ValueKind != JsonValueKind.Object ||
            !chat.TryGetProperty("messages", out var messages) ||
            messages.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var chatKey = ReadScalar(chat, "id");
        if (string.IsNullOrWhiteSpace(chatKey)) chatKey = ReadScalar(chat, "name");
        if (string.IsNullOrWhiteSpace(chatKey)) chatKey = fallbackChatKey;

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object) continue;
            if (message.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                !string.Equals(type.GetString(), "message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!message.TryGetProperty("text", out var textElement)) continue;
            var text = ExtractDesktopText(textElement);
            var parsed = TelegramTaskParser.TryParse(text);
            if (parsed is null) continue;

            var messageKey = ReadScalar(message, "id");
            if (string.IsNullOrWhiteSpace(messageKey))
            {
                messageKey = ReadScalar(message, "date_unixtime");
            }
            if (string.IsNullOrWhiteSpace(messageKey))
            {
                messageKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
            }

            tasks.Add(new TelegramIncomingTask
            {
                TelegramKey = $"desktop:{chatKey}:{messageKey}",
                Parsed = parsed
            });
        }
    }

    private static string ExtractDesktopText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString() ?? "";
        if (element.ValueKind != JsonValueKind.Array) return "";

        var builder = new StringBuilder();
        foreach (var part in element.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                builder.Append(part.GetString());
                continue;
            }

            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out var nestedText) &&
                nestedText.ValueKind == JsonValueKind.String)
            {
                builder.Append(nestedText.GetString());
            }
        }
        return builder.ToString();
    }

    private static string ReadScalar(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static TelegramPollResult ParseUpdates(string json, long previousLastUpdateId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : "Telegram API error";
            return new TelegramPollResult { Success = false, Error = description };
        }

        var lastUpdateId = previousLastUpdateId;
        var tasks = new List<TelegramIncomingTask>();
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return new TelegramPollResult { Success = true, LastUpdateId = lastUpdateId };
        }

        foreach (var update in result.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var updateIdElement))
            {
                lastUpdateId = Math.Max(lastUpdateId, updateIdElement.GetInt64());
            }

            if (!update.TryGetProperty("message", out var message)) continue;
            if (!message.TryGetProperty("text", out var textElement)) continue;

            var parsed = TelegramTaskParser.TryParse(textElement.GetString());
            if (parsed is null) continue;

            if (!message.TryGetProperty("chat", out var chat) ||
                !chat.TryGetProperty("id", out var chatIdElement) ||
                !message.TryGetProperty("message_id", out var messageIdElement))
            {
                continue;
            }

            tasks.Add(new TelegramIncomingTask
            {
                TelegramKey = $"{chatIdElement.GetInt64()}:{messageIdElement.GetInt32()}",
                Parsed = parsed
            });
        }

        return new TelegramPollResult
        {
            Success = true,
            LastUpdateId = lastUpdateId,
            Tasks = tasks
        };
    }

    private static bool TryReadBotId(string json, out long botId)
    {
        botId = 0;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return false;
        if (!root.TryGetProperty("result", out var result)) return false;
        if (!result.TryGetProperty("id", out var idElement)) return false;
        botId = idElement.GetInt64();
        return true;
    }
}
