using System.Collections.Specialized;
using System.Windows;
using LlamaDesktop.App.Presentation.ViewModels;
using Microsoft.Web.WebView2.Wpf;

namespace LlamaDesktop.App;

public partial class ShellWindow : Window
{
    private readonly WebView2 _webView;

    public ShellWindow(ShellViewModel viewModel, WebView2 webView)
    {
        InitializeComponent();
        DataContext = viewModel;
        _webView = webView;
        WebHostGrid.Children.Add(webView);
        viewModel.Log.Lines.CollectionChanged += OnLogLinesChanged;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ProcessExited can raise on a threadpool thread; marshal to the UI dispatcher.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    LogText.AppendText($"{item}\r\n");
                }
                LogText.ScrollToEnd();
            }
        }));
    }
}
