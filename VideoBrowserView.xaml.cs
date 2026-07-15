using System.Text.Encodings.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace DevCockpit;

public partial class VideoBrowserView : System.Windows.Controls.UserControl, IDisposable
{
    private enum CatalogMode
    {
        Catalog,
        Favorites,
        History
    }

    private readonly YummyAnimeService? _service;
    private readonly Action<string> _saveUserToken;
    private readonly List<YummyAnimeFavorite> _favorites;
    private readonly List<YummyAnimeWatchEntry> _watchHistory;
    private readonly Action _saveLocalData;
    private readonly HashSet<int> _watchedVideoIds = [];
    private Task? _initializationTask;
    private IReadOnlyList<YummyAnimeVideo> _videos = [];
    private YummyAnimeVideo? _currentVideo;
    private YummyAnimeItem? _selectedAnime;
    private YummyAnimeFavorite? _selectedFavorite;
    private CancellationTokenSource? _loadCancellation;
    private bool _updatingFilters;
    private CatalogMode _catalogMode = CatalogMode.Catalog;
    private bool _webMessageHooked;
    private bool _disposed;

    public bool IsMiniMode { get; private set; }
    public event Action<bool>? FullScreenChanged;
    public event Action? DragRequested;

    public VideoBrowserView(
        string applicationToken,
        string userToken,
        Action<string> saveUserToken,
        List<YummyAnimeFavorite> favorites,
        List<YummyAnimeWatchEntry> watchHistory,
        Action saveLocalData)
    {
        InitializeComponent();
        _saveUserToken = saveUserToken;
        _favorites = favorites;
        _watchHistory = watchHistory;
        _saveLocalData = saveLocalData;
        foreach (var entry in _watchHistory)
        {
            if (entry.VideoId > 0) _watchedVideoIds.Add(entry.VideoId);
        }

        if (!string.IsNullOrWhiteSpace(applicationToken))
        {
            _service = new YummyAnimeService(applicationToken, userToken);
        }
        else
        {
            CatalogStatus.Text = "Укажите публичный X-Application token в настройках WideS.";
            SearchBox.IsEnabled = false;
            CatalogTabButton.IsEnabled = false;
            FavoritesTabButton.IsEnabled = false;
            HistoryTabButton.IsEnabled = false;
            SyncTabButton.IsEnabled = false;
        }

        UpdateLoginStatus();
        UpdateCatalogTabStyles();
        Loaded += async (_, _) => await EnsureInitializedAsync();
    }

