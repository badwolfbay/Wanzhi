using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Wanzhi.Rendering;

public class BubblesRenderer : IBackgroundEffectRenderer, IVariationOffsetRenderer
{
    private readonly Ellipse[] _bubbles;
    private readonly BubbleState[] _states;
    private int _activeCount;
    private readonly int _seedBase;
    private double _t;
    private double _variationOffset;
    private double _canvasWidth;
    private double _canvasHeight;

    private struct BubbleState
    {
        public double X;
        public double Y;
        public double Radius;
        public double Phase;
        public double DriftX;
        public double DriftY;
        public double Pulse;
    }

    public BubblesRenderer(double width, double height, int bubbleCount = 5, int seedBase = 0)
    {
        _canvasWidth = width;
        _canvasHeight = height;
        _seedBase = seedBase;

        var count = Math.Max(5, bubbleCount);
        _bubbles = new Ellipse[count];
        _states = new BubbleState[count];

        for (var i = 0; i < count; i++)
        {
            _bubbles[i] = new Ellipse
            {
                StrokeThickness = 0,
                Fill = new SolidColorBrush(Colors.Transparent),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
        }

        InitializeLayout();
    }

    public IEnumerable<UIElement> GetVisuals() => _bubbles;

    public void SetCanvasSize(double width, double height)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return;
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0) return;

        if (Math.Abs(_canvasWidth - width) < 0.01 && Math.Abs(_canvasHeight - height) < 0.01)
        {
            return;
        }

