namespace DevCockpit;

internal static class VideoPlayerScripts
{
    public static string BuildFrameEnhancements(bool skipOp, int skipSec, bool clickSkipAds, bool autoNext) =>
        $$"""
        (() => {
          const cfg = {
            skipOp: {{(skipOp ? "true" : "false")}},
            skipSec: {{Math.Max(20, skipSec)}},
            clickSkipAds: {{(clickSkipAds ? "true" : "false")}},
            autoNext: {{(autoNext ? "true" : "false")}}
          };
          window.__widesCfg = cfg;

          if (window.__widesFrameHook) return true;
          window.__widesFrameHook = true;

          const post = (msg) => {
            try { chrome.webview.postMessage(msg); } catch (_) {}
          };

          let skippedOp = false;
          let endedSent = false;

          const clickSkipAdsFn = () => {
            if (!cfg.clickSkipAds) return;
            const nodes = document.querySelectorAll('button, a, div, span, input');
            for (const el of nodes) {
              const text = ((el.innerText || el.textContent || el.value || '') + '').trim().toLowerCase();
              if (!text || text.length > 48) continue;
              if (/пропуст/.test(text) && /реклам|ad|ads/.test(text)) {
                try { el.click(); } catch (_) {}
              } else if (/^skip\s*ad|^skip\s*ads/.test(text)) {
                try { el.click(); } catch (_) {}
              }
            }
          };

          const maybeSkipOp = (video) => {
            if (!cfg.skipOp || skippedOp) return;
            if (!(video.duration > cfg.skipSec + 45)) return;
            if (video.currentTime >= 0 && video.currentTime < Math.min(8, cfg.skipSec / 4)) {
              try {
                video.currentTime = cfg.skipSec;
                skippedOp = true;
                post('skipped_op');
              } catch (_) {}
            }
          };

          const maybeEnded = (video) => {
            if (!cfg.autoNext || endedSent) return;
            if (!(video.duration > 30)) return;
            if (video.ended || (video.duration - video.currentTime) <= 1.25) {
              endedSent = true;
              post('ended');
            }
          };

          const bindVideo = (video) => {
            if (!video || video.__widesBound) return;
            video.__widesBound = true;
            video.addEventListener('timeupdate', () => {
              maybeSkipOp(video);
              maybeEnded(video);
            });
            video.addEventListener('ended', () => maybeEnded(video));
            video.addEventListener('loadedmetadata', () => maybeSkipOp(video));
          };

          const scan = () => {
            clickSkipAdsFn();
            document.querySelectorAll('video').forEach(bindVideo);
          };

          setInterval(scan, 600);
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

internal static class VideoAdBlocker
{
    private static readonly string[] BlockParts =
    [
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "pagead2.googlesyndication",
        "adservice.google",
        "adnxs.com",
        "adsrvr.org",
        "adfox.",
        "yandex.ru/ads",
        "an.yandex.ru",
        "advertising.com",
        "adcolony.com",
        "moatads.com",
        "scorecardresearch.com",
        "mostbet",
        "1xbet",
        "1xstavka",
        "betcity",
        "fonbet",
        "melbet",
        "vavada",
        "pin-up",
        "pinup",
        "ggbet",
        "cpmstar.com",
        "juicyads.com",
        "exoclick.com",
        "popads.",
        "propellerads",
        "adskeeper",
        "mgid.com",
        "outbrain.com",
        "taboola.com",
        "yandexadexchange",
        "imasdk.googleapis.com",
        "ima3.js",
        "vast.yandex",
        "ad.mail.ru",
        "ads.vk.com"
    ];

    private static readonly string[] AllowParts =
    [
        "kodik",
        "yani.tv",
        "yummyani",
        "kodik-storage",
        "kodik-cdn",
        "cloud.kodik"
    ];

    public static bool ShouldBlock(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var value = uri.ToLowerInvariant();
        if (AllowParts.Any(value.Contains)) return false;
        return BlockParts.Any(value.Contains);
    }
}
