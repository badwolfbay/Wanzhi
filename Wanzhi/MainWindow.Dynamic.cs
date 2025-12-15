using System;
using System.Windows;
using Wanzhi.Settings;

namespace Wanzhi
{
    public partial class MainWindow : Window
    {
        public void EnsureDynamicActive()
        {
            if (Visibility != Visibility.Visible)
            {
                Show();
            }

            InitializeWaveRenderer();

            // 强制重启动画定时器，避免之前被停止后未恢复
            _animationTimer.Stop();
            _lastAnimationTime = DateTime.Now;
            if (AppSettings.Instance.EnableWaveAnimation)
            {
                _animationTimer.Start();
            }

            SetupWindowBounds();

            if (_currentPoetry != null)
            {
                UpdatePoetryDisplay(_currentPoetry);
            }

            App.Log($"EnsureDynamicActive: Visible={IsVisible}, WaveRenderer={(_waveRenderer!=null)}, Timer={_animationTimer.IsEnabled}");
        }
    }
}
