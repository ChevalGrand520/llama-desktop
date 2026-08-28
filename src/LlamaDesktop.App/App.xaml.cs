using System.Windows;

namespace LlamaDesktop.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CompositionRoot.Run(this);
    }
}
