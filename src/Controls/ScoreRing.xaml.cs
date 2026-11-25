using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameStateAssistant.Controls
{
    public partial class ScoreRing : UserControl
    {
        public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
            nameof(Score), typeof(double), typeof(ScoreRing),
            new PropertyMetadata(0.0, OnScoreChanged));

        public static readonly DependencyProperty AnimatedProgressProperty = DependencyProperty.Register(
            nameof(AnimatedProgress), typeof(double), typeof(ScoreRing),
            new PropertyMetadata(0.0, OnAnimatedProgressChanged));

        public static bool AnimationsEnabled { get; set; } = true;

        public double Score
        {
            get => (double)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }

        private double AnimatedProgress
        {
            get => (double)GetValue(AnimatedProgressProperty);
            set => SetValue(AnimatedProgressProperty, value);
        }

        private const double SizePx = 104.0;
        private const double Radius = 44.0;
        private const double StartAngleDeg = 135.0; // 从左下角开始顺时针扫过 270 度到右下角
        private const double MaxSweepDeg = 270.0;

        public ScoreRing()
        {
            InitializeComponent();
            TrackPath.Data = BuildArcGeometry(StartAngleDeg, MaxSweepDeg);
            RedrawProgress(0);
        }

        private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScoreRing ring) return;
            double target = Math.Max(0, Math.Min(100, (double)e.NewValue));
            ring.ScoreText.Text = target.ToString("0");
            ring.UpdateColors(target);

            if (AnimationsEnabled && SystemParameters.ClientAreaAnimation)
            {
                var anim = new DoubleAnimation(ring.AnimatedProgress, target, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ring.BeginAnimation(AnimatedProgressProperty, anim);
            }
            else
            {
                ring.BeginAnimation(AnimatedProgressProperty, null);
                ring.AnimatedProgress = target;
            }
        }

        private static void OnAnimatedProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScoreRing ring)
                ring.RedrawProgress((double)e.NewValue);
        }

        private void UpdateColors(double score)
        {
            if (score >= 90)
            {
                GradStart.Color = (Color)ColorConverter.ConvertFromString("#10B981");
                GradEnd.Color = (Color)ColorConverter.ConvertFromString("#22D3EE");
            }
            else if (score >= 75)
            {
                GradStart.Color = (Color)ColorConverter.ConvertFromString("#FBBF24");
                GradEnd.Color = (Color)ColorConverter.ConvertFromString("#22D3EE");
            }
            else
            {
                GradStart.Color = (Color)ColorConverter.ConvertFromString("#F87171");
                GradEnd.Color = (Color)ColorConverter.ConvertFromString("#FBBF24");
            }
        }

        private void RedrawProgress(double percent)
        {
            double sweep = MaxSweepDeg * Math.Max(0, Math.Min(100, percent)) / 100.0;
            if (sweep < 1.0)
            {
                ProgressPath.Data = Geometry.Empty;
                return;
            }
            ProgressPath.Data = BuildArcGeometry(StartAngleDeg, sweep);
        }

        private static Geometry BuildArcGeometry(double startDeg, double sweepDeg)
        {
            double cx = SizePx / 2.0;
            double cy = SizePx / 2.0;
            double startRad = startDeg * Math.PI / 180.0;
            double endRad = (startDeg + sweepDeg) * Math.PI / 180.0;

            var startPt = new Point(cx + Radius * Math.Cos(startRad), cy + Radius * Math.Sin(startRad));
            var endPt = new Point(cx + Radius * Math.Cos(endRad), cy + Radius * Math.Sin(endRad));

            var seg = new ArcSegment(endPt, new Size(Radius, Radius), 0, sweepDeg > 180.0, SweepDirection.Clockwise, true);
            var fig = new PathFigure(startPt, new[] { seg }, false);
            return new PathGeometry(new[] { fig });
        }
    }
}
