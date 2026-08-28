using System.Collections.Specialized;
using System.Windows;
using LlamaDesktop.App.Presentation.ViewModels;
using Microsoft.Web.WebView2.Wpf;

namespace LlamaDesktop.App;

public partial class ShellWindow : Window
{
    private readonly WebView2 _webView;
    private readonly ShellViewModel _viewModel;

    public ShellWindow(ShellViewModel viewModel, WebView2 webView)
    {
        InitializeComponent();
        DataContext = viewModel;
        _webView = webView;
        _viewModel = viewModel;
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
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
            {
                // The VM caps Lines at 2000; rebuild the pane so the TextBox stays bounded too.
                RebuildLogText();
            }
            LogText.ScrollToEnd();
        }));
    }

    private void RebuildLogText()
    {
        LogText.Clear();
        foreach (var line in _viewModel.Log.Lines)
        {
            LogText.AppendText($"{line}\r\n");
        }
    }
}
