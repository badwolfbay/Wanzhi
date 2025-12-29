using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Wanzhi.Rendering;

public class BlobsRenderer : IBackgroundEffectRenderer, IVariationOffsetRenderer
{
    private readonly Path[] _blobs;
    private readonly BlobState[] _states;
    private int _activeCount;
    private readonly int _seedBase;
    private double _t;
    private double _variationOffset;
    private double _canvasWidth;
    private double _canvasHeight;

    private struct BlobState
    {
        public double X;
        public double Y;
        public double Radius;
        public double Phase;
        public double Drift;
        public int Segments;

        public double Rotation;
        public double StretchX;
        public double StretchY;

        public double A2;
        public double A3;
        public double A5;
        public double P2;
        public double P3;
        public double P5;
    }

    public BlobsRenderer(double width, double height, int blobCount = 5, int seedBase = 0)
    {
        _canvasWidth = width;
        _canvasHeight = height;
        _seedBase = seedBase;

        var count = Math.Max(5, blobCount);
        _blobs = new Path[count];
        _states = new BlobState[count];

        for (var i = 0; i < count; i++)
        {
            _blobs[i] = new Path
            {
                StrokeThickness = 0,
                Fill = new SolidColorBrush(Colors.Transparent)
            };
        }

        InitializeLayout();
    }

    public IEnumerable<UIElement> GetVisuals() => _blobs;

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
        for (var i = 0; i < _blobs.Length; i++)
        {
            if (_blobs[i].Visibility == Visibility.Visible)
            {
                visibleCount++;
            }
        }

