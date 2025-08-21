using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Linq;

namespace Avalonia.WebView2.Demo;

public partial class WebView2Compat : UserControl
{
    public WebView2Compat()
    {
        InitializeComponent();
    }

    // Expose the inner WebView2 instance dynamically from the visual tree
    public Avalonia.Controls.WebView2? WebView2
        => RootGrid?.Children?.OfType<Avalonia.Controls.WebView2>().LastOrDefault();

    // Replace inner WebView2 to allow re-initialization with a different CoreWebView2Environment
    public Avalonia.Controls.WebView2 ReplaceWebView2()
    {
        // Remove existing instances, if any
        var existing = RootGrid.Children.OfType<Avalonia.Controls.WebView2>().ToList();
        foreach (var item in existing)
        {
            RootGrid.Children.Remove(item);
        }

        // Create and add a fresh WebView2 control
        var newWebView = new Avalonia.Controls.WebView2();
        RootGrid.Children.Add(newWebView);
        return newWebView;
    }
}