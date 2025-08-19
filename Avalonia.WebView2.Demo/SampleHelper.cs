using Avalonia.Input;
using System;
using System.Linq;
using System.Net;
using static Avalonia.Controls.WebView2;
using WV2 = Avalonia.Controls.WebView2;

namespace Avalonia.WebView2.Demo;

static partial class SampleHelper
{
    internal static void Navigate(WV2 wv2, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (string.Equals("t", url, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("test", url, StringComparison.OrdinalIgnoreCase) ||
                url.All(static x => x == ' ' || x == '\t' || x == '\r' || x == '\n' || x == default || char.ToLowerInvariant(x) == 't'))
            {
                wv2.Source = new WebResourceRequestUri("https://wv2.bing.com", null)
                {
                     
                };

                return;
            }
            else if (url.Contains('.'))
            {
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = $"https://{url}";
                }
                wv2.Navigate(url);
            }
        }
    }
}