        _canvasWidth = width;
        _canvasHeight = height;
        InitializeLayout();
        Update(0);
    }

    public void SetVariationOffset(double offset)
    {
        _variationOffset = offset;
        InitializeLayout();
    }

    public void UpdateColor(Color baseColor, bool isDarkTheme)
    {
        var visibleCount = 0;
        for (var i = 0; i < _bubbles.Length; i++)
        {
            if (_bubbles[i].Visibility == Visibility.Visible)
            {
                visibleCount++;
            }
        }

        var visibleIndex = 0;
        for (var i = 0; i < _bubbles.Length; i++)
        {
            if (_bubbles[i].Visibility != Visibility.Visible)
            {
                continue;
            }

            var factor = visibleCount <= 1 ? 0.0 : (double)visibleIndex / (visibleCount - 1);
            visibleIndex++;

            byte alpha = (byte)(80 + factor * 175);
            byte r = (byte)(baseColor.R * (0.5 + 0.5 * factor) + 255 * (1 - (0.5 + 0.5 * factor)));
            byte g = (byte)(baseColor.G * (0.5 + 0.5 * factor) + 255 * (1 - (0.5 + 0.5 * factor)));
            byte b = (byte)(baseColor.B * (0.5 + 0.5 * factor) + 255 * (1 - (0.5 + 0.5 * factor)));

            if (isDarkTheme)
            {
                alpha = (byte)(30 + factor * 100);
                r = (byte)(baseColor.R * (0.3 + 0.7 * factor));
                g = (byte)(baseColor.G * (0.3 + 0.7 * factor));
                b = (byte)(baseColor.B * (0.3 + 0.7 * factor));
            }

            _bubbles[i].Fill = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        }
    }

    public void Update(double deltaTime)
    {
        _t += deltaTime * 0.20;

        for (var i = 0; i < _bubbles.Length; i++)
        {
            var s = _states[i];

            var drift = 8 + (s.Radius * 0.02);
            var x = s.X + Math.Sin(_t * 0.9 + s.Phase) * drift * s.DriftX;
            var y = s.Y + Math.Cos(_t * 0.75 + s.Phase * 1.3) * drift * s.DriftY;

            var pulse = 1.0 + Math.Sin(_t * 0.6 + s.Phase) * (0.03 + s.Pulse * 0.02);
            if (_bubbles[i].RenderTransform is ScaleTransform st)
            {
                st.ScaleX = pulse;
                st.ScaleY = pulse;
            }

            var d = s.Radius * 2;
            _bubbles[i].Width = d;
            _bubbles[i].Height = d;

            Canvas.SetLeft(_bubbles[i], x - s.Radius);
            Canvas.SetTop(_bubbles[i], y - s.Radius);
        }
    }

    private void InitializeLayout()
    {
        // Random composition: size/count/position are randomized each initialization
        var w = Math.Max(_canvasWidth, 1);
        var h = Math.Max(_canvasHeight, 1);

        // Stable per-instance seed; include variationOffset so multi-monitor differs (if used)
        var seed = HashCode.Combine(_seedBase, (int)(_variationOffset * 100000));
        var rng = new Random(seed);

        var minCount = Math.Min(5, _bubbles.Length);
        var maxCount = _bubbles.Length;
        _activeCount = rng.Next(minCount, maxCount + 1);

        var minDim = Math.Min(w, h);
        var minR = minDim * 0.06;
        var maxR = minDim * 0.26;

        var padding = minDim * 0.01;
        var placed = new List<(double X, double Y, double R)>();

        double NextRadius()
        {
            // Bias towards smaller bubbles but keep some large ones
            var u = rng.NextDouble();
            var bias = u * u;
            var radius = minR + (maxR - minR) * bias;

            // Sometimes create a big corner bubble for the screenshot-like cropping
            if (rng.NextDouble() < 0.25)
            {
                radius = minDim * (0.20 + rng.NextDouble() * 0.18);
            }

            return radius;
        }

        bool Overlaps(double x, double y, double r)
        {
            for (var j = 0; j < placed.Count; j++)
            {
                var p = placed[j];
                var dx = x - p.X;
                var dy = y - p.Y;
                var minDist = r + p.R + padding;
                if (dx * dx + dy * dy < minDist * minDist)
                {
                    return true;
                }
            }
            return false;
        }

        void AddPlaced(double x, double y, double r)
        {
            placed.Add((x, y, r));
        }

        for (var i = 0; i < _bubbles.Length; i++)
        {
            var active = i < _activeCount;
            _bubbles[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active)
            {
                _states[i] = default;
                continue;
            }

            // Ensure some bubbles are cropped by screen edges
            var edgeTarget = Math.Min(2, _activeCount);
            var preferEdge = i < edgeTarget;

            var radius = NextRadius();
            double x = 0;
            double y = 0;

            var attempts = 0;
            var maxAttempts = 60;
            while (attempts++ < maxAttempts)
            {
                radius = NextRadius();

                if (preferEdge)
                {
                    var side = rng.Next(0, 4);
                    switch (side)
                    {
                        case 0: // left
                            x = -radius * (0.25 + rng.NextDouble() * 0.35);
                            y = (rng.NextDouble() * 1.2 - 0.1) * h;
                            break;
                        case 1: // right
                            x = w + radius * (0.25 + rng.NextDouble() * 0.35);
                            y = (rng.NextDouble() * 1.2 - 0.1) * h;
                            break;
                        case 2: // top
                            x = (rng.NextDouble() * 1.2 - 0.1) * w;
                            y = -radius * (0.25 + rng.NextDouble() * 0.35);
                            break;
                        default: // bottom
                            x = (rng.NextDouble() * 1.2 - 0.1) * w;
                            y = h + radius * (0.25 + rng.NextDouble() * 0.35);
                            break;
                    }
                }
                else
                {
                    // Mostly inside, sometimes slightly off-screen
                    x = (rng.NextDouble() * 1.25 - 0.125) * w;
                    y = (rng.NextDouble() * 1.25 - 0.125) * h;
                }

                if (!Overlaps(x, y, radius))
                {
                    break;
                }
            }

            AddPlaced(x, y, radius);

            // Random drift directions (avoid 0)
            var dx = rng.NextDouble() * 2 - 1;
            var dy = rng.NextDouble() * 2 - 1;
            if (Math.Abs(dx) < 0.15) dx = dx < 0 ? -0.15 : 0.15;
            if (Math.Abs(dy) < 0.15) dy = dy < 0 ? -0.15 : 0.15;

            _states[i] = new BubbleState
            {
                X = x,
                Y = y,
                Radius = radius,
                Phase = rng.NextDouble() * Math.PI * 2,
                DriftX = dx,
                DriftY = dy,
                Pulse = 0.4 + rng.NextDouble() * 0.9
            };
        }
    }
}
