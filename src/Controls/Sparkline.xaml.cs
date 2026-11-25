using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameStateAssistant.Controls
{
    public partial class Sparkline : UserControl
    {
        public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
            nameof(LineColor), typeof(Color), typeof(Sparkline),
            new PropertyMetadata((Color)ColorConverter.ConvertFromString("#22D3EE"), OnVisualChanged));

        public static readonly DependencyProperty MinValueProperty = DependencyProperty.Register(
            nameof(MinValue), typeof(double), typeof(Sparkline), new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
            nameof(MaxValue), typeof(double), typeof(Sparkline), new PropertyMetadata(100.0));

        private readonly Queue<double> _samples = new Queue<double>();
        private const int MaxSamples = 30; // 2秒一次 * 30 = 60秒历史窗口

        public Color LineColor
        {
            get => (Color)GetValue(LineColorProperty);
            set => SetValue(LineColorProperty, value);
        }

        public double MinValue
        {
            get => (double)GetValue(MinValueProperty);
            set => SetValue(MinValueProperty, value);
        }

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        public Sparkline()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Redraw();
        }

        public void PushSample(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            _samples.Enqueue(value);
            while (_samples.Count > MaxSamples)
                _samples.Dequeue();
            Redraw();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Sparkline sp)
            {
                sp.LinePoly.Stroke = new SolidColorBrush(sp.LineColor);
                sp.AreaTop.Color = Color.FromArgb(0x38, sp.LineColor.R, sp.LineColor.G, sp.LineColor.B);
            }
        }

        private void Redraw()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 || h <= 2 || _samples.Count == 0) return;

            var list = _samples.ToList();
            if (list.Count == 1) list.Add(list[0]);

            double min = MinValue;
            double max = Math.Max(min + 1, MaxValue);
            double padY = 2.0;
            double drawH = Math.Max(1.0, h - padY * 2);

            var linePts = new PointCollection(list.Count);
            var areaPts = new PointCollection(list.Count + 2);

            for (int i = 0; i < list.Count; i++)
            {
                double x = (double)i / (list.Count - 1) * w;
                double norm = Math.Max(0, Math.Min(1, (list[i] - min) / (max - min)));
                double y = padY + drawH * (1.0 - norm);
                var pt = new Point(x, y);
                linePts.Add(pt);
                areaPts.Add(pt);
            }

            areaPts.Add(new Point(w, h));
            areaPts.Add(new Point(0, h));

            LinePoly.Points = linePts;
            AreaPoly.Points = areaPts;
        }
    }
}
