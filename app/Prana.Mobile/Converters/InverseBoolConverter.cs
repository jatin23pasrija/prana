using System.Globalization;

namespace Prana.Mobile.Converters;

/// <summary>
/// Inverts a boolean, for the common case of showing one thing when another is hidden.
/// </summary>
/// <remarks>
/// Used for empty states and for the incomplete-record notice, where the alternative is a second
/// property on the view model that exists only to say "not that one".
/// </remarks>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}
