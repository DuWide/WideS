using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DevCockpit;

/// <summary>
/// Читает информацию о текущем воспроизводимом медиа (браузер, Spotify и т.п.)
/// через системный менеджер сеансов Windows и позволяет управлять плеером.
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public sealed record Snapshot(bool HasSession, string Title, string Artist, bool IsPlaying, string Source, byte[]? AlbumArt);

    private GlobalSystemMediaTransportControlsSession? Current
        => _manager?.GetCurrentSession();

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            _manager = null;
        }
    }

    public async Task<Snapshot> GetSnapshotAsync()
    {
        try
        {
            var session = Current;
            if (session is null)
            {
                return new Snapshot(false, string.Empty, string.Empty, false, string.Empty, null);
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var title = props?.Title ?? string.Empty;
            var artist = props?.Artist ?? string.Empty;
            if (string.IsNullOrWhiteSpace(artist))
            {
                artist = props?.AlbumTitle ?? string.Empty;
            }

            var source = FriendlySource(session.SourceAppUserModelId);
            var albumArt = await ReadThumbnailAsync(props?.Thumbnail);
            return new Snapshot(!string.IsNullOrWhiteSpace(title), title, artist, isPlaying, source, albumArt);
        }
        catch
        {
            return new Snapshot(false, string.Empty, string.Empty, false, string.Empty, null);
        }
    }

    public Task TogglePlayPauseAsync() => TryControlAsync(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => TryControlAsync(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => TryControlAsync(s => s.TrySkipPreviousAsync());

    private async Task TryControlAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
    {
        try
        {
            var session = Current;
            if (session is not null)
            {
                await action(session);
            }
        }
        catch
        {
            // управление недоступно для данного источника — игнорируем
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0) return null;
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static string FriendlySource(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return string.Empty;
        var lower = appId.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("yandex")) return "Yandex";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("brave")) return "Brave";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("aimp")) return "AIMP";
        if (lower.Contains("zen")) return "Zen";
        return string.Empty;
    }
}
