using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Wanzhi.Settings
{
    /// <summary>
    /// 应用程序设置
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        private static AppSettings? _instance;
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wanzhi",
            "settings.json"
        );

        public static AppSettings Instance => _instance ??= Load();

        // 主题设置
        private ThemeMode _theme = ThemeMode.System;
        public ThemeMode Theme
        {
            get => _theme;
            set { _theme = value; OnPropertyChanged(); }
        }

        // 背景颜色 (ARGB 格式)
        private string _backgroundColor = "#FF1E3A8A"; // 深蓝色
        public string BackgroundColor
        {
            get => _backgroundColor;
            set { _backgroundColor = value; OnPropertyChanged(); }
        }

        // 启用波浪动画
        private bool _enableWaveAnimation = true;
        public bool EnableWaveAnimation
        {
            get => _enableWaveAnimation;
            set { _enableWaveAnimation = value; OnPropertyChanged(); }
        }

        // 波浪颜色 (ARGB 格式)
        private string _waveColor = "#FF26A69A"; // Teal / Fresh Green
        public string WaveColor
        {
            get => _waveColor;
            set { _waveColor = value; OnPropertyChanged(); }
        }

        // 诗词文字大小
        private int _poetryFontSize = 36;
        public int PoetryFontSize
        {
            get => _poetryFontSize;
            set { _poetryFontSize = value; OnPropertyChanged(); }
        }

        // 诗词字体
        private string _poetryFontFamily = "Microsoft YaHei";
        public string PoetryFontFamily
        {
            get => _poetryFontFamily;
            set { _poetryFontFamily = value; OnPropertyChanged(); }
        }

        // 诗词文字方向
        private TextOrientation _poetryOrientation = TextOrientation.Vertical;
        public TextOrientation PoetryOrientation
        {
            get => _poetryOrientation;
            set { _poetryOrientation = value; OnPropertyChanged(); }
        }

        // 诗词行间距
        private int _poetryLineSpacing = 20;
        public int PoetryLineSpacing
        {
            get => _poetryLineSpacing;
            set { _poetryLineSpacing = value; OnPropertyChanged(); }
        }

        // 刷新间隔（分钟）
        private int _refreshIntervalMinutes = 60;
        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set { _refreshIntervalMinutes = value; OnPropertyChanged(); }
        }

        // 开机自启
        private bool _autoStart = false;
        public bool AutoStart
        {
            get => _autoStart;
            set { _autoStart = value; OnPropertyChanged(); }
        }

        // 作者文字大小
        private int _authorFontSize = 18;
        public int AuthorFontSize
        {
            get => _authorFontSize;
            set { _authorFontSize = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 保存设置到文件
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载设置
        /// </summary>
        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}");
            }

            return new AppSettings();
        }
    }

    /// <summary>
    /// 主题模式
    /// </summary>
    public enum ThemeMode
    {
        Light,
        Dark,
        System
    }

    /// <summary>
    /// 文字方向
    /// </summary>
    public enum TextOrientation
    {
        Horizontal,
        Vertical
    }
}
