using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using LlamaDesktop.App.Presentation.ViewModels;

namespace LlamaDesktop.App;

public partial class ShellWindow : Window
{
    private readonly WebView2 _webView;

    public ShellWindow(ShellViewModel viewModel, Microsoft.Web.WebView2.Wpf.WebView2 webView)
    {
        InitializeComponent();
        DataContext = viewModel;
        _webView = webView;
        WebHostGrid.Children.Add(webView);
    }
}
