namespace Pesaroll.Identity;

/// <summary>
/// AUTH-05: a signed-in device as people recognise it ("Chrome on Windows", "Safari on iPhone"),
/// from the browser's user agent. Only for showing the person their own sessions.
/// </summary>
public static class Devices
{
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown device";
        }

        var browser = Browser(userAgent);
        var system = System(userAgent);
        return (browser, system) switch
        {
            (null, null) => "Unknown browser",
            (null, _) => $"Unknown browser on {system}",
            (_, null) => browser,
            _ => $"{browser} on {system}",
        };
    }

    // Order matters: Edge and Opera also say Chrome, and Chrome also says Safari.
    private static string? Browser(string ua) =>
        ua.Contains("Edg/") ? "Edge"
        : ua.Contains("OPR/") || ua.Contains("Opera") ? "Opera"
        : ua.Contains("Firefox/") || ua.Contains("FxiOS/") ? "Firefox"
        : ua.Contains("SamsungBrowser/") ? "Samsung Internet"
        : ua.Contains("Chrome/") || ua.Contains("CriOS/") ? "Chrome"
        : ua.Contains("Safari/") && ua.Contains("Version/") ? "Safari"
        : null;

    private static string? System(string ua) =>
        ua.Contains("iPhone") ? "iPhone"
        : ua.Contains("iPad") ? "iPad"
        : ua.Contains("Android") ? "Android"
        : ua.Contains("Windows") ? "Windows"
        : ua.Contains("CrOS") ? "Chromebook"
        : ua.Contains("Macintosh") || ua.Contains("Mac OS X") ? "Mac"
        : ua.Contains("Linux") ? "Linux"
        : null;
}
