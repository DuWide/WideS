using System.Net.Http;
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
