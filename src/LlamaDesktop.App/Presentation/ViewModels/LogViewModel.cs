using System.Collections.ObjectModel;
using LlamaDesktop.App.Presentation;

namespace LlamaDesktop.App.Presentation.ViewModels;

public sealed class LogViewModel : ObservableObject
{
    public ObservableCollection<string> Lines { get; } = new();

    public void Append(string text)
    {
        Lines.Add(text);
        while (Lines.Count > 2000) Lines.RemoveAt(0);
        OnPropertyChanged(nameof(Lines));
    }

    public void Clear() => Lines.Clear();
}
