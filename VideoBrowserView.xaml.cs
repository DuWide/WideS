using System.Text.Encodings.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace DevCockpit;

public partial class VideoBrowserView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly YummyAnimeService? _service;
    private readonly Action<string> _saveUserToken;
    private Task? _initializationTask;
    private IReadOnlyList<YummyAnimeVideo> _videos = [];
    private YummyAnimeVideo? _currentVideo;
    private CancellationTokenSource? _loadCancellation;
    private bool _updatingFilters;
    private bool _disposed;

    public bool IsMiniMode { get; private set; }
    public event Action<bool>? FullScreenChanged;

    public VideoBrowserView(string applicationToken, string userToken, Action<string> saveUserToken)
    {
        InitializeComponent();
        _saveUserToken = saveUserToken;
        if (!string.IsNullOrWhiteSpace(applicationToken))
        {
            _service = new YummyAnimeService(applicationToken, userToken);
        }
        else
        {
            CatalogStatus.Text = "Укажите публичный X-Application token в настройках WideS.";
            SearchBox.IsEnabled = false;
            LoginPanel.IsEnabled = false;
        }
        UpdateLoginStatus();
        Loaded += async (_, _) => await EnsureInitializedAsync();
    }

    private Task EnsureInitializedAsync() =>
        _initializationTask ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        await InitializeBrowserAsync();
        if (_service is null) return;

        if (!string.IsNullOrWhiteSpace(_service.UserToken))
        {
            try
            {
                var refreshedToken = await _service.RefreshUserTokenAsync();
                if (!string.IsNullOrWhiteSpace(refreshedToken)) _saveUserToken(refreshedToken);
            }
            catch (YummyAnimeException)
            {
                _service.ClearUserToken();
                _saveUserToken("");
            }
            UpdateLoginStatus();
        }
        await SearchAsync();
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
            Browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
            };
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                PlayerStatus.Text = args.IsSuccess ? "" : "Ошибка загрузки плеера";
                if (_currentVideo is not null) LoadingPanel.Visibility = Visibility.Collapsed;
            };
            Browser.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            {
                FullScreenChanged?.Invoke(Browser.CoreWebView2.ContainsFullScreenElement);
            };
        }
        catch (Exception ex)
        {
            LoadingTitle.Text = "WebView2 не запустился";
            LoadingText.Text = ex.Message;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_service is null) return;
        var cancellationToken = ReplaceCancellation();
        try
        {
            CatalogStatus.Text = "Загрузка каталога…";
            AnimeList.ItemsSource = await _service.SearchAsync(SearchBox.Text, cancellationToken);
            CatalogStatus.Text = AnimeList.Items.Count == 0
                ? "Ничего не найдено."
                : $"Найдено: {AnimeList.Items.Count}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CatalogStatus.Text = ex.Message;
        }
    }

    private async void AnimeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_service is null || AnimeList.SelectedItem is not YummyAnimeItem anime) return;
        var cancellationToken = ReplaceCancellation();
        try
        {
            SelectedAnimeTitle.Text = anime.Title;
            PlayerStatus.Text = "Загрузка серий…";
            _videos = await _service.GetVideosAsync(anime.AnimeId, cancellationToken);
            FillDubbings();
            PlayerStatus.Text = _videos.Count == 0 ? "Доступных серий нет" : "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PlayerStatus.Text = ex.Message;
        }
    }

    private void FillDubbings()
    {
        _updatingFilters = true;
        DubbingBox.ItemsSource = _videos
            .Select(video => video.Dubbing)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        DubbingBox.SelectedIndex = DubbingBox.Items.Count > 0 ? 0 : -1;
        _updatingFilters = false;
        FillPlayers();
    }

    private void DubbingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFilters) FillPlayers();
    }

    private void FillPlayers()
    {
        var dubbing = DubbingBox.SelectedItem?.ToString() ?? "";
        _updatingFilters = true;
        PlayerBox.ItemsSource = _videos
            .Where(video => video.Dubbing.Equals(dubbing, StringComparison.OrdinalIgnoreCase))
            .Select(video => video.Player)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        PlayerBox.SelectedIndex = PlayerBox.Items.Count > 0 ? 0 : -1;
        _updatingFilters = false;
        FillEpisodes();
    }

    private void PlayerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFilters) FillEpisodes();
    }

    private void FillEpisodes()
    {
        var dubbing = DubbingBox.SelectedItem?.ToString() ?? "";
        var player = PlayerBox.SelectedItem?.ToString() ?? "";
        EpisodeList.ItemsSource = _videos
            .Where(video =>
                video.Dubbing.Equals(dubbing, StringComparison.OrdinalIgnoreCase) &&
                video.Player.Equals(player, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (EpisodeList.Items.Count > 0) EpisodeList.SelectedIndex = 0;
    }

    private async void EpisodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EpisodeList.SelectedItem is not YummyAnimeVideo video) return;
        await PlayEpisodeAsync(video);
    }

    private async Task PlayEpisodeAsync(YummyAnimeVideo video)
    {
        var playerUri = video.GetPlayerUri();
        if (playerUri is null || Browser.CoreWebView2 is null) return;

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

        if (_service is not null && !string.IsNullOrWhiteSpace(_service.UserToken))
        {
            try
            {
                await _service.MarkWatchedAsync(video);
            }
            catch (YummyAnimeException ex)
            {
                LoginStatus.Text = ex.Message;
            }
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        if (string.IsNullOrWhiteSpace(LoginBox.Text) || string.IsNullOrEmpty(PasswordBox.Password))
        {
            LoginStatus.Text = "Введите e-mail/логин и пароль.";
            return;
        }

        LoginButton.IsEnabled = false;
        LoginStatus.Text = "Вход…";
        try
        {
            var token = await _service.LoginAsync(LoginBox.Text, PasswordBox.Password);
            _saveUserToken(token);
            PasswordBox.Clear();
            LoginPanel.IsExpanded = false;
            UpdateLoginStatus();
        }
        catch (Exception ex)
        {
            LoginStatus.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void UpdateLoginStatus()
    {
        if (_service is null)
        {
            LoginStatus.Text = "Сначала настройте токен приложения.";
            return;
        }
        LoginStatus.Text = string.IsNullOrWhiteSpace(_service.UserToken)
            ? "Вход нужен только для синхронизации просмотренных серий."
            : "Вход выполнен. Просмотренные серии синхронизируются.";
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

    public Task<bool> TryEnterMiniModeAsync()
    {
        if (_currentVideo is null || Browser.CoreWebView2 is null) return Task.FromResult(false);
        if (IsMiniMode) return Task.FromResult(true);
        IsMiniMode = true;
        ApplyPlayerOnlyLayout();
        return Task.FromResult(true);
    }

    public Task ExitMiniModeAsync()
    {
        if (!IsMiniMode) return Task.CompletedTask;
        IsMiniMode = false;
        RestoreNormalLayout();
        return Task.CompletedTask;
    }

    public void EnterFullScreenHostLayout() => ApplyPlayerOnlyLayout();

    public void ExitFullScreenHostLayout()
    {
        if (!IsMiniMode) RestoreNormalLayout();
    }

    private void ApplyPlayerOnlyLayout()
    {
        MinHeight = 0;
        CatalogColumn.Width = new GridLength(0);
        CatalogPanel.Visibility = Visibility.Collapsed;
        PlayerPanel.SetValue(Grid.ColumnProperty, 0);
        PlayerPanel.SetValue(Grid.ColumnSpanProperty, 3);
        PlayerHeaderRow.Height = new GridLength(0);
        PlayerControlsRow.Height = new GridLength(0);
        EpisodeRow.Height = new GridLength(0);
    }

    private void RestoreNormalLayout()
    {
        MinHeight = 620;
        CatalogColumn.Width = new GridLength(310);
        CatalogPanel.Visibility = Visibility.Visible;
        PlayerPanel.SetValue(Grid.ColumnProperty, 2);
        PlayerPanel.SetValue(Grid.ColumnSpanProperty, 1);
        PlayerHeaderRow.Height = GridLength.Auto;
        PlayerControlsRow.Height = GridLength.Auto;
        EpisodeRow.Height = GridLength.Auto;
    }

    public void StopPlayback()
    {
        _currentVideo = null;
        Browser.CoreWebView2?.Navigate("about:blank");
        LoadingTitle.Text = "Плеер готов";
        LoadingText.Text = "Найдите аниме и выберите серию";
        LoadingPanel.Visibility = Visibility.Visible;
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
