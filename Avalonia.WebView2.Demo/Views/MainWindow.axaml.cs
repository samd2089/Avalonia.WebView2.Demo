using System;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Diagnostics;
using Microsoft.Playwright;
using Microsoft.Web.WebView2.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Layout;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        
        // Status tracking related fields
        private readonly System.Diagnostics.Stopwatch _operationStopwatch = new System.Diagnostics.Stopwatch();
        private string _currentStep = "";
        private CancellationTokenSource? _timerCts;
        
        // AI Chat related fields
        private string? _apiKey;
        private readonly ObservableCollection<ChatMessage> _chatHistory = new ObservableCollection<ChatMessage>();

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            LoadChatSettings();
            InitializeChatUI();
            // Delay initialization until Window is opened to ensure visual tree is ready
            this.Opened += async (_, __) =>
            {
                await InitializeWebView2();
            };
        }
        
        // Status update related methods
        private void UpdateStatus(string message, bool startTimer = false, bool stopTimer = false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = message;
                }
                
                if (startTimer)
                {
                    _operationStopwatch.Restart();
                    _currentStep = message;
                    StartTimerUpdates();
                }
                
                if (stopTimer)
                {
                    _operationStopwatch.Stop();
                    StopTimerUpdates();
                    if (TimerTextBlock != null)
                    {
                        TimerTextBlock.Text = $"Total time: {_operationStopwatch.ElapsedMilliseconds}ms";
                    }
                }
            });
        }
        
        private void StartTimerUpdates()
        {
            StopTimerUpdates(); // Stop previous timer
            _timerCts = new CancellationTokenSource();
            
            Task.Run(async () =>
            {
                while (!_timerCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, _timerCts.Token);
                    
                    if (!_timerCts.Token.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (TimerTextBlock != null && _operationStopwatch.IsRunning)
                            {
                                TimerTextBlock.Text = $"Current step time: {_operationStopwatch.ElapsedMilliseconds}ms";
                            }
                        });
                    }
                }
            }, _timerCts.Token);
        }
        
        private void StopTimerUpdates()
        {
            _timerCts?.Cancel();
            _timerCts?.Dispose();
            _timerCts = null;
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
                    if (TodayFirstCheckBox != null)
                    {
                        TodayFirstCheckBox.IsChecked = model?.TodayFirst ?? false; // default: Early first
                    }
                    if (EnableGoToGameCheckBox != null)
                    {
                        EnableGoToGameCheckBox.IsChecked = model?.EnableGoToGame ?? true; // default: keep existing behavior
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
                var json = JsonSerializer.Serialize(new AppSettings
                {
                    ProxyAddress = _proxyAddress,
                    TodayFirst = TodayFirstCheckBox?.IsChecked ?? false,
                    EnableGoToGame = EnableGoToGameCheckBox?.IsChecked ?? true,
                });
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
            public bool? TodayFirst { get; set; }
            public bool? EnableGoToGame { get; set; }
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
                    Console.WriteLine("not find page, waiting...");
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
                Console.WriteLine("timeout, retry");
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
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            UpdateStatus($"Starting betting operation - Game: {gameType}, Match: {ecid}", startTimer: true);
            
            try
            {
                var opened = await FindMatchAcrossTabsAsync(gameType, leagueId: leagueId, ecid: ecid);

                if (opened)
                {
                    UpdateStatus("Attempting to click odds...");
                    var options = new List<string> { betItem, handicap, odds };
                    var success = await TryClickExactOddsNowAsync(page, gameType, ecid, betType, string.Join(" ", options), amount);
                    
                    if (success)
                    {
                        UpdateStatus($"Betting operation completed! Total time: {totalStopwatch.ElapsedMilliseconds}ms", stopTimer: true);
                    }
                    else
                    {
                        UpdateStatus($"Betting operation failed. Total time: {totalStopwatch.ElapsedMilliseconds}ms", stopTimer: true);
                    }
                }
                else
                {
                    UpdateStatus($"Match not found, operation failed. Total time: {totalStopwatch.ElapsedMilliseconds}ms", stopTimer: true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Execution exception: {ex.Message}. Total time: {totalStopwatch.ElapsedMilliseconds}ms", stopTimer: true);
            }
        }

        private async Task<bool> TryClickExactOddsNowAsync(IPage page, string gameType, string ecid, string betType, string text, string amount)
        {
            var spliter = gameType == "ft" ? "FT" : "0";
            var parentDivsSelector = $"#body_{betType}_{spliter}_{ecid}";

            try
            {
                await page.Locator(parentDivsSelector).First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
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
            UpdateStatus("Starting match search...", startTimer: true);
            
            try
            {
                UpdateStatus("Closing order window...");
                await CloseOrder();

                var todayFirst = TodayFirstCheckBox?.IsChecked == true;
                UpdateStatus($"Checking search strategy: {(todayFirst ? "Today first" : "Early first")}");

                if (todayFirst)
                {
                    UpdateStatus("Searching match in Today page...");
                    if (await TryFindMatchInTodayAsync(gameType, $"#game_{ecid}", $"#mainShow_{ecid}", $"#LEG_{leagueId}"))
                    {
                        UpdateStatus("Match found in Today page!", stopTimer: true);
                        return true;
                    }

                    UpdateStatus("Searching match in Early page...");
                    if (await TryFindMatchInEarlyAsync(gameType, $"#mainShow_{ecid}", $"#league_{leagueId}"))
                    {
                        UpdateStatus("Match found in Early page!", stopTimer: true);
                        return true;
                    }
                }
                else
                {
                    UpdateStatus("Searching match in Early page...");
                    if (await TryFindMatchInEarlyAsync(gameType, $"#mainShow_{ecid}", $"#league_{leagueId}"))
                    {
                        UpdateStatus("Match found in Early page!", stopTimer: true);
                        return true;
                    }

                    UpdateStatus("Searching match in Today page...");
                    if (await TryFindMatchInTodayAsync(gameType, $"#game_{ecid}", $"#mainShow_{ecid}", $"#LEG_{leagueId}"))
                    {
                        UpdateStatus("Match found in Today page!", stopTimer: true);
                        return true;
                    }
                }

                UpdateStatus("Match not found", stopTimer: true);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Match search failed: {ex.Message}", stopTimer: true);
                return false;
            }
        }

        private async Task<bool> TryFindMatchInTodayAsync(
            string gameType,
            string gameSelector,
            string matchSelector,
            string leagueSelector)
        {
            var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            UpdateStatus("Navigating to Today page...");
            await GoToTabAsync("#today_page");
            
            UpdateStatus($"Navigation to Today page completed ({stepStopwatch.ElapsedMilliseconds}ms)");
            stepStopwatch.Restart();

            if (EnableGoToGameCheckBox?.IsChecked == true || gameType == "bk")
            {
                UpdateStatus($"Navigating to game type: {gameType}...");
                await GoToGameAsync($"#symbol_{gameType}");
                UpdateStatus($"Navigation to game type completed ({stepStopwatch.ElapsedMilliseconds}ms)");
                stepStopwatch.Restart();
            }

            UpdateStatus("Checking if game element exists...");
            if (!await CheckSelectorExist(gameSelector!))
            {
                UpdateStatus($"Game element does not exist: {gameSelector} ({stepStopwatch.ElapsedMilliseconds}ms)");
                return false;
            }
            
            UpdateStatus($"Game element exists ({stepStopwatch.ElapsedMilliseconds}ms)");
            stepStopwatch.Restart();

            UpdateStatus("Ensuring league is expanded...");
            await EnsureLeagueExpandedAsync(gameSelector, leagueSelector);
            UpdateStatus($"League expansion operation completed ({stepStopwatch.ElapsedMilliseconds}ms)");
            stepStopwatch.Restart();

            UpdateStatus("Attempting to open match...");
            if (await TryOpenMatchByIdNowAsync(matchSelector))
            {
                UpdateStatus($"Successfully opened match ({stepStopwatch.ElapsedMilliseconds}ms)");
                return true;
            }
            
            UpdateStatus($"Failed to open match ({stepStopwatch.ElapsedMilliseconds}ms)");
            return false;
        }

        private async Task EnsureLeagueExpandedAsync(string matchSelector, string leagueSelector)
        {
            try
            {
                var body = page.Locator(matchSelector);
                if (!await body.IsVisibleAsync())
                {
                    await page.Locator(leagueSelector).AllAsync().ContinueWith(async locs =>
                    {
                        if (locs.Result.Count > 0)
                        {
                            var firstLeague = locs.Result[0];
                            await firstLeague.ClickAsync();
                        }
                    });
                    // await page.Locator(leagueSelector).ClickAsync();
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
            var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            UpdateStatus("Navigating to Early page...");
            await GoToTabAsync("#early_page");
            UpdateStatus($"Navigation to Early page completed ({stepStopwatch.ElapsedMilliseconds}ms)");
            stepStopwatch.Restart();

            if (EnableGoToGameCheckBox?.IsChecked == true || gameType == "bk")
            {
                UpdateStatus($"Navigating to game type: {gameType}...");
                await GoToGameAsync($"#symbol_{gameType}");
                UpdateStatus($"Navigation to game type completed ({stepStopwatch.ElapsedMilliseconds}ms)");
                stepStopwatch.Restart();
            }

            UpdateStatus("Checking if league element exists...");
            if (!await CheckSelectorExist(leagueSelector))
            {
                UpdateStatus($"League element does not exist: {leagueSelector} ({stepStopwatch.ElapsedMilliseconds}ms)");
                return false;
            }
            
            UpdateStatus($"League element exists ({stepStopwatch.ElapsedMilliseconds}ms)");
            stepStopwatch.Restart();

            var regionBodySelector = string.Empty;
            var regionHeadSelector = string.Empty;

            UpdateStatus("Checking league visibility...");
            var visbility = await CheckSelectorVisbileAsync(leagueSelector);
            if (!visbility)
            {
                UpdateStatus("League not visible, getting parent element...");
                var parentLocator = page.Locator(leagueSelector).Locator("..");
                var parentId = await parentLocator.GetAttributeAsync("id");

                regionBodySelector = $"#{parentId}";
                regionHeadSelector = $"#{parentId?.Replace("body", "head")}";
                UpdateStatus($"Parent element retrieval completed ({stepStopwatch.ElapsedMilliseconds}ms)");
            }
            else
            {
                UpdateStatus($"League is already visible ({stepStopwatch.ElapsedMilliseconds}ms)");
            }
            stepStopwatch.Restart();

            if (!string.IsNullOrWhiteSpace(regionBodySelector) && !string.IsNullOrWhiteSpace(regionHeadSelector))
            {
                UpdateStatus("Ensuring region is expanded...");
                await EnsureRegionExpandedAsync(regionBodySelector!, regionHeadSelector!);
                UpdateStatus($"Region expansion operation completed ({stepStopwatch.ElapsedMilliseconds}ms)");
                stepStopwatch.Restart();
            }

            if (!string.IsNullOrWhiteSpace(leagueSelector))
            {
                UpdateStatus("Attempting to open league...");
                await TryOpenLeagueIfExistsAsync(leagueSelector!);
                UpdateStatus($"League opening operation completed ({stepStopwatch.ElapsedMilliseconds}ms)");
                stepStopwatch.Restart();
            }

            UpdateStatus("Attempting to open match...");
            if (await TryOpenMatchByIdNowAsync(matchSelector))
            {
                UpdateStatus($"Successfully opened match ({stepStopwatch.ElapsedMilliseconds}ms)");
                return true;
            }
            
            UpdateStatus($"Failed to open match ({stepStopwatch.ElapsedMilliseconds}ms)");
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

        private async Task WaitLoadingHiddenAsync(int timeoutMs = 30000)
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
        
        // AI Chat related methods
        private void InitializeChatUI()
        {
            // Don't set ItemsSource, we'll manage Items directly
            // This avoids the "ItemsSource is in use" error
        }
        
        private async void SaveApiKey_Click(object? sender, RoutedEventArgs e)
        {
            _apiKey = ApiKeyTextBox?.Text;
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                try
                {
                    var settings = LoadChatSettings();
                    settings.ApiKey = _apiKey;
                    SaveChatSettings(settings);
                    AddChatMessage("System", "API Key saved successfully.", "#E0E0E0");
                    UpdateStatus("OpenRouter API Key saved");
                }
                catch (Exception ex)
                {
                    AddChatMessage("System", $"Error saving API Key: {ex.Message}", "#FFB0B0");
                    UpdateStatus($"Error saving API Key: {ex.Message}");
                }
            }
            else
            {
                AddChatMessage("System", "Please enter a valid API Key.", "#FFB0B0");
            }
        }
        
        private async void TestConnection_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                AddChatMessage("System", "Please save an API Key first.", "#FFB0B0");
                return;
            }
            
            UpdateStatus("Testing OpenRouter connection...", startTimer: true);
            
            try
            {
                var testMessage = new
                {
                    model = GetSelectedModel(),
                    messages = new[]
                    {
                        new { role = "user", content = "Hello! This is a connection test." }
                    },
                    max_tokens = 50
                };
                
                var response = await SendRequestToOpenRouter(testMessage);
                if (response.HasValue)
                {
                    AddChatMessage("System", "Connection test successful!", "#B0FFB0");
                    UpdateStatus("OpenRouter connection test successful", stopTimer: true);
                }
                else
                {
                    AddChatMessage("System", "Connection test failed. Please check your API Key and try again.", "#FFB0B0");
                    UpdateStatus("OpenRouter connection test failed", stopTimer: true);
                }
            }
            catch (Exception ex)
            {
                AddChatMessage("System", $"Connection test error: {ex.Message}", "#FFB0B0");
                UpdateStatus($"Connection test error: {ex.Message}", stopTimer: true);
            }
        }
        
        private async void DebugApi_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                AddChatMessage("System", "Please save an API Key first.", "#FFB0B0");
                return;
            }
            
            AddChatMessage("Debug", "=== API Debug Information ===", "#F0F0F0");
            AddChatMessage("Debug", $"API Key Length: {_apiKey.Length}", "#F0F0F0");
            AddChatMessage("Debug", $"API Key Prefix: {_apiKey.Substring(0, Math.Min(10, _apiKey.Length))}...", "#F0F0F0");
            AddChatMessage("Debug", $"Selected Model: {GetSelectedModel()}", "#F0F0F0");
            AddChatMessage("Debug", $"OpenRouter Endpoint: https://openrouter.ai/api/v1/chat/completions", "#F0F0F0");
            
            // Test basic HTTP connectivity
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.GetAsync("https://openrouter.ai");
                AddChatMessage("Debug", $"OpenRouter Website Accessible: {response.IsSuccessStatusCode} ({response.StatusCode})", "#F0F0F0");
            }
            catch (Exception ex)
            {
                AddChatMessage("Debug", $"OpenRouter Website Access Error: {ex.Message}", "#FFB0B0");
            }
            
            // Test API with minimal request
            try
            {
                AddChatMessage("Debug", "Testing minimal API request...", "#F0F0F0");
                
                var minimalRequest = new
                {
                    model = "openai/gpt-4o-mini", // Use a reliable model
                    messages = new[]
                    {
                        new { role = "user", content = "Hi" }
                    },
                    max_tokens = 10
                };
                
                var response = await SendRequestToOpenRouter(minimalRequest);
                if (response.HasValue)
                {
                    AddChatMessage("Debug", "Minimal API request successful!", "#B0FFB0");
                }
            }
            catch (Exception ex)
            {
                AddChatMessage("Debug", $"Minimal API request failed: {ex.Message}", "#FFB0B0");
            }
            
            AddChatMessage("Debug", "=== Debug Complete ===", "#F0F0F0");
        }
        
        private async void SendMessage_Click(object? sender, RoutedEventArgs e)
        {
            await SendChatMessage();
        }
        
        private async void MessageTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                await SendChatMessage();
            }
        }
        
        private async Task SendChatMessage()
        {
            var message = MessageTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;
                
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                AddChatMessage("System", "Please save an API Key first.", "#FFB0B0");
                return;
            }
            
            // Clear input
            if (MessageTextBox != null)
                MessageTextBox.Text = string.Empty;
            
            // Add user message to UI
            AddChatMessage("User", message, "#B0D4FF");
            
            UpdateStatus("Sending message to AI...", startTimer: true);
            
            try
            {
                // Prepare messages for API - include the current message
                var apiMessages = new List<object>();
                
                // Add previous conversation context (last 9 messages to leave room for current message)
                var previousMessages = _chatHistory
                    .Where(m => m.Role == "User" || m.Role == "Assistant")
                    .TakeLast(9) // Leave room for current user message
                    .Select(m => new { role = m.Role.ToLower(), content = m.Content });
                
                apiMessages.AddRange(previousMessages);
                
                // Add current user message
                apiMessages.Add(new { role = "user", content = message });
                
                // Ensure we have at least one message
                if (apiMessages.Count == 0)
                {
                    apiMessages.Add(new { role = "user", content = message });
                }
                
                var requestBody = new
                {
                    model = GetSelectedModel(),
                    messages = apiMessages.ToArray(),
                    max_tokens = 1000,
                    temperature = 0.7
                };
                
                var response = await SendRequestToOpenRouter(requestBody);
                
                if (response.HasValue && response.Value.TryGetProperty("choices", out var choices) && 
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("message", out var responseMessage) &&
                        responseMessage.TryGetProperty("content", out var content))
                    {
                        var aiResponse = content.GetString() ?? "No response";
                        AddChatMessage("Assistant", aiResponse, "#E0FFE0");
                        UpdateStatus("AI response received", stopTimer: true);
                    }
                    else
                    {
                        AddChatMessage("System", "Invalid response format from AI", "#FFB0B0");
                        UpdateStatus("Invalid AI response format", stopTimer: true);
                    }
                }
                else
                {
                    AddChatMessage("System", "No response from AI. Please try again.", "#FFB0B0");
                    UpdateStatus("No AI response received", stopTimer: true);
                }
            }
            catch (Exception ex)
            {
                AddChatMessage("System", $"Error: {ex.Message}", "#FFB0B0");
                UpdateStatus($"AI chat error: {ex.Message}", stopTimer: true);
            }
        }
        
        private void ClearChat_Click(object? sender, RoutedEventArgs e)
        {
            _chatHistory.Clear();
            ChatHistoryListBox?.Items?.Clear();
            AddChatMessage("System", "Chat history cleared.", "#E0E0E0");
            UpdateStatus("Chat history cleared");
        }
        
        private string GetSelectedModel()
        {
            var selectedItem = ModelComboBox?.SelectedItem as ComboBoxItem;
            return selectedItem?.Content?.ToString() ?? "anthropic/claude-3.5-sonnet";
        }
        
        private async Task<JsonElement?> SendRequestToOpenRouter(object requestBody)
        {
            try
            {
                var json = JsonSerializer.Serialize(requestBody);
                
                // Create a new HttpRequestMessage for each request
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(60);
                
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // Set headers on the request, not on the client
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Headers.Add("HTTP-Referer", "https://avalonia-webview2-demo.local");
                request.Headers.Add("X-Title", "Avalonia WebView2 Demo");
                
                var response = await httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<JsonElement>(responseString);
                    }
                    catch (JsonException jsonEx)
                    {
                        throw new Exception($"Failed to parse JSON response: {jsonEx.Message}. Raw response: {responseString}");
                    }
                }
                else
                {
                    // Parse error response if possible
                    try
                    {
                        var errorJson = JsonSerializer.Deserialize<JsonElement>(responseString);
                        if (errorJson.TryGetProperty("error", out var errorObj))
                        {
                            var errorMessage = "Unknown error";
                            var errorCode = "Unknown";
                            
                            if (errorObj.TryGetProperty("message", out var msgElement))
                                errorMessage = msgElement.GetString() ?? "Unknown error";
                            
                            if (errorObj.TryGetProperty("code", out var codeElement))
                                errorCode = codeElement.GetString() ?? "Unknown";
                            
                            throw new Exception($"OpenRouter API Error [{errorCode}]: {errorMessage}");
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse the error, return the raw response
                    }
                    
                    throw new Exception($"API Error: {response.StatusCode} - {responseString}");
                }
            }
            catch (HttpRequestException httpEx)
            {
                throw new Exception($"Network error: {httpEx.Message}");
            }
            catch (TaskCanceledException timeoutEx)
            {
                throw new Exception($"Request timeout: {timeoutEx.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Request failed: {ex.Message}");
            }
        }
        
        private void AddChatMessage(string role, string content, string backgroundColor)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Create UI element directly
                var border = new Border
                {
                    Margin = new Thickness(5),
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.Parse(backgroundColor))
                };
                
                var stackPanel = new StackPanel();
                
                var roleText = new TextBlock
                {
                    Text = role,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                
                var contentText = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap
                };
                
                var timestampText = new TextBlock
                {
                    Text = DateTime.Now.ToString("HH:mm:ss"),
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                
                stackPanel.Children.Add(roleText);
                stackPanel.Children.Add(contentText);
                stackPanel.Children.Add(timestampText);
                
                border.Child = stackPanel;
                
                // Add to ListBox directly
                var listBoxItem = new ListBoxItem { Content = border };
                ChatHistoryListBox?.Items?.Add(listBoxItem);
                
                // Also store in collection for API context
                var message = new ChatMessage
                {
                    Role = role,
                    Content = content,
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    BackgroundColor = backgroundColor
                };
                
                _chatHistory.Add(message);
                
                // Auto-scroll to bottom
                if (ChatHistoryListBox != null && ChatHistoryListBox.Items?.Count > 0)
                {
                    var lastItem = ChatHistoryListBox.Items[ChatHistoryListBox.Items.Count - 1];
                    if (lastItem != null)
                        ChatHistoryListBox.ScrollIntoView(lastItem);
                }
            });
        }
        
        private ChatSettings LoadChatSettings()
        {
            try
            {
                var path = GetChatConfigPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<ChatSettings>(json) ?? new ChatSettings();
                    _apiKey = settings.ApiKey;
                    if (ApiKeyTextBox != null && !string.IsNullOrWhiteSpace(_apiKey))
                    {
                        ApiKeyTextBox.Text = _apiKey;
                    }
                    return settings;
                }
            }
            catch (Exception ex)
            {
                AddChatMessage("System", $"Error loading chat settings: {ex.Message}", "#FFB0B0");
            }
            return new ChatSettings();
        }
        
        private void SaveChatSettings(ChatSettings settings)
        {
            try
            {
                var path = GetChatConfigPath();
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save chat settings: {ex.Message}");
            }
        }
        
        private string GetChatConfigPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Avalonia.WebView2.Demo");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "chat-settings.json");
        }
    }
    
    // Chat message model
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string BackgroundColor { get; set; } = "#FFFFFF";
    }
    
    // Chat settings model
    public class ChatSettings
    {
        public string? ApiKey { get; set; }
    }
}