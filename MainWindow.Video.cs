namespace DevCockpit;

public partial class MainWindow
{
    private VideoBrowserView? _videoBrowserView;
    private FloatingVideoWindow? _floatingVideoWindow;
    private bool _changingVideoHost;
    private bool _wasMiniBeforeFullScreen;

    private void InitializeVideoLifecycle()
    {
        StateChanged += async (_, _) =>
        {
            if (WindowState == System.Windows.WindowState.Minimized)
            {
                FloatVideoIfAvailable();
                return;
            }

            await TryRestoreVideoToMainAsync();
        };
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await TryRestoreVideoToMainAsync();
            }
        };
        Activated += async (_, _) => await TryRestoreVideoToMainAsync();
    }

    private async Task TryRestoreVideoToMainAsync()
    {
        if (!IsVisible || WindowState == System.Windows.WindowState.Minimized) return;
        if (_currentViewKey != "video" || _videoBrowserView?.IsMiniMode != true) return;
        await RestoreVideoToMainAsync();
    }

    private async void ShowVideo()
    {
        EnterView("video");
        SetTitle("Плеер", "YummyAnime: каталог, избранное, серии и автоматический мини-плеер");
        EnsureVideoBrowserView();
        await RestoreVideoToMainAsync(forceAttach: true);
    }

    private VideoBrowserView EnsureVideoBrowserView()
    {
        if (_videoBrowserView is not null) return _videoBrowserView;
        var applicationToken = SecretService.Unprotect(_settings.YummyAnimeAppTokenEncrypted);
        var userToken = SecretService.Unprotect(_settings.YummyAnimeUserTokenEncrypted);
        _videoBrowserView = new VideoBrowserView(
            applicationToken,
            userToken,
            token =>
            {
                _settings.YummyAnimeUserTokenEncrypted = string.IsNullOrWhiteSpace(token)
                    ? ""
                    : SecretService.Protect(token);
                _settingsStore.Save(_settings);
            },
            _settings,
            () => _settingsStore.Save(_settings));
        _videoBrowserView.FullScreenChanged += HandleVideoFullScreenChanged;
        return _videoBrowserView;
    }

    private void ResetVideoBrowser()
    {
        ShutdownVideoBrowser();
    }

    private async void HandleVideoFullScreenChanged(bool isFullScreen)
    {
        if (_videoBrowserView is null) return;

        if (isFullScreen)
        {
            _wasMiniBeforeFullScreen = _videoBrowserView.IsMiniMode;
            _videoBrowserView.EnterFullScreenHostLayout();
            _floatingVideoWindow ??= CreateFloatingVideoWindow();
            _floatingVideoWindow.Attach(_videoBrowserView);
            if (!_floatingVideoWindow.IsVisible)
            {
                _floatingVideoWindow.Show();
            }
            _floatingVideoWindow.EnterFullScreen();
            return;
        }

        _floatingVideoWindow?.ExitFullScreen();
        _videoBrowserView.ExitFullScreenHostLayout();
        if (!_wasMiniBeforeFullScreen && _currentViewKey == "video" && IsVisible)
        {
            await RestoreVideoToMainAsync();
        }
    }

    private async void FloatVideoIfAvailable()
    {
        if (_changingVideoHost || _videoBrowserView is null || _videoBrowserView.IsMiniMode) return;
        _changingVideoHost = true;
        try
        {
            if (!await _videoBrowserView.TryEnterMiniModeAsync()) return;

            _floatingVideoWindow ??= CreateFloatingVideoWindow();
            _floatingVideoWindow.Attach(_videoBrowserView);
            if (!_floatingVideoWindow.IsVisible)
            {
                _floatingVideoWindow.Show();
            }
            _floatingVideoWindow.Activate();
        }
        finally
        {
            _changingVideoHost = false;
        }
    }

    private FloatingVideoWindow CreateFloatingVideoWindow()
    {
        var window = new FloatingVideoWindow();
        window.CloseRequested += async (_, _) =>
        {
            window.Detach();
            window.Hide();
            if (_videoBrowserView is null) return;
            await _videoBrowserView.ExitMiniModeAsync();
            _videoBrowserView.StopPlayback();
            if (_currentViewKey == "video" && IsVisible)
            {
                _videoBrowserView.AttachTo(ContentHost);
            }
        };
        return window;
    }

    private async Task RestoreVideoToMainAsync(bool forceAttach = false)
    {
        if (_changingVideoHost || _videoBrowserView is null) return;
        if (!forceAttach && !_videoBrowserView.IsMiniMode) return;

        _changingVideoHost = true;
        try
        {
            _floatingVideoWindow?.Detach();
            _floatingVideoWindow?.Hide();
            _videoBrowserView.AttachTo(ContentHost);
            await _videoBrowserView.ExitMiniModeAsync();
        }
        finally
        {
            _changingVideoHost = false;
        }
    }

    private void ShutdownVideoBrowser()
    {
        _floatingVideoWindow?.Detach();
        _floatingVideoWindow?.ClosePermanently();
        _floatingVideoWindow = null;
        if (_videoBrowserView is not null)
        {
            _videoBrowserView.FullScreenChanged -= HandleVideoFullScreenChanged;
        }
        _videoBrowserView?.Dispose();
        _videoBrowserView = null;
    }
}
