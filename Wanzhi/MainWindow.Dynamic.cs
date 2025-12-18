using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Wanzhi.Settings;

namespace Wanzhi
{
    public partial class MainWindow : Window
    {
        private static readonly Dictionary<char, char> VerticalCharMap = new Dictionary<char, char>
        {
            ['('] = '︵',
            [')'] = '︶',
            ['（'] = '︵',
            ['）'] = '︶',
            ['{'] = '︷',
            ['}'] = '︸',
            ['《'] = '︽',
            ['》'] = '︾',
            ['〈'] = '︿',
            ['〉'] = '﹀',
            ['「'] = '﹁',
            ['」'] = '﹂',
            ['『'] = '﹃',
            ['』'] = '﹄',
            ['，'] = '︑',
            ['。'] = '︒',
            ['：'] = '︓',
            ['；'] = '︔',
            ['！'] = '︕',
            ['？'] = '︖'
        };

        private static string MapVertical(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (VerticalCharMap.TryGetValue(ch, out var v)) sb.Append(v); else sb.Append(ch);
            }
            return sb.ToString();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            LayoutUpdated -= MainWindow_LayoutUpdated;
            LayoutUpdated += MainWindow_LayoutUpdated;
        }

        private void MainWindow_LayoutUpdated(object? sender, EventArgs e)
        {
            if (PoetryContainer == null) return;
            if (PoetryContainer.Orientation != Orientation.Horizontal) return;
            if (PoetryContainer.FlowDirection != FlowDirection.RightToLeft) return;
            ApplyVerticalMap(PoetryContainer);
        }

        private void ApplyVerticalMap(object element)
        {
            if (element is TextBlock tb)
            {
                var t = tb.Text;
                if (!string.IsNullOrEmpty(t))
                {
                    var chars = t.ToCharArray();
                    bool changed = false;
                    for (int i = 0; i < chars.Length; i++)
                    {
                        if (VerticalCharMap.TryGetValue(chars[i], out var m)) { chars[i] = m; changed = true; }
                    }
                    if (changed) tb.Text = new string(chars);
                }
                // 统一标题引号的样式尺寸，保证上下符号一致
                if (tb.Text == "﹁" || tb.Text == "﹂")
                {
                    var targetSize = Wanzhi.Settings.AppSettings.Instance.AuthorFontSize;
                    if (Math.Abs(tb.FontSize - targetSize) > 0.01)
                    {
                        tb.FontSize = targetSize;
                    }

                    var targetFamilyName = Wanzhi.Settings.AppSettings.Instance.AuthorFontFamily;
                    if (tb.FontFamily == null || !string.Equals(tb.FontFamily.Source, targetFamilyName, StringComparison.Ordinal))
                    {
                        tb.FontFamily = new System.Windows.Media.FontFamily(targetFamilyName);
                    }
                }
                return;
            }
            if (element is Panel panel)
            {
                foreach (var child in panel.Children) ApplyVerticalMap(child);
                return;
            }
            if (element is Border border && border.Child != null)
            {
                ApplyVerticalMap(border.Child);
            }
        }
    }
}
