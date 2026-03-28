using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MajdataEdit_Neo.Utils;

namespace MajdataEdit_Neo.Converters;

public class KeyGestureStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return null;
        return KeyGestureUtil.TryParse(s);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
