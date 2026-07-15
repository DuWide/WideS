namespace DevCockpit;

internal static class VideoPlayerScripts
{
    // Отмечаем серию просмотренной, когда до конца осталось ≤ 10 минут.
    private const int WatchedRemainingSeconds = 600;

    public static string BuildFrameEnhancements(bool skipOp, int skipSec, bool autoNext) =>
        $$"""
        (() => {
          const cfg = {
            skipOp: {{(skipOp ? "true" : "false")}},
            skipSec: {{Math.Max(20, skipSec)}},
            autoNext: {{(autoNext ? "true" : "false")}},
            watchRemain: {{WatchedRemainingSeconds}}
          };
          window.__widesCfg = cfg;

          if (window.__widesFrameHook) return true;
          window.__widesFrameHook = true;

          const post = (msg) => {
            try { chrome.webview.postMessage(msg); } catch (_) {}
          };

          let skippedOp = false;
          let watchedSent = false;
          let endedSent = false;

          const isMainEpisode = (video) =>
            !!video && Number.isFinite(video.duration) && video.duration >= Math.max(cfg.skipSec + 90, 480);

          const maybeSkipOp = (video) => {
            if (!cfg.skipOp || skippedOp) return;
            if (!isMainEpisode(video)) return;
            if (video.currentTime >= 0 && video.currentTime < Math.min(5, cfg.skipSec / 5)) {
              try {
                video.currentTime = cfg.skipSec;
                skippedOp = true;
                post('skipped_op');
              } catch (_) {}
            }
          };

          const maybeWatched = (video) => {
            if (watchedSent || !isMainEpisode(video)) return;
            const left = video.duration - video.currentTime;
            if (left <= cfg.watchRemain) {
              watchedSent = true;
              post('watched:' + Math.floor(video.currentTime) + ':' + Math.floor(video.duration));
            }
          };

          const maybeEnded = (video) => {
            if (!cfg.autoNext || endedSent) return;
            if (!isMainEpisode(video)) return;
            if (video.ended || (video.duration - video.currentTime) <= 1.25) {
              endedSent = true;
              maybeWatched(video);
              post('ended');
            }
          };

          const bindVideo = (video) => {
            if (!video || video.__widesBound) return;
            video.__widesBound = true;
            video.addEventListener('timeupdate', () => {
              maybeSkipOp(video);
              maybeWatched(video);
              maybeEnded(video);
            });
            video.addEventListener('ended', () => maybeEnded(video));
            video.addEventListener('loadedmetadata', () => maybeSkipOp(video));
          };

          const scan = () => document.querySelectorAll('video').forEach(bindVideo);
          setInterval(scan, 1200);
          try {
            new MutationObserver(scan).observe(document.documentElement || document.body, {
              childList: true,
              subtree: true
            });
          } catch (_) {}
          scan();
          return true;
        })();
        """;
}
