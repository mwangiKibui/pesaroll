namespace Pesaroll.Identity.Tests;

/// <summary>AUTH-05: a signed-in device, as people recognise it, from the browser's user agent.</summary>
public sealed class DevicesTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15", "Safari on Mac")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-A145F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; TECNO KI5k) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36 OPR/80.0.0.0", "Opera on Android")]
    [InlineData("curl/8.7.1", "Unknown browser")]
    [InlineData("", "Unknown device")]
    [InlineData(null, "Unknown device")]
    public void A_device_is_named_by_browser_and_system(string? userAgent, string name) =>
        Assert.Equal(name, Devices.Describe(userAgent));
}
