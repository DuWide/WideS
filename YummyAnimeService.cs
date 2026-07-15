using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevCockpit;

public sealed class YummyAnimeService : IDisposable
{
    private const string ApiBaseUrl = "https://api.yani.tv";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string UserToken { get; private set; }

    public void ClearUserToken() => UserToken = "";

    public YummyAnimeService(string applicationToken, string userToken = "")
    {
        if (string.IsNullOrWhiteSpace(applicationToken))
        {
            throw new ArgumentException("Не указан X-Application token.", nameof(applicationToken));
        }

        UserToken = userToken;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(25)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Application", applicationToken);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Lang", "ru");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json,image/avif,image/webp");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WideS/1.6");
    }

    public async Task<IReadOnlyList<YummyAnimeItem>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var route = string.IsNullOrWhiteSpace(query)
            ? "/anime?limit=30&offset=0"
            : $"/search?q={Uri.EscapeDataString(query.Trim())}&limit=30&offset=0";
        var result = await SendAsync<YummyAnimeSearchResponse>(
            HttpMethod.Get,
            route,
            null,
            requiresUser: false,
            cancellationToken);
        return result.Response;
    }

    public async Task<IReadOnlyList<YummyAnimeVideo>> GetVideosAsync(
        int animeId,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<YummyAnimeVideosResponse>(
            HttpMethod.Get,
            $"/anime/{animeId}/videos",
            null,
            requiresUser: false,
            cancellationToken);
        return result.Response
            .Where(video => video.GetPlayerUri() is not null)
            .OrderBy(video => video.Index)
            .ThenBy(video => video.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            need_json = true,
            login = email.Trim(),
            password,
            recaptcha_response = ""
        };
        using var result = await SendRawAsync(
            HttpMethod.Post,
            "/profile/login",
            body,
            requiresUser: false,
            cancellationToken);
        var token = ExtractToken(result.RootElement);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new YummyAnimeException("YummyAnime не подтвердил вход.");
        }

        UserToken = token;
        return UserToken;
    }

    public async Task<string> RefreshUserTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserToken)) return "";
        using var result = await SendRawAsync(
            HttpMethod.Get,
            "/profile/token",
            null,
            requiresUser: true,
            cancellationToken);
        var token = ExtractToken(result.RootElement);
        if (!string.IsNullOrWhiteSpace(token))
        {
            UserToken = token;
        }
        return UserToken;
    }

    public async Task MarkWatchedAsync(
        YummyAnimeVideo video,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserToken)) return;
        var body = new
        {
            time = 0,
            duration = Math.Max(0, (int)video.Duration),
            date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            times = Array.Empty<int>()
        };
        using var _ = await SendRawAsync(
            HttpMethod.Put,
            $"/video/{video.VideoId}",
            body,
            requiresUser: true,
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string route,
        object? body,
        bool requiresUser,
        CancellationToken cancellationToken)
    {
        using var document = await SendRawAsync(
            method,
            route,
            body,
            requiresUser,
            cancellationToken);
        try
        {
            return document.RootElement.Deserialize<T>(_jsonOptions)
                   ?? throw new YummyAnimeException("YummyAnime вернул пустой ответ.");
        }
        catch (JsonException ex)
        {
            throw new YummyAnimeException("Не удалось прочитать ответ YummyAnime.", ex);
        }
    }

    private async Task<JsonDocument> SendRawAsync(
        HttpMethod method,
        string route,
        object? body,
        bool requiresUser,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (requiresUser)
        {
            if (string.IsNullOrWhiteSpace(UserToken))
            {
                throw new YummyAnimeException("Сначала войдите в аккаунт YummyAnime.");
            }
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", UserToken);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new YummyAnimeException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    requiresUser ? "Авторизация YummyAnime истекла. Войдите заново." : "YummyAnime отклонил X-Application token.",
                (HttpStatusCode)420 =>
                    "YummyAnime запросил captcha. Выполните вход на сайте и повторите позже.",
                _ => $"YummyAnime API вернул {(int)response.StatusCode}: {response.ReasonPhrase}"
            });
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new YummyAnimeException("Не удалось прочитать ответ YummyAnime.", ex);
        }
    }

    private static string ExtractToken(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? "";
            return value.Count(character => character == '.') >= 2 ? value : "";
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("token", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString() ?? "";
                }
                var nested = ExtractToken(property.Value);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractToken(item);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return "";
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class YummyAnimeException : Exception
{
    public YummyAnimeException(string message) : base(message)
    {
    }

    public YummyAnimeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class YummyAnimeSearchResponse
{
    [JsonPropertyName("response")]
    public List<YummyAnimeItem> Response { get; set; } = [];
}

public sealed class YummyAnimeItem
{
    [JsonPropertyName("anime_id")]
    public int AnimeId { get; set; }

    [JsonPropertyName("anime_url")]
    public string Alias { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("season")]
    public int? Season { get; set; }

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (Year is > 0) parts.Add(Year.Value.ToString());
            if (Season is > 0) parts.Add($"сезон {Season}");
            return parts.Count == 0 ? "YummyAnime" : string.Join(" · ", parts);
        }
    }
}

public sealed class YummyAnimeVideosResponse
{
    [JsonPropertyName("response")]
    public List<YummyAnimeVideo> Response { get; set; } = [];
}

public sealed class YummyAnimeVideo
{
    [JsonPropertyName("video_id")]
    public int VideoId { get; set; }

    [JsonPropertyName("iframe_url")]
    public string IframeUrl { get; set; } = "";

    [JsonPropertyName("data")]
    public YummyAnimeVideoData Data { get; set; } = new();

    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("index")]
    public double Index { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    public string Dubbing => string.IsNullOrWhiteSpace(Data.Dubbing) ? "Без указания" : Data.Dubbing;
    public string Player => string.IsNullOrWhiteSpace(Data.Player) ? "Плеер" : Data.Player;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Number) ? "Серия" : $"Серия {Number}";

    public Uri? GetPlayerUri()
    {
        var value = IframeUrl.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }
}

public sealed class YummyAnimeVideoData
{
    [JsonPropertyName("dubbing")]
    public string Dubbing { get; set; } = "";

    [JsonPropertyName("player")]
    public string Player { get; set; } = "";

    [JsonPropertyName("player_id")]
    public int PlayerId { get; set; }
}

