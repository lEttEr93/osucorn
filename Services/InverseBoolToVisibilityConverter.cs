using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OsuCorn.Services;

/// <summary>true → Collapsed、false → Visible。用于「mania 行显示下拉框、其它行显示标签」这类二选一。</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
