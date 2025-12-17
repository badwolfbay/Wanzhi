using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Wanzhi.Rendering
{
    /// <summary>
    /// 波浪动画渲染器
    /// </summary>
    public class WaveRenderer
    {
        private readonly Path[] _wavePaths;
        private double _animationOffset = 0;
        private double _canvasWidth;
        private double _canvasHeight;

        public WaveRenderer(double width, double height, int waveCount = 5)
        {
            _canvasWidth = width;
            _canvasHeight = height;
            _wavePaths = new Path[waveCount];

            InitializeWaves();
        }

        private void InitializeWaves()
        {
            for (int i = 0; i < _wavePaths.Length; i++)
            {
                _wavePaths[i] = new Path
                {
                    StrokeThickness = 0,
                    Fill = CreateWaveBrush(i)
                };
            }
        }

        private System.Windows.Media.Brush CreateWaveBrush(int index)
        {
            // 默认颜色，会被 UpdateColor 覆盖
            return new SolidColorBrush(Colors.Blue);
        }

        public Path[] GetWavePaths() => _wavePaths;

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
            Update(0);
        }

        /// <summary>
        /// 更新波浪颜色 - 上浅下深渐变
        /// </summary>
        public void UpdateColor(System.Windows.Media.Color baseColor, bool isDarkTheme)
        {
            // 将基准色转换为 HSL 或简单调整 RGB
            // 假设 baseColor 是最深色（底部）
            
            for (int i = 0; i < _wavePaths.Length; i++)
            {
                // 计算渐变因子：0 (最上) -> 1 (最下)
                // 实际上我们希望最上面的波浪（i=0）颜色最浅，最下面的（i=Length-1）颜色最深
                // 但在绘制时，通常先画后面的（上层），再画前面的（下层）。
                // 假设 i=0 是最远的波浪（最上），i=Length-1 是最近的波浪（最下）。
                
                double factor = (double)i / (_wavePaths.Length - 1); // 0.0 to 1.0
                
                // 调整透明度：上层透明度低，下层高
                // 增加最小透明度以确保在浅色背景上可见
                byte alpha = (byte)(80 + factor * 175); // 80 -> 255
                
                // 调整亮度：上层亮（混入白色），下层接近原色
                // 简单的混合算法：Color = Base * factor + White * (1 - factor)
                
                byte r = (byte)(baseColor.R * (0.5 + 0.5 * factor) + 255 * (1 - (0.5 + 0.5 * factor)));
                byte g = (byte)(baseColor.G * (0.5 + 0.5 * factor) + 255 * (1 - (0.5 + 0.5 * factor)));
                byte b = (byte)(baseColor.B * (0.5 + 0.5 * factor) + 255 * (1 - (0.5 + 0.5 * factor)));

                if (isDarkTheme)
                {
                    // 深色模式下，混入黑色或保持深色
                    alpha = (byte)(30 + factor * 100);
                    r = (byte)(baseColor.R * (0.3 + 0.7 * factor));
                    g = (byte)(baseColor.G * (0.3 + 0.7 * factor));
                    b = (byte)(baseColor.B * (0.3 + 0.7 * factor));
                }

                var waveColor = System.Windows.Media.Color.FromArgb(alpha, r, g, b);
                _wavePaths[i].Fill = new SolidColorBrush(waveColor);
            }
        }

        /// <summary>
        /// 更新动画帧
        /// </summary>
        public void Update(double deltaTime)
        {
            _animationOffset += deltaTime * 0.3; // 减慢速度

            for (int i = 0; i < _wavePaths.Length; i++)
            {
                UpdateWavePath(i);
            }
        }

        private void UpdateWavePath(int waveIndex)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(0, _canvasHeight)
            };

            // 波浪参数
            // i=0 (最远/最上): 基准线高，振幅小
            // i=Max (最近/最下): 基准线低，振幅大
            
            double factor = (double)waveIndex / _wavePaths.Length;
            
            double amplitude = 40 + factor * 40; // 40 -> 80
            double frequency = 0.002 + waveIndex * 0.0005; 
            double phase = _animationOffset + waveIndex * 1.5; 
            
            // 基准线：从屏幕中间开始，向下延伸
            double baseY = _canvasHeight * (0.5 + 0.1 * waveIndex); 

            // 生成波浪曲线
            var points = new PointCollection();
            for (double x = 0; x <= _canvasWidth; x += 20)
            {
                // 叠加两个正弦波以增加随机感
                double y = baseY + 
                           Math.Sin(x * frequency + phase) * amplitude + 
                           Math.Sin(x * frequency * 2.5 + phase * 1.3) * (amplitude * 0.3);
                points.Add(new Point(x, y));
            }
            // 确保最后一个点在右边界
            points.Add(new Point(_canvasWidth, baseY + Math.Sin(_canvasWidth * frequency + phase) * amplitude));

            // 使用贝塞尔曲线平滑连接点
            if (points.Count > 0)
            {
                figure.StartPoint = new Point(0, points[0].Y);
                
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    // 简单的控制点计算
                    var controlPoint = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                    // 实际上应该用三次贝塞尔或者更平滑的插值，但二次贝塞尔在这里够用了
                    // 为了更平滑，我们可以直接用 PolyBezier 或者 Catmull-Rom (需要自己算)
                    // 这里简化，直接连线或者二次贝塞尔
                    
                    var segment = new QuadraticBezierSegment(controlPoint, p2, true);
                    figure.Segments.Add(segment);
                }
            }

            // 闭合路径到底部
            figure.Segments.Add(new LineSegment(new Point(_canvasWidth, _canvasHeight), true));
            figure.Segments.Add(new LineSegment(new Point(0, _canvasHeight), true));
            figure.IsClosed = true;

            geometry.Figures.Add(figure);
            _wavePaths[waveIndex].Data = geometry;
        }
    }
}
