using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace LlamaDesktop.App.Presentation.Converters;

/// <summary>
/// 将完整模型路径转换为文件名用于显示；完整路径经 ToolTip 保留。
/// </summary>
public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFileName(path);
        }
        return value ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
