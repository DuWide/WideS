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

    public async Task AddFavoriteAsync(int animeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserToken)) return;
        await SendRawAsync(
            HttpMethod.Put,
            $"/anime/{animeId}/list/fav",
            new { date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            requiresUser: true,
            cancellationToken);
    }

    public async Task RemoveFavoriteAsync(int animeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserToken)) return;
        await SendRawAsync(
            HttpMethod.Delete,
            $"/anime/{animeId}/list/fav",
            null,
            requiresUser: true,
            cancellationToken);
    }

    public async Task MarkWatchedAsync(
        YummyAnimeVideo video,
        int progressSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserToken)) return;
        var duration = Math.Max(0, (int)video.Duration);
        var time = Math.Clamp(progressSeconds, 0, duration > 0 ? duration : progressSeconds);
        var body = new
        {
            time,
            duration,
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

    public async Task<HashSet<int>> GetWatchedVideoIdsAsync(CancellationToken cancellationToken = default)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(UserToken)) return result;

        foreach (var route in new[] { "/video/watch-history", "/video/last-watches" })
        {
            try
            {
                using var document = await SendRawAsync(
                    HttpMethod.Get,
                    route,
                    null,
                    requiresUser: true,
                    cancellationToken);
                CollectVideoIds(document.RootElement, result);
            }
            catch (YummyAnimeException)
            {
                // История опциональна: локальные отметки всё равно работают.
            }
        }

        return result;
    }

    private static void CollectVideoIds(JsonElement element, HashSet<int> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if ((property.NameEquals("video_id") || property.NameEquals("id")) &&
                        property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var id) &&
                        id > 0)
                    {
                        result.Add(id);
                    }
                    CollectVideoIds(property.Value, result);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectVideoIds(item, result);
                }
                break;
        }
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

    [JsonPropertyName("poster")]
    public YummyAnimePoster Poster { get; set; } = new();

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

    public string PosterUrl => YummyAnimePoster.Normalize(Poster.Medium, Poster.Big, Poster.Full);

    public YummyAnimeFavorite ToFavorite() => new()
    {
        AnimeId = AnimeId,
        Title = Title,
        Subtitle = Subtitle,
        PosterUrl = PosterUrl
    };
}

public sealed class YummyAnimePoster
{
    [JsonPropertyName("medium")]
    public string Medium { get; set; } = "";

    [JsonPropertyName("big")]
    public string Big { get; set; } = "";

    [JsonPropertyName("full")]
    public string Full { get; set; } = "";

    public static string Normalize(params string[] values)
    {
        foreach (var value in values)
        {
            var normalized = NormalizeOne(value);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        }
        return "";
    }

    private static string NormalizeOne(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.StartsWith("//", StringComparison.Ordinal)) return "https:" + value;
        if (value.StartsWith("/", StringComparison.Ordinal)) return "https://api.yani.tv" + value;
        return value;
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
    public bool IsWatched { get; set; }
    public string ListTitle => IsWatched ? $"✓ {DisplayTitle}" : DisplayTitle;

    public override string ToString() => ListTitle;

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

