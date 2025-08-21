using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Playwright;
using Microsoft.Web.WebView2.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

using WV2 = Avalonia.Controls.WebView2;

namespace Avalonia.WebView2.Demo.Views
{
    public partial class MainWindow : Window
    {
        WV2? WebView => WebView2CompatControl?.WebView2;
        IPlaywright? playwright;
        IBrowser? browser;
        IPage? page;
        bool? isHg;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            // Delay initialization until Window is opened to ensure visual tree is ready
            this.Opened += async (_, __) =>
            {
                await InitializeWebView2();
            };
        }

        private string _initialUrl = "";
        private bool _isInitialized = false;
        private string? _proxyAddress;

        private string GetConfigPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Avalonia.WebView2.Demo");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        private void LoadSettings()
        {
            try
            {
                var path = GetConfigPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var model = JsonSerializer.Deserialize<AppSettings>(json);
                    _proxyAddress = model?.ProxyAddress;
                    if (GlobalProxyTextBox != null)
                    {
                        GlobalProxyTextBox.Text = _proxyAddress;
                    }
                    if (!string.IsNullOrWhiteSpace(_proxyAddress))
                        AddDownloadItem($"Loaded proxy: {_proxyAddress}", "Info");
                }
            }
            catch (Exception ex)
            {
                AddDownloadItem($"Load settings failed: {ex.Message}", "Error");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var path = GetConfigPath();
                var json = JsonSerializer.Serialize(new AppSettings { ProxyAddress = _proxyAddress });
                File.WriteAllText(path, json);
                AddDownloadItem("Settings saved.", "Success");
            }
            catch (Exception ex)
            {
                AddDownloadItem($"Save settings failed: {ex.Message}", "Error");
            }
        }

        private class AppSettings
        {
            public string? ProxyAddress { get; set; }
        }

        private async Task EnsureWebViewReadyAsync()
        {
            // wait until WebView control is attached to visual tree
            if (WebView2CompatControl == null)
                return;

            var tcs = new TaskCompletionSource<bool>();
            void OnAttached(object? s, VisualTreeAttachmentEventArgs e)
            {
                tcs.TrySetResult(true);
            }

            if (WebView != null && WebView2CompatControl.IsAttachedToVisualTree())
            {
                return;
            }

            WebView2CompatControl.AttachedToVisualTree += OnAttached;
            try
            {
                // also post to next UI tick to ensure layout pass
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.WhenAny(tcs.Task, Task.Delay(2000));
            }
            finally
            {
                WebView2CompatControl.AttachedToVisualTree -= OnAttached;
            }
        }

        private async Task InitializeWebView2()
        {
            if (_isInitialized) return;

            try
            {
                await EnsureWebViewReadyAsync();

                if (WebView is null)
                {
                    // Safeguard: schedule later if WebView not ready yet
                    await Dispatcher.UIThread.InvokeAsync(async () => await InitializeWebView2(), DispatcherPriority.Background);
                    return;
                }
                // initialize WebView2
                var environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    null,
                    new CoreWebView2EnvironmentOptions()
                    {
                        // AdditionalBrowserArguments = "--remote-debugging-port=9222 --remote-allow-origins=* --proxy-server=http://localhost:10809",
                        AdditionalBrowserArguments = $"--remote-debugging-port=9222 --remote-allow-origins=*{(!string.IsNullOrWhiteSpace(_proxyAddress) ? $" --proxy-server={_proxyAddress}" : string.Empty)}",
                    });

                await WebView.EnsureCoreWebView2Async(environment);

                if (WebView != null && !string.IsNullOrEmpty(_initialUrl))
                {
                    WebView.Source = new Uri(_initialUrl);
                }

                await InitializePlaywright();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitializeWebView2 failed: {ex.Message}");
            }
        }

        private async Task InitializePlaywright()
        {
            try
            {
                playwright = await Playwright.CreateAsync();

                browser = await playwright.Chromium.ConnectOverCDPAsync("http://localhost:9222");
                var contexts = browser.Contexts;

                if (contexts.Count == 0)
                {
                    Console.WriteLine("not found contexts");
                    return;
                }

                var context = contexts[0];
                var pages = context.Pages;

                if (pages.Count > 0)
                {
                    page = pages[0];

                    page.Response += Page_Response;

                    Console.WriteLine($"find page: {page.Url}");
                }
                else
                {
                    Console.WriteLine("not find page，waiting...");
                    page = await context.WaitForPageAsync(new() { Timeout = 10000 });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Playwright InitializePlaywright failed: {ex.Message}");
            }
        }

        private void Page_Response(object? sender, IResponse e)
        {
            if(e.Url.Contains("google"))
            {
                
            }
        }

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WebView?.CoreWebView2?.OpenDevToolsWindow();
        }

        private void TextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (WebView is not null && !string.IsNullOrWhiteSpace(UrlTextBox?.Text))
                {
                    SampleHelper.Navigate(WebView, UrlTextBox.Text);
                }
                if (UrlTextBox != null) UrlTextBox.Text = UrlTextBox.Text;
            }
        }

        private async void SearchButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async Task PerformSearch()
        {
            if (!Controls.WebView2.IsSupported)
            {
                Console.WriteLine("WebView2 not support");
                return;
            }

            try
            {
                var searchText = SomethingTextBox.Text;

                if (playwright == null)
                {
                    Console.WriteLine("Playwright not initialize");
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                await page.WaitForSelectorAsync("textarea[name='q']");

                await page.FillAsync("textarea[name='q']", searchText);

                await page.ClickAsync("input[type='submit']");

                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10000 });
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("timeout，retry");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"search err: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private async void SomethingTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await DoPerformance(SomethingTextBox.Text!);
            }
        }

        private async void PasteButton_Click(object? sender, RoutedEventArgs e)
        {
            // use clipboard
            var text = await Clipboard?.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                SomethingTextBox.Text = text;

                await DoPerformance(text);

                await Clipboard.ClearAsync();
            }
        }

        private async Task DoPerformance(string text)
        {
            var url = UrlTextBox.Text;
            if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(url))
            {
                if (await IsHg())
                {
                    // Format: gameType_leagueId_ecid_betType_betItem_handicap_odds_amount
                    // Example: ft_100784_9785694_R_KuPS_+2/2.5_1.09_100
                    // Example: ft_100784_9785694_OU_Over_1.5_0.90_200

                    /**
                     *  head_R_FT_9758648
                     *  betType values:
                     *      Handicap                -> R
                     *      Handicap 1st Half       -> HR
                     *      Over/Under              -> OU
                     *      Over/Under   Half       -> HOU
                     *      1X2                     -> M
                     *      1X2 1st Half            -> HM
                     */

                    var (gameType, leagueId, ecid, betType, betItem, handicap, odds, amount, action) = ConvertMatch(text);

                    if (!string.IsNullOrWhiteSpace(gameType))
                    {
                        await Performance(gameType, leagueId, ecid, betType, betItem, handicap, odds, amount, action);
                    }
                }
                else
                {
                    await PerformSearch();
                }
            }
        }

        private (string gameType, string leagueId, string ecid, string betType, string betItem, string handicap, string odds, string amount, string action) ConvertMatch(string content)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                var splitContent = content.Split("_");
                var len = splitContent.Count();

                if (len >= 7)
                {
                    switch (len)
                    {
                        case 8:
                            {
                                return (splitContent[0], splitContent[1], splitContent[2], splitContent[3], splitContent[4], splitContent[5], splitContent[6], splitContent[7], "");
                            }
                        case 9:
                            {
                                return (splitContent[0], splitContent[1], splitContent[2], splitContent[3], splitContent[4], splitContent[5], splitContent[6], splitContent[7], splitContent[8]);
                            }
                        default:
                            {
                                return (splitContent[0], splitContent[1], splitContent[2], splitContent[3], splitContent[4], splitContent[5], splitContent[6], "", "");
                            }
                    }
                }
            }

            return default;
        }

        private async Task Performance(string gameType, string leagueId, string ecid, string betType, string betItem, string handicap, string odds, string amount, string action)
        {
            var opened = await FindMatchAcrossTabsAsync(gameType, leagueId: leagueId, ecid: ecid);

            if (opened)
            {

                var options = new List<string> { betItem, handicap, odds };
                await TryClickExactOddsNowAsync(page, gameType, ecid, betType, string.Join(" ", options), amount);
            }
        }

        private async Task<bool> TryClickExactOddsNowAsync(IPage page, string gameType, string ecid, string betType, string text, string amount)
        {
            var spliter = gameType == "ft" ? "FT" : "0";
            var parentDivsSelector = $"#body_{betType}_{spliter}_{ecid}";

            try
            {
                await page.Locator(parentDivsSelector).First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                return false;
            }

            var parentDivs = page.Locator(parentDivsSelector);
            var count = await parentDivs.CountAsync();
            if (count == 0) return false;

            for (int i = 0; i < count; i++)
            {
                var parentDiv = parentDivs.Nth(i);
                var loc = parentDiv.GetByText(text);

                if (await loc.CountAsync() == 0) continue;

                try
                {
                    await loc.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });

                    if (await loc.IsEnabledAsync())
                    {
                        await loc.ClickAsync(new LocatorClickOptions
                        {
                            Timeout = 2000,
                            Force = false
                        });

                        return await Order(amount);
                    }
                }
                catch (TimeoutException)
                {
                    continue;
                }
            }
            return false;
        }

        private async Task<bool> Order(string amount)
        {
            try
            {
                var betslip = page.Locator("#betslip_show");
                await betslip.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

                var betSlipOnElement = page.Locator(".bet_slip.on");
                await betSlipOnElement.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

                var betGoldBgPc = page.Locator("#bet_gold_bg_pc");
                await betGoldBgPc.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                
                await page.WaitForFunctionAsync("() => !document.querySelector('#bet_gold_bg_pc').classList.contains('noenter')", new PageWaitForFunctionOptions { Timeout = 5000 });

                var loc = page.Locator("#bet_gold_pc");
                await loc.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

                if (await loc.IsEnabledAsync() && await loc.IsVisibleAsync())
                {
                    await loc.ClearAsync();
                    
                    await loc.ClickAsync();
                    
                    await Task.Delay(1000);

                    if (!string.IsNullOrWhiteSpace(amount))
                    {
                        await loc.FillAsync(amount);

                        await loc.PressAsync("Space");
                    }
                    
                    await Task.Delay(300);

                    await ShowLimit();

                    return true;
                }
            }
            catch (TimeoutException)
            {

            }

            return false;
        }

        private async Task ShowLimit()
        {
            try
            {
                var loc = page.Locator("#div_showlimit");
                if (await loc.IsVisibleAsync() && await loc.IsEnabledAsync())
                {
                    await loc.ClickAsync();
                }
            }
            catch { }
        }

        private async Task CloseOrder()
        {
            try
            {
                await page.EvaluateAsync(@"
                    const element = document.querySelector('#order_close');
                    if (element) {
                        element.click();
                    }
                ");
            }
            catch
            {

            }
        }

        private async Task<bool> IsHg()
        {
            try
            {
                return await ElementExists("#parlay_page");
            }
            catch
            {

            }

            return false;
        }

        private async Task<bool> FindMatchAcrossTabsAsync(
            string gameType,
            string leagueId,
            string ecid
            )
        {
            await CloseOrder();

            if (await TryFindMatchInEarlyAsync(gameType, $"#mainShow_{ecid}", $"#league_{leagueId}"))
                return true;

            if (await TryFindMatchInTodayAsync(gameType, $"#game_{ecid}", $"#mainShow_{ecid}", $"#LEG_{leagueId}"))
                return true;

            return false;
        }

        private async Task<bool> TryFindMatchInTodayAsync(
            string gameType,
            string gameSelector,
            string matchSelector,
            string leagueSelector)
        {

            await GoToTabAsync("#today_page");

            await GoToGameAsync($"#symbol_{gameType}");

            if (!await CheckSelectorExist(gameSelector!))
                return false;

            await EnsureLeagueExpandedAsync(gameSelector, leagueSelector);

            if (await TryOpenMatchByIdNowAsync(matchSelector)) return true;

            return false;
        }

        private async Task EnsureLeagueExpandedAsync(string matchSelector, string leagueSelector)
        {
            try
            {
                var body = page.Locator(matchSelector);
                if (!await body.IsVisibleAsync())
                {
                    await page.Locator(leagueSelector).ClickAsync();
                    try
                    {
                        await body.WaitForAsync(new LocatorWaitForOptions
                        {
                            State = WaitForSelectorState.Visible,
                            Timeout = 4000
                        });
                    }
                    catch
                    {
                        
                    }
                }
            }
            catch
            {
                
            }
        }

        private async Task<bool> TryFindMatchInEarlyAsync(
            string gameType,
            string matchSelector,
            string leagueSelector)
        {
            await GoToTabAsync("#early_page");

            await GoToGameAsync($"#symbol_{gameType}");

            if (!await CheckSelectorExist(leagueSelector))
                return false;

            var regionBodySelector = string.Empty;
            var regionHeadSelector = string.Empty;

            var visbility = await CheckSelectorVisbileAsync(leagueSelector);
            if (!visbility)
            {
                var parentLocator = page.Locator(leagueSelector).Locator("..");
                var parentId = await parentLocator.GetAttributeAsync("id");

                regionBodySelector = $"#{parentId}";
                regionHeadSelector = $"#{parentId?.Replace("body", "head")}";
            }

            if (!string.IsNullOrWhiteSpace(regionBodySelector) && !string.IsNullOrWhiteSpace(regionHeadSelector))
            {
                await EnsureRegionExpandedAsync(regionBodySelector!, regionHeadSelector!);
            }

            if (!string.IsNullOrWhiteSpace(leagueSelector))
            {
                await TryOpenLeagueIfExistsAsync(leagueSelector!);
            }

            if (await TryOpenMatchByIdNowAsync(matchSelector)) return true;

            return false;
        }

        private async Task<bool> GoToTabAsync(string tabSelector)
        {
            try
            {
                await page.Locator(tabSelector).ClickAsync();
                await WaitLoadingHiddenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> GoToGameAsync(string gameType)
        {
            try
            {
                await page.Locator(gameType).ClickAsync();
                await WaitLoadingHiddenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task WaitLoadingHiddenAsync(int timeoutMs = 20000)
        {
            try
            {
                await page.Locator("#body_loading").WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = timeoutMs
                });
            }
            catch
            {

            }
        }

        private async Task EnsureRegionExpandedAsync(string regionBodySelector, string regionHeadSelector)
        {
            try
            {
                var body = page.Locator(regionBodySelector);
                if (!await body.IsVisibleAsync())
                {
                    await page.Locator(regionHeadSelector).ClickAsync();
                    try
                    {
                        await body.WaitForAsync(new LocatorWaitForOptions
                        {
                            State = WaitForSelectorState.Visible,
                            Timeout = 4000
                        });
                    }
                    catch
                    {
                        
                    }
                }
            }
            catch
            {

            }
        }

        private async Task<bool> CheckSelectorVisbileAsync(string selector)
        {
            var body = page.Locator(selector);
            return await body.IsVisibleAsync();
        }

        private async Task<bool> TryOpenLeagueIfExistsAsync(string leagueSelector)
        {
            try
            {
                var league = page.Locator(leagueSelector);
                //var handle = await league.ElementHandleAsync(new LocatorElementHandleOptions { Timeout = 0 });
                //if (handle is null) return false;
                var exist = await league.CountAsync() > 0;
                if (!exist) return false;

                await league.ClickAsync();
                await WaitLoadingHiddenAsync();
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> CheckSelectorExist(string selector)
        {
            var element = await page.QuerySelectorAsync(selector);
            return element != null;
        }

        private async Task<bool> TryOpenMatchByIdNowAsync(string matchSelector)
        {
            try
            {
                var loc = page.Locator(matchSelector);
                //var handle = await loc.ElementHandleAsync(new LocatorElementHandleOptions { Timeout = 0 });
                //if (handle is null) return false;
                var exist = await loc.CountAsync() > 0;
                if (!exist) return false;

                await loc.ClickAsync();
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> ElementExists(string selector)
        {
            var count = await page.Locator(selector).CountAsync();
            return count > 0;
        }

        private async void Download_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await DownloadYoutubeVideo();
        }

        private async void YoutubeTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await DownloadYoutubeVideo();
            }
        }

        private async Task DownloadYoutubeVideo()
        {
            var url = YoutubeTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                AddDownloadItem("Please enter a valid YouTube link.", "Error");
                return;
            }

            try
            {
                AddDownloadItem($"Processing: {url}", "Info");

                YoutubeClient youtube;
                if (!string.IsNullOrWhiteSpace(_proxyAddress))
                {
                    try
                    {
                        var httpClientHandler = new HttpClientHandler
                        {
                            Proxy = new WebProxy(_proxyAddress),
                            UseProxy = true,
                        };
                        var httpClient = new HttpClient(httpClientHandler);
                        youtube = new YoutubeClient(httpClient);
                        AddDownloadItem($"Using proxy: {_proxyAddress}", "Info");
                    }
                    catch (Exception ex)
                    {
                        AddDownloadItem($"Invalid proxy address: {ex.Message}", "Error");
                        return;
                    }
                }
                else
                {
                    youtube = new YoutubeClient();
                }

                var videoId = VideoId.Parse(url);
                var video = await youtube.Videos.GetAsync(videoId);
                AddDownloadItem($"Video Title: {video.Title}", "Info");
                AddDownloadItem($"Video Author: {video.Author.ChannelTitle}", "Info");

                // Get video stream information
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(url);

                // Select the highest quality mixed stream (containing video and audio)
                var streamInfo = streamManifest.GetMuxedStreams()
                    .Where(s => s.Container == Container.Mp4)
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();

                if (streamInfo == null)
                {
                    AddDownloadItem("Failed to find a downloadable video stream.", "Error");
                    return;
                }

                AddDownloadItem($"Found best quality stream: {streamInfo.VideoQuality.Label}", "Info");
                AddDownloadItem($"File size: {streamInfo.Size.MegaBytes:F1} MB", "Info");

                // Create download directory
                var downloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "YouTube Downloads");
                Directory.CreateDirectory(downloadDir);

                // Clean up invalid characters in the file name
                var fileName = string.Join("", video.Title.Split(Path.GetInvalidFileNameChars()));
                var filePath = Path.Combine(downloadDir, $"{fileName}.{streamInfo.Container.Name}");

                AddDownloadItem($"Starting download to: {filePath}", "Info");

                // Download video
                var progress = new Progress<double>(p =>
                {
                    var percentage = (p * 100).ToString("F1");
                    // This could be used to update the UI with progress, for now, we just log it.
                    Console.WriteLine($"Download progress: {percentage}%");
                });

                await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progress);

                AddDownloadItem($"Download finished: {filePath}", "Success");
            }
            catch (ArgumentException)
            {
                AddDownloadItem($"Invalid YouTube link or video ID: {url}", "Error");
            }
            catch (Exception ex)
            {
                AddDownloadItem($"An unknown error occurred: {ex.Message}", "Error");
            }
        }
        
        private async void ApplyProxy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _proxyAddress = GlobalProxyTextBox.Text;
            AddDownloadItem($"Applying new proxy setting: {_proxyAddress}", "Info");

            // persist settings
            SaveSettings();

            AddDownloadItem("Proxy saved. Restarting app to apply settings...", "Info");
            RestartApp();
        }

        private void RestartApp()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath)!
                    };
                    Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                AddDownloadItem($"Failed to restart automatically: {ex.Message}", "Error");
            }
            finally
            {
                // Exit current process
                Environment.Exit(0);
            }
        }

        private async Task CleanupAndRecreateWebViewAsync()
        {
            try
            {
                // Detach page event
                if (page != null)
                {
                    page.Response -= Page_Response;
                }

                // Close browser connection
                if (browser != null)
                {
                    try { await browser.CloseAsync(); } catch { }
                    browser = null;
                }

                // Dispose playwright
                if (playwright != null)
                {
                    try { playwright.Dispose(); } catch { }
                    playwright = null;
                }
            }
            catch { }

            page = null;

            // Replace the WebView2 control to avoid reusing an initialized instance
            WebView2CompatControl?.ReplaceWebView2();

            // Allow re-init
            _isInitialized = false;
        }

        private void AddDownloadItem(string message, string type)
        {
            var listBoxItem = new ListBoxItem
            {
                Content = $"[{DateTime.Now:HH:mm:ss}] [{type}] {message}"
            };
            
            switch (type)
            {
                case "Error":
                    listBoxItem.Foreground = Brushes.Red;
                    break;
                case "Success":
                    listBoxItem.Foreground = Brushes.Green;
                    break;
                case "Info":
                    listBoxItem.Foreground = Brushes.Blue;
                    break;
                default:
                    listBoxItem.Foreground = Brushes.Black;
                    break;
            }
            
            if(DownloadList?.Items.Count > 5)
            {
                DownloadList?.Items.Clear();
            }

            DownloadList?.Items?.Add(listBoxItem);
            
            // Auto-scroll to the latest item
            if (DownloadList?.Items?.Count > 0)
            {
                if (DownloadList?.Items.LastOrDefault() is { } lastItem)
                {
                    DownloadList.ScrollIntoView(lastItem);
                }
            }
        }

        private async void GoogleButton(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var url = UrlTextBox.Text;
            if (!string.IsNullOrWhiteSpace(url) && url.Contains("google"))
            {
                await PerformSearch();
            }
        }
    }
}