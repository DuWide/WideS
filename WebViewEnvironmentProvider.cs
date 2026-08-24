using Microsoft.Web.WebView2.Core;

namespace DevCockpit;

public static class WebViewEnvironmentProvider
{
    private static readonly Lazy<Task<CoreWebView2Environment>> EnvironmentTask =
        new(CreateEnvironmentAsync);

    public static Task<CoreWebView2Environment> GetAsync() => EnvironmentTask.Value;

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(AppPaths.WebAppsDirectory);
        return await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.WebAppsDirectory);
    }
}
