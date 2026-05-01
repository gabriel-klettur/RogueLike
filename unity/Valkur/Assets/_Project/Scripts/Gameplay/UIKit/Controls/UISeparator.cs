using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>1px horizontal separator using <see cref="UITheme.SEPARATOR"/>.</summary>
    public static class UISeparator
    {
        public static void Build(Transform parent)
        {
            var go = UIFactory.CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = UITheme.SEPARATOR;
        }
    }
}