    private Task EnsureInitializedAsync() =>
        _initializationTask ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        await InitializeBrowserAsync();
        if (_service is null)
        {
            RefreshCatalog();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_service.UserToken))
        {
            try
            {
                var refreshedToken = await _service.RefreshUserTokenAsync();
                if (!string.IsNullOrWhiteSpace(refreshedToken)) _saveUserToken(refreshedToken);
                var remoteWatched = await _service.GetWatchedVideoIdsAsync();
                foreach (var id in remoteWatched) _watchedVideoIds.Add(id);
            }
            catch (YummyAnimeException)
            {
                _service.ClearUserToken();
                _saveUserToken("");
            }
            UpdateLoginStatus();
        }

        RefreshCatalog();
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.VideoWebViewDataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: AppPaths.VideoWebViewDataDirectory);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            Browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.NavigationCompleted += async (_, args) =>
            {
                PlayerStatus.Text = args.IsSuccess ? "" : "Ошибка загрузки плеера";
                if (_currentVideo is not null) LoadingPanel.Visibility = Visibility.Collapsed;
                if (IsMiniMode) await EnableMiniDragOverlayAsync();
            };
            Browser.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            {
                FullScreenChanged?.Invoke(Browser.CoreWebView2.ContainsFullScreenElement);
            };
            if (!_webMessageHooked)
            {
                Browser.CoreWebView2.WebMessageReceived += (_, args) =>
                {
                    if (string.Equals(args.TryGetWebMessageAsString(), "drag", StringComparison.OrdinalIgnoreCase))
                    {
                        DragRequested?.Invoke();
                    }
                };
                _webMessageHooked = true;
            }
        }
        catch (Exception ex)
        {
            LoadingTitle.Text = "WebView2 не запустился";
            LoadingText.Text = ex.Message;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        _catalogMode = CatalogMode.Catalog;
        UpdateCatalogTabStyles();
        await SearchAsync();
    }

    private async void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _catalogMode = CatalogMode.Catalog;
        UpdateCatalogTabStyles();
        await SearchAsync();
    }

    private async void CatalogTab_Click(object sender, RoutedEventArgs e)
    {
        _catalogMode = CatalogMode.Catalog;
        UpdateCatalogTabStyles();
        AccountPanel.Visibility = Visibility.Collapsed;
        await SearchAsync();
    }

    private void FavoritesTab_Click(object sender, RoutedEventArgs e)
    {
        _catalogMode = CatalogMode.Favorites;
        UpdateCatalogTabStyles();
        AccountPanel.Visibility = Visibility.Collapsed;
        ShowFavoritesCatalog();
    }

    private void HistoryTab_Click(object sender, RoutedEventArgs e)
    {
        _catalogMode = CatalogMode.History;
        UpdateCatalogTabStyles();
        AccountPanel.Visibility = Visibility.Collapsed;
        ShowHistoryCatalog();
    }

    private void AccountPanel_Click(object sender, RoutedEventArgs e)
    {
        AccountPanel.Visibility = AccountPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateLoginStatus();
    }

    private void UpdateCatalogTabStyles()
    {
        CatalogTabButton.Style = StyleFor(_catalogMode == CatalogMode.Catalog);
        FavoritesTabButton.Style = StyleFor(_catalogMode == CatalogMode.Favorites);
        HistoryTabButton.Style = StyleFor(_catalogMode == CatalogMode.History);
        SyncTabButton.Style = (Style)FindResource("GhostButton");
    }

    private Style StyleFor(bool active) =>
        (Style)FindResource(active ? "PrimaryButton" : "GhostButton");

    private void RefreshCatalog()
    {
        switch (_catalogMode)
        {
            case CatalogMode.Favorites:
                ShowFavoritesCatalog();
                break;
            case CatalogMode.History:
                ShowHistoryCatalog();
                break;
            default:
                _ = SearchAsync();
                break;
        }
    }

    private async Task SearchAsync()
    {
        if (_service is null) return;
        var cancellationToken = ReplaceCancellation();
        try
        {
            CatalogStatus.Text = "Загрузка каталога…";
            var items = await _service.SearchAsync(SearchBox.Text, cancellationToken);
            CatalogItems.ItemsSource = items;
            CatalogStatus.Text = items.Count == 0
                ? "Ничего не найдено."
                : $"Найдено: {items.Count}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CatalogStatus.Text = ex.Message;
        }
    }

    private void ShowFavoritesCatalog()
    {
        CatalogItems.ItemsSource = _favorites
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        CatalogStatus.Text = _favorites.Count == 0
            ? "Избранное пусто. Нажмите ★ на карточке аниме."
            : $"Избранное: {_favorites.Count}";
    }

    private void ShowHistoryCatalog()
    {
        var items = _watchHistory
            .GroupBy(item => item.AnimeId)
            .Select(group => group.OrderByDescending(item => item.WatchedAt).First())
            .OrderByDescending(item => item.WatchedAt)
            .Take(40)
            .ToList();
        CatalogItems.ItemsSource = items;
        CatalogStatus.Text = items.Count == 0
            ? "История пуста. Откройте серию — она появится здесь."
            : $"Недавние просмотры: {items.Count}";
    }

    private async void CatalogItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        switch (element.DataContext)
        {
            case YummyAnimeItem anime:
                await OpenAnimeAsync(anime);
                break;
            case YummyAnimeFavorite favorite:
                await OpenFavoriteAsync(favorite);
                break;
            case YummyAnimeWatchEntry history:
                await OpenHistoryAsync(history);
                break;
        }
    }

    private async Task OpenAnimeAsync(YummyAnimeItem anime)
    {
        _selectedAnime = anime;
        _selectedFavorite = null;
        await LoadAnimePlayerAsync(anime.AnimeId, anime.Title, anime.PosterUrl);
    }

    private async Task OpenFavoriteAsync(YummyAnimeFavorite favorite)
    {
        _selectedAnime = null;
        _selectedFavorite = favorite;
        await LoadAnimePlayerAsync(favorite.AnimeId, favorite.Title, favorite.PosterUrl);
    }

    private async Task OpenHistoryAsync(YummyAnimeWatchEntry history)
    {
        _selectedAnime = null;
        _selectedFavorite = new YummyAnimeFavorite
        {
            AnimeId = history.AnimeId,
            Title = history.AnimeTitle,
            PosterUrl = history.PosterUrl,
            Subtitle = history.Episode
        };
        await LoadAnimePlayerAsync(history.AnimeId, history.AnimeTitle, history.PosterUrl, history.VideoId);
    }

    private async Task LoadAnimePlayerAsync(
        int animeId,
        string title,
        string posterUrl,
        int preferredVideoId = 0)
    {
        if (_service is null) return;
        var cancellationToken = ReplaceCancellation();
        try
        {
            ShowPlayerScreen();
            SelectedAnimeTitle.Text = title;
            PlayerStatus.Text = "Загрузка серий…";
            _videos = await _service.GetVideosAsync(animeId, cancellationToken);
            ApplyWatchedMarks();
            FillDubbings(preferredVideoId);
            PlayerStatus.Text = _videos.Count == 0 ? "Доступных серий нет" : "";
            UpdateFavoriteButton();

            if (_selectedFavorite is null && !string.IsNullOrWhiteSpace(posterUrl))
            {
                _selectedFavorite = new YummyAnimeFavorite
                {
                    AnimeId = animeId,
                    Title = title,
                    PosterUrl = posterUrl
                };
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PlayerStatus.Text = ex.Message;
        }
    }

    private void ApplyWatchedMarks()
    {
        foreach (var video in _videos)
        {
            video.IsWatched = _watchedVideoIds.Contains(video.VideoId);
        }
    }

    private void ShowPlayerScreen()
    {
        CatalogScreen.Visibility = Visibility.Collapsed;
        PlayerScreen.Visibility = Visibility.Visible;
        LoginStatus.Visibility = Visibility.Collapsed;
    }

    private void ShowCatalogScreen()
    {
        PlayerScreen.Visibility = Visibility.Collapsed;
        CatalogScreen.Visibility = Visibility.Visible;
        LoginStatus.Visibility = Visibility.Visible;
        RefreshCatalog();
    }

    private void BackToCatalog_Click(object sender, RoutedEventArgs e) => ShowCatalogScreen();

    private void FillDubbings(int preferredVideoId = 0)
    {
        _updatingFilters = true;
        var preferred = preferredVideoId > 0
            ? _videos.FirstOrDefault(video => video.VideoId == preferredVideoId)
            : null;
        DubbingBox.ItemsSource = _videos
            .Select(video => video.Dubbing)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (preferred is not null)
        {
            DubbingBox.SelectedItem = preferred.Dubbing;
        }
        else
        {
            DubbingBox.SelectedIndex = DubbingBox.Items.Count > 0 ? 0 : -1;
        }
        _updatingFilters = false;
        FillPlayers(preferredVideoId);
    }

    private void DubbingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFilters) FillPlayers();
    }

    private void FillPlayers(int preferredVideoId = 0)
    {
        var dubbing = DubbingBox.SelectedItem?.ToString() ?? "";
        var preferred = preferredVideoId > 0
            ? _videos.FirstOrDefault(video => video.VideoId == preferredVideoId)
            : null;
        _updatingFilters = true;
        PlayerBox.ItemsSource = _videos
            .Where(video => video.Dubbing.Equals(dubbing, StringComparison.OrdinalIgnoreCase))
            .Select(video => video.Player)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (preferred is not null && preferred.Dubbing.Equals(dubbing, StringComparison.OrdinalIgnoreCase))
        {
            PlayerBox.SelectedItem = preferred.Player;
        }
        else
        {
            PlayerBox.SelectedIndex = PlayerBox.Items.Count > 0 ? 0 : -1;
        }
        _updatingFilters = false;
        FillEpisodes(preferredVideoId);
    }

    private void PlayerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFilters) FillEpisodes();
    }

    private void FillEpisodes(int preferredVideoId = 0)
    {
        var dubbing = DubbingBox.SelectedItem?.ToString() ?? "";
        var player = PlayerBox.SelectedItem?.ToString() ?? "";
        var episodes = _videos
            .Where(video =>
                video.Dubbing.Equals(dubbing, StringComparison.OrdinalIgnoreCase) &&
                video.Player.Equals(player, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _updatingFilters = true;
        EpisodeBox.ItemsSource = episodes;
        if (preferredVideoId > 0)
        {
            EpisodeBox.SelectedItem = episodes.FirstOrDefault(video => video.VideoId == preferredVideoId)
                ?? episodes.FirstOrDefault();
        }
        else
        {
            var lastLocal = _watchHistory
                .Where(entry => entry.AnimeId == (_selectedAnime?.AnimeId ?? _selectedFavorite?.AnimeId ?? 0)
                    && episodes.Any(video => video.VideoId == entry.VideoId))
                .OrderByDescending(entry => entry.WatchedAt)
                .FirstOrDefault();
            EpisodeBox.SelectedItem = lastLocal is null
                ? episodes.FirstOrDefault()
                : episodes.FirstOrDefault(video => video.VideoId == lastLocal.VideoId) ?? episodes.FirstOrDefault();
        }
        _updatingFilters = false;

        if (EpisodeBox.SelectedItem is YummyAnimeVideo selected)
        {
            _ = PlayEpisodeAsync(selected);
        }
    }

    private async void EpisodeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilters) return;
        if (EpisodeBox.SelectedItem is not YummyAnimeVideo video) return;
        await PlayEpisodeAsync(video);
    }

    private async Task PlayEpisodeAsync(YummyAnimeVideo video)
    {
        var playerUri = video.GetPlayerUri();
        if (playerUri is null || Browser.CoreWebView2 is null) return;
        if (_currentVideo?.VideoId == video.VideoId && LoadingPanel.Visibility != Visibility.Visible) return;

        _currentVideo = video;
        PlayerStatus.Text = "Загрузка плеера…";
        LoadingTitle.Text = video.DisplayTitle;
        LoadingText.Text = "Подключение к видеоплееру…";
        LoadingPanel.Visibility = Visibility.Visible;
        var source = HtmlEncoder.Default.Encode(playerUri.AbsoluteUri);
        Browser.NavigateToString(
            $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <style>
                html,body{width:100%;height:100%;margin:0;overflow:hidden;background:#000}
                iframe{position:fixed;inset:0;width:100%;height:100%;border:0;background:#000}
              </style>
            </head>
            <body>
              <iframe src="{{source}}"
                      allow="autoplay *; fullscreen *; picture-in-picture *; encrypted-media *"
                      allowfullscreen></iframe>
            </body>
            </html>
            """);

        await RecordWatchAsync(video);
        if (IsMiniMode) await EnableMiniDragOverlayAsync();
    }

    private async Task RecordWatchAsync(YummyAnimeVideo video)
    {
        var animeId = _selectedAnime?.AnimeId ?? _selectedFavorite?.AnimeId ?? 0;
        var animeTitle = _selectedAnime?.Title
            ?? _selectedFavorite?.Title
            ?? SelectedAnimeTitle.Text;
        var posterUrl = _selectedAnime?.PosterUrl
            ?? _selectedFavorite?.PosterUrl
            ?? "";
        var duration = Math.Max(0, (int)video.Duration);
        var progress = duration > 0 ? Math.Max(1, duration / 20) : 0;

        var existing = _watchHistory.FirstOrDefault(entry => entry.VideoId == video.VideoId);
        if (existing is null)
        {
            existing = new YummyAnimeWatchEntry { VideoId = video.VideoId };
            _watchHistory.Insert(0, existing);
        }

        existing.AnimeId = animeId;
        existing.AnimeTitle = animeTitle;
        existing.Episode = video.DisplayTitle;
        existing.Dubbing = video.Dubbing;
        existing.Player = video.Player;
        existing.PosterUrl = posterUrl;
        existing.ProgressSeconds = progress;
        existing.DurationSeconds = duration;
        existing.WatchedAt = DateTime.Now;

        while (_watchHistory.Count > 200)
        {
            _watchHistory.RemoveAt(_watchHistory.Count - 1);
        }

        _watchedVideoIds.Add(video.VideoId);
        video.IsWatched = true;
        RefreshEpisodeBoxTitles();
        _saveLocalData();

        if (_service is not null && !string.IsNullOrWhiteSpace(_service.UserToken))
        {
            try
            {
                await _service.MarkWatchedAsync(video, progress);
                PlayerStatus.Text = "Просмотр сохранён";
            }
            catch (YummyAnimeException ex)
            {
                PlayerStatus.Text = ex.Message;
            }
        }
        else
        {
            PlayerStatus.Text = "Просмотр сохранён локально";
        }
    }

    private void RefreshEpisodeBoxTitles()
    {
        var selected = EpisodeBox.SelectedItem as YummyAnimeVideo;
        var items = EpisodeBox.ItemsSource as IList<YummyAnimeVideo>
            ?? EpisodeBox.Items.OfType<YummyAnimeVideo>().ToList();
        _updatingFilters = true;
        EpisodeBox.ItemsSource = null;
        EpisodeBox.ItemsSource = items.ToList();
        if (selected is not null)
        {
            EpisodeBox.SelectedItem = items.FirstOrDefault(video => video.VideoId == selected.VideoId);
        }
        _updatingFilters = false;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        if (string.IsNullOrWhiteSpace(LoginBox.Text) || string.IsNullOrEmpty(PasswordBox.Password))
        {
            AccountHint.Text = "Введите e-mail и пароль аккаунта YummyAnime.";
            return;
        }

        LoginButton.IsEnabled = false;
        AccountHint.Text = "Вход…";
        try
        {
            var token = await _service.LoginAsync(LoginBox.Text, PasswordBox.Password);
            _saveUserToken(token);
            PasswordBox.Clear();
            var remoteWatched = await _service.GetWatchedVideoIdsAsync();
            foreach (var id in remoteWatched) _watchedVideoIds.Add(id);
            ApplyWatchedMarks();
            RefreshEpisodeBoxTitles();
            AccountHint.Text = "Готово. Просмотры и избранное будут синхронизироваться.";
            UpdateLoginStatus();
        }
        catch (Exception ex)
        {
            AccountHint.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _service?.ClearUserToken();
        _saveUserToken("");
        PasswordBox.Clear();
        UpdateLoginStatus();
        AccountHint.Text = "Вы вышли. Локальная история просмотров осталась на этом ПК.";
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement element) return;
        if (element.Tag is YummyAnimeItem anime)
        {
            ToggleFavorite(anime.AnimeId, anime.ToFavorite());
        }
    }

    private void FavoriteToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var animeId = _selectedAnime?.AnimeId ?? _selectedFavorite?.AnimeId ?? 0;
        if (animeId <= 0) return;
        var favorite = _selectedAnime?.ToFavorite()
            ?? _selectedFavorite
            ?? new YummyAnimeFavorite { AnimeId = animeId, Title = SelectedAnimeTitle.Text };
        ToggleFavorite(animeId, favorite);
    }

    private void ToggleFavorite(int animeId, YummyAnimeFavorite favorite)
    {
        var existing = _favorites.FirstOrDefault(item => item.AnimeId == animeId);
        if (existing is not null)
        {
            _favorites.Remove(existing);
            _ = SyncFavoriteAsync(animeId, add: false);
        }
        else
        {
            _favorites.Add(favorite);
            _ = SyncFavoriteAsync(animeId, add: true);
        }
        _saveLocalData();
        UpdateFavoriteButton();
        if (_catalogMode == CatalogMode.Favorites) ShowFavoritesCatalog();
    }

    private async Task SyncFavoriteAsync(int animeId, bool add)
    {
        if (_service is null || string.IsNullOrWhiteSpace(_service.UserToken)) return;
        try
        {
            if (add) await _service.AddFavoriteAsync(animeId);
            else await _service.RemoveFavoriteAsync(animeId);
        }
        catch (YummyAnimeException ex)
        {
            AccountHint.Text = ex.Message;
        }
    }

    private void UpdateFavoriteButton()
    {
        var animeId = _selectedAnime?.AnimeId ?? _selectedFavorite?.AnimeId ?? 0;
        var isFavorite = animeId > 0 && _favorites.Any(item => item.AnimeId == animeId);
        FavoriteToggleButton.Content = isFavorite ? "★" : "☆";
        FavoriteToggleButton.ToolTip = isFavorite ? "Убрать из избранного" : "Добавить в избранное";
    }

    private void UpdateLoginStatus()
    {
        if (_service is null)
        {
            LoginStatus.Text = "Сначала настройте токен приложения в настройках.";
            AccountHint.Text = "Сначала укажите X-Application token в настройках WideS.";
            LogoutButton.IsEnabled = false;
            return;
        }

        var signedIn = !string.IsNullOrWhiteSpace(_service.UserToken);
        LogoutButton.IsEnabled = signedIn;
        LoginStatus.Text = signedIn
            ? "Синхронизация с YummyAnime включена."
            : "Без входа история сохраняется только локально на этом ПК.";
        AccountHint.Text = signedIn
            ? "Вы вошли. Можно закрыть этот блок."
            : "Вход необязателен — каталог и локальная история работают и без него.";
    }

    private CancellationToken ReplaceCancellation()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        return _loadCancellation.Token;
    }

    public void AttachTo(ContentControl host)
    {
        if (Parent is ContentControl currentHost) currentHost.Content = null;
        host.Content = this;
    }

    public async Task<bool> TryEnterMiniModeAsync()
    {
        if (_currentVideo is null || Browser.CoreWebView2 is null) return false;
        if (IsMiniMode) return true;
        IsMiniMode = true;
        ApplyPlayerOnlyLayout();
        await EnableMiniDragOverlayAsync();
        return true;
    }

    public async Task ExitMiniModeAsync()
    {
        if (!IsMiniMode) return;
        IsMiniMode = false;
        await DisableMiniDragOverlayAsync();
        RestoreNormalLayout();
    }

    public void EnterFullScreenHostLayout() => ApplyPlayerOnlyLayout();

    public async void ExitFullScreenHostLayout()
    {
        if (!IsMiniMode) RestoreNormalLayout();
        await Task.CompletedTask;
    }

    private void ApplyPlayerOnlyLayout()
    {
        MinHeight = 0;
        CatalogScreen.Visibility = Visibility.Collapsed;
        PlayerScreen.Visibility = Visibility.Visible;
        PlayerHeaderRow.Height = new GridLength(0);
        PlayerControlsRow.Height = new GridLength(0);
        LoginStatus.Visibility = Visibility.Collapsed;
        PlayerFrame.BorderThickness = new Thickness(0);
        PlayerFrame.CornerRadius = new CornerRadius(0);
    }

    private void RestoreNormalLayout()
    {
        MinHeight = 620;
        if (_currentVideo is null && PlayerScreen.Visibility != Visibility.Visible)
        {
            ShowCatalogScreen();
        }
        else
        {
            CatalogScreen.Visibility = Visibility.Collapsed;
            PlayerScreen.Visibility = Visibility.Visible;
        }
        PlayerHeaderRow.Height = GridLength.Auto;
        PlayerControlsRow.Height = GridLength.Auto;
        LoginStatus.Visibility = CatalogScreen.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlayerFrame.BorderThickness = new Thickness(1);
        PlayerFrame.CornerRadius = new CornerRadius(8);
    }

    private async Task EnableMiniDragOverlayAsync()
    {
        if (Browser.CoreWebView2 is null) return;
        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                  let bar = document.getElementById('wides-drag');
                  if (!bar) {
                    bar = document.createElement('div');
                    bar.id = 'wides-drag';
                    bar.addEventListener('mousedown', (e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      try { chrome.webview.postMessage('drag'); } catch {}
                    });
                    document.body.appendChild(bar);
                  }
                  bar.style.cssText = 'position:fixed;top:0;left:0;right:0;height:22px;z-index:2147483647;cursor:move;background:transparent;';
                  return true;
                })();
                """);
        }
        catch
        {
        }
    }

    private async Task DisableMiniDragOverlayAsync()
    {
        if (Browser.CoreWebView2 is null) return;
        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                  const bar = document.getElementById('wides-drag');
                  if (bar) bar.remove();
                })();
                """);
        }
        catch
        {
        }
    }

    public void StopPlayback()
    {
        _currentVideo = null;
        Browser.CoreWebView2?.Navigate("about:blank");
        LoadingTitle.Text = "Плеер готов";
        LoadingText.Text = "Выберите серию";
        LoadingPanel.Visibility = Visibility.Visible;
        ShowCatalogScreen();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _service?.Dispose();
        Browser.Dispose();
    }
}
