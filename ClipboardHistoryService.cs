using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace DevCockpit;

public static partial class ClipboardHistoryService
{
    private const int MaxItems = 160;
    private const int MaxImages = 30;

    public static ClipboardHistoryItem? CaptureText(ClipboardHistoryStoreData store, string text, string sourceApp)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        var hash = Hash(text);
        if (store.Items.OrderByDescending(x => x.CapturedAt).FirstOrDefault()?.ContentHash == hash)
        {
            return null;
        }

        var sensitive = LooksSensitive(text);
        var kind = Classify(text, sensitive);
        var item = new ClipboardHistoryItem
        {
            Kind = kind,
            Preview = sensitive ? "Содержимое скрыто: возможно, здесь есть учетные данные" : BuildPreview(text),
            EncryptedContent = SecretService.Protect(text),
            SourceApp = FriendlySource(sourceApp),
            ContentHash = hash,
            IsSensitive = sensitive,
            CapturedAt = DateTime.Now
        };
        store.Items.Add(item);
        Prune(store);
        return item;
    }

    public static ClipboardHistoryItem? CaptureImage(ClipboardHistoryStoreData store, BitmapSource image, string sourceApp)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (store.Items.OrderByDescending(x => x.CapturedAt).FirstOrDefault()?.ContentHash == hash)
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.ClipboardImagesDirectory);
        var path = Path.Combine(AppPaths.ClipboardImagesDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);
        var item = new ClipboardHistoryItem
        {
            Kind = "Изображение",
            Preview = $"{image.PixelWidth} x {image.PixelHeight}",
            ImagePath = path,
            SourceApp = FriendlySource(sourceApp),
            ContentHash = hash,
            CapturedAt = DateTime.Now
        };
        store.Items.Add(item);
        Prune(store);
        return item;
    }

    public static string GetText(ClipboardHistoryItem item) =>
        SecretService.Unprotect(item.EncryptedContent);

    public static void Delete(ClipboardHistoryStoreData store, ClipboardHistoryItem item)
    {
        store.Items.Remove(item);
        DeleteImage(item.ImagePath);
    }

    public static void ClearUnpinned(ClipboardHistoryStoreData store)
    {
        foreach (var item in store.Items.Where(x => !x.IsPinned).ToList())
        {
            Delete(store, item);
        }
    }

    private static void Prune(ClipboardHistoryStoreData store)
    {
        var excess = store.Items
            .Where(x => !x.IsPinned)
            .OrderByDescending(x => x.CapturedAt)
            .Skip(MaxItems)
            .ToList();
        foreach (var item in excess) Delete(store, item);

        var oldImages = store.Items
            .Where(x => !x.IsPinned && x.Kind == "Изображение")
            .OrderByDescending(x => x.CapturedAt)
            .Skip(MaxImages)
            .ToList();
        foreach (var item in oldImages) Delete(store, item);
    }

    private static string Classify(string text, bool sensitive)
    {
        if (sensitive) return "Секрет";
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return "Ссылка";
        if (Path.IsPathFullyQualified(text) || File.Exists(text) || Directory.Exists(text)) return "Путь";
        if (ConnectionPattern().IsMatch(text)) return "Адрес";
        if (CommandPattern().IsMatch(text)) return "Команда";
        if (CodePattern().IsMatch(text)) return "Код";
        return "Текст";
    }

    private static bool LooksSensitive(string text) =>
        SensitivePattern().IsMatch(text) ||
        (text.Length is >= 20 and <= 220 && TokenPattern().IsMatch(text));

    private static string BuildPreview(string text)
    {
        var compact = Regex.Replace(text.Replace('\r', ' ').Replace('\n', ' '), "\\s+", " ").Trim();
        return compact.Length <= 220 ? compact : compact[..220] + "...";
    }

    private static string FriendlySource(string source) => string.IsNullOrWhiteSpace(source) ? "Windows" : source;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void DeleteImage(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A locked preview will be cleaned during a later prune.
        }
    }

    [GeneratedRegex(@"(?i)(password|passwd|pass\s*[:=]|парол|secret|token|api[_ -]?key|access[_ -]?key|логин\s*[:=])")]
    private static partial Regex SensitivePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_\-+/=]{28,}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)^(ping|mstsc|ssh|cmd|powershell|pwsh|dotnet|git|npm|winget|Test-NetConnection)\b")]
    private static partial Regex CommandPattern();

    [GeneratedRegex(@"(?s)(\{.*\}|class\s+\w+|function\s+\w+|процедура\s+\w+|SELECT\s+.+\s+FROM\s+|=>|</?\w+[^>]*>)", RegexOptions.IgnoreCase)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^(?:\d{8,10}|(?:[a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}|(?:\d{1,3}\.){3}\d{1,3})(?::\d{2,5})?$")]
    private static partial Regex ConnectionPattern();
}