        var visibleIndex = 0;
        for (var i = 0; i < _blobs.Length; i++)
        {
            if (_blobs[i].Visibility != Visibility.Visible)
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

            _blobs[i].Fill = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        }
    }

    public void Update(double deltaTime)
    {
        _t += deltaTime * 0.18;

        for (var i = 0; i < _blobs.Length; i++)
        {
            if (_blobs[i].Visibility != Visibility.Visible)
            {
                continue;
            }
            UpdateBlob(i);
        }
    }

    private void InitializeLayout()
    {
        var w = Math.Max(_canvasWidth, 1);
        var h = Math.Max(_canvasHeight, 1);

        var seed = HashCode.Combine(_seedBase, (int)(_variationOffset * 100000));
        var rng = new Random(seed);

        var minCount = Math.Min(4, _blobs.Length);
        var maxCount = _blobs.Length;
        _activeCount = rng.Next(minCount, maxCount + 1);

        var minDim = Math.Min(w, h);
        var minR = minDim * 0.08;
        var maxR = minDim * 0.30;
        var padding = minDim * 0.012;

        var placed = new List<(double X, double Y, double R)>();

        double NextRadius()
        {
            var u = rng.NextDouble();
            var bias = u * u;
            var r = minR + (maxR - minR) * bias;
            if (rng.NextDouble() < 0.30)
            {
                r = minDim * (0.18 + rng.NextDouble() * 0.22);
            }
            return r;
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

        for (var i = 0; i < _blobs.Length; i++)
        {
            var active = i < _activeCount;
            _blobs[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active)
            {
                _states[i] = default;
                continue;
            }

            var edgeTarget = Math.Min(2, _activeCount);
            var preferEdge = i < edgeTarget;

            var radius = NextRadius();
            double x = 0;
            double y = 0;

            var attempts = 0;
            var maxAttempts = 70;
            while (attempts++ < maxAttempts)
            {
                radius = NextRadius();

                if (preferEdge)
                {
                    var side = rng.Next(0, 4);
                    switch (side)
                    {
                        case 0: // left
                            x = -radius * (0.22 + rng.NextDouble() * 0.40);
                            y = (rng.NextDouble() * 1.2 - 0.1) * h;
                            break;
                        case 1: // right
                            x = w + radius * (0.22 + rng.NextDouble() * 0.40);
                            y = (rng.NextDouble() * 1.2 - 0.1) * h;
                            break;
                        case 2: // top
                            x = (rng.NextDouble() * 1.2 - 0.1) * w;
                            y = -radius * (0.22 + rng.NextDouble() * 0.40);
                            break;
                        default: // bottom
                            x = (rng.NextDouble() * 1.2 - 0.1) * w;
                            y = h + radius * (0.22 + rng.NextDouble() * 0.40);
                            break;
                    }
                }
                else
                {
                    x = (rng.NextDouble() * 1.25 - 0.125) * w;
                    y = (rng.NextDouble() * 1.25 - 0.125) * h;
                }

                if (!Overlaps(x, y, radius))
                {
                    break;
                }
            }

            AddPlaced(x, y, radius);

            _states[i] = new BlobState
            {
                X = x,
                Y = y,
                Radius = radius,
                Phase = rng.NextDouble() * Math.PI * 2,
                Drift = 5 + rng.NextDouble() * 12,
                Segments = 34 + rng.Next(0, 12),

                Rotation = rng.NextDouble() * Math.PI * 2,
                StretchX = 0.78 + rng.NextDouble() * 0.55,
                StretchY = 0.78 + rng.NextDouble() * 0.55,

                A2 = 0.05 + rng.NextDouble() * 0.06,
                A3 = 0.02 + rng.NextDouble() * 0.05,
                A5 = 0.01 + rng.NextDouble() * 0.04,
                P2 = rng.NextDouble() * Math.PI * 2,
                P3 = rng.NextDouble() * Math.PI * 2,
                P5 = rng.NextDouble() * Math.PI * 2
            };
        }
    }

    private void UpdateBlob(int index)
    {
        var w = Math.Max(_canvasWidth, 1);
        var h = Math.Max(_canvasHeight, 1);

        var s = _states[index];
        var dx = Math.Sin(_t * 0.8 + s.Phase) * s.Drift;
        var dy = Math.Cos(_t * 0.65 + s.Phase * 1.3) * (s.Drift * 0.9);

        var cx = s.X + dx;
        var cy = s.Y + dy;

        // Radial deformation: low-frequency harmonics + anisotropic stretch (pebble-like)
        var cosR = Math.Cos(s.Rotation);
        var sinR = Math.Sin(s.Rotation);
        var pts = new List<Point>(s.Segments);
        for (var k = 0; k < s.Segments; k++)
        {
            var a = (k / (double)s.Segments) * Math.PI * 2;
            var n2 = Math.Sin(a * 2 + s.P2 + s.Phase + _t * 0.18) * s.A2;
            var n3 = Math.Sin(a * 3 + s.P3 + s.Phase * 0.7 - _t * 0.14) * s.A3;
            var n5 = Math.Sin(a * 5 + s.P5 + _t * 0.10) * s.A5;

            var m = 1.0 + n2 + n3 + n5;
            m = Math.Clamp(m, 0.84, 1.18);
            var rr = s.Radius * m;

            var lx = Math.Cos(a) * rr * s.StretchX;
            var ly = Math.Sin(a) * rr * s.StretchY;
            var x = cx + (lx * cosR - ly * sinR);
            var y = cy + (lx * sinR + ly * cosR);
            pts.Add(new Point(x, y));
        }

        if (pts.Count < 3)
        {
            return;
        }

        // One Chaikin pass for rounder edges (pebble-like)
        var count = pts.Count;
        var smoothPts = new List<Point>(count * 2);
        for (var i = 0; i < count; i++)
        {
            var p0 = pts[i];
            var p1 = pts[(i + 1) % count];
            smoothPts.Add(new Point(p0.X * 0.75 + p1.X * 0.25, p0.Y * 0.75 + p1.Y * 0.25));
            smoothPts.Add(new Point(p0.X * 0.25 + p1.X * 0.75, p0.Y * 0.25 + p1.Y * 0.75));
        }

        pts = smoothPts;
        count = pts.Count;
        if (count < 4)
        {
            return;
        }

        var geometry = new PathGeometry();
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };

        for (var i = 0; i < count; i++)
        {
            var p0 = pts[(i - 1 + count) % count];
            var p1 = pts[i];
            var p2 = pts[(i + 1) % count];
            var p3 = pts[(i + 2) % count];

            var c1 = new Point(
                p1.X + (p2.X - p0.X) / 10.0,
                p1.Y + (p2.Y - p0.Y) / 10.0);
            var c2 = new Point(
                p2.X - (p3.X - p1.X) / 10.0,
                p2.Y - (p3.Y - p1.Y) / 10.0);

            fig.Segments.Add(new BezierSegment(c1, c2, p2, true));
        }

        geometry.Figures.Add(fig);
        _blobs[index].Data = geometry;

        // Keep inside some reasonable bounds to avoid runaway layouts
        if (cx < -w * 0.5 || cx > w * 1.5 || cy < -h * 0.8 || cy > h * 1.8)
        {
            InitializeLayout();
        }
    }
}
