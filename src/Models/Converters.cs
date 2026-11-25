using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GameStateAssistant.Models;

namespace GameStateAssistant
{
    public static class StatusConverters
    {
        public static readonly IValueConverter BgConverter = new StatusBrushConverter(isBg: true);
        public static readonly IValueConverter FgConverter = new StatusBrushConverter(isBg: false);
        public static readonly IValueConverter BoolToVisConverter = new BoolToVisibilityConverter();
        public static readonly IValueConverter InverseBoolToVisConverter = new BoolToVisibilityConverter(invert: true);
        public static readonly IValueConverter PermissionIconConverter = new PermissionGlyphConverter();

        private sealed class StatusBrushConverter : IValueConverter
        {
            private readonly bool _isBg;
            public StatusBrushConverter(bool isBg) => _isBg = isBg;

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var st = value is CheckStatus s ? s : CheckStatus.Info;
                string hex = _isBg
                    ? st switch
                    {
                        CheckStatus.Optimal => "#2010B981",
                        CheckStatus.Attention => "#22F59E0B",
                        CheckStatus.Critical => "#24EF4444",
                        _ => "#1EFFFFFF"
                    }
                    : st switch
                    {
                        CheckStatus.Optimal => "#34D399",
                        CheckStatus.Attention => "#FBBF24",
                        CheckStatus.Critical => "#F87171",
                        _ => "#94A3B8"
                    };
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }

        private sealed class BoolToVisibilityConverter : IValueConverter
        {
            private readonly bool _invert;
            public BoolToVisibilityConverter(bool invert = false) => _invert = invert;

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                bool b = value is true;
                if (_invert) b = !b;
                return b ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }

        private sealed class PermissionGlyphConverter : IValueConverter
        {
            // Segoe Fluent Icons: E945=闪电(免权限), E72E=锁(安全权衡), EA18=盾牌(需提权)
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is CheckItem item)
                {
                    if (!item.CanAutoFix) return "\uE946"; // 信息
                    if (item.ExcludeFromBatchFix) return "\uE72E"; // 锁
                    return item.RequiresAdmin ? "\uEA18" : "\uE945"; // 盾牌 vs 闪电
                }
                return "\uE946";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
