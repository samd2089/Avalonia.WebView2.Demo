# Avalonia WebView2, Playwright, and YouTube Downloader Demo

This project is an educational demo application built with Avalonia UI. It showcases the integration and usage of `WebView2`, `Microsoft.Playwright` for web automation, and `YoutubeExplode` for downloading YouTube videos.

## Features

- **WebView2 Integration**: Demonstrates how to embed and control a `WebView2` browser component within an Avalonia desktop application.
- **Playwright Automation**: Uses Playwright to programmatically interact with the content inside `WebView2`.
- **YouTube Video Downloader**: A functional tool to download YouTube videos by providing a URL.
- **Global Proxy Support**: A centralized setting to configure an HTTP proxy for both `WebView2` browsing and YouTube downloads.

## Prerequisites

This project uses Playwright to interact with the WebView2 component. Before running the application, you need to install the necessary browser drivers. After building the project, run the following command in your terminal:

```powershell
# Replace netX.0 with your actual target framework folder, e.g., net8.0
.\bin\Debug\netX.0\playwright.ps1 install
```

For more details, refer to the [official Playwright for .NET documentation](https://playwright.dev/dotnet/docs/library).

## How to Use

### 1. WebView Browsing

1.  Navigate to the **WebView** tab.
2.  Enter a URL into the address bar (e.g., `https://www.google.com`).
3.  Click the **Go** button to load the page.

### Automation Features

This application also includes some automation capabilities powered by Playwright:

- **Google Search**: If the current URL in the address bar is a Google search page, you can type a query into the text box below it and press Enter to perform a search.

- **HG Match Finder**: If the current URL is a supported HG site, you can paste a specific match format string into the text box and press Enter or click "Paste" to automatically find and interact with the match.

  **Match Format**:
  `gameType_leagueId_ecid_betType_betItem_handicap_odds`

  **Examples**:

  - `ft_100784_9785694_R_KuPS_+2/2.5_1.09`
  - `ft_100784_9785694_OU_Over_1.5_0.90`

**`gameType` Values**:

| Game Type  | Value |
| ---------- | ----- |
| Football   | ft    |
| Basketball | bk    |

**`betType` Values**:

| Bet Type            | Value |
| ------------------- | ----- |
| Handicap            | R     |
| Handicap 1st Half   | HR    |
| Over/Under          | OU    |
| Over/Under 1st Half | HOU   |
| 1X2                 | M     |
| 1X2 1st Half        | HM    |

### 2. Downloading YouTube Videos

1.  Navigate to the **Youtube** tab.
2.  Enter the full URL of a YouTube video (e.g., `https://www.youtube.com/watch?v=dQw4w9WgXcQ`).
3.  Click the **Download** button.
4.  The download progress and status will be displayed in the list below.
5.  Completed downloads are saved to a `YouTubeDownloads` folder on your Desktop.

### 3. Configuring the Proxy

Both the WebView and the YouTube downloader can be routed through an HTTP proxy.

1.  Navigate to the **Setting** tab.
2.  In the **Global Proxy Setting** section, enter your proxy server address (e.g., `http://127.0.0.1:10809`).
3.  Click the **Save** button.
4.  The application will apply the setting, and the WebView component will automatically restart to use the new proxy.

## Disclaimer

This project is intended for educational and learning purposes only. It demonstrates how to work with various technologies in a .NET Avalonia application. The downloading of copyrighted content may be against the terms of service of the content provider. Users are responsible for their own actions and for complying with all applicable laws.
