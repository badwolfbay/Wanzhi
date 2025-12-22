using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Wanzhi.Rendering;

public interface IBackgroundEffectRenderer
{
    IEnumerable<UIElement> GetVisuals();
    void SetCanvasSize(double width, double height);
    void Update(double deltaTime);
    void UpdateColor(Color baseColor, bool isDarkTheme);
}
