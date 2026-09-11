using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// A titled, collapsible block of the debug HUD: a header strip that is the ONLY click target
    /// of the section, and a body its owner fills with rows.
    ///
    /// <para><b>Two ways to be closed.</b> <see cref="Collapsed"/> is the author's choice and is
    /// persisted; <see cref="AutoCollapsed"/> is the panel folding a section because the band
    /// ran out of height on this screen, and is never saved — a choice the screen made for the
    /// author must not outlive the screen. Either one draws the section as its header only,
    /// with a "+" saying there is more.</para>
    /// </summary>
    public sealed class DebugHudSection
    {
        private readonly HudPixelText _mark;
        private readonly HudPixelText _title;
        private readonly HudPixelText _summary;
        private readonly Image _header;
        private readonly int _headerH;
        private bool _collapsed;
        private bool _autoCollapsed;

        public RectTransform Root { get; }
        public RectTransform Body { get; }
        public int Width { get; }
        public int HeaderHeight => _headerH;

        /// <summary>The author folded this section. Persisted by the owner.</summary>
        public bool Collapsed
        {
            get => _collapsed;
            set { _collapsed = value; RefreshMark(); }
        }

        /// <summary>The panel folded this section to fit the band. Never persisted.</summary>
        public bool AutoCollapsed
        {
            get => _autoCollapsed;
            set { _autoCollapsed = value; RefreshMark(); }
        }

        public bool IsOpen => !_collapsed && !_autoCollapsed;

        /// <summary>Raised when the header is clicked.</summary>
        public event Action HeaderClicked;

        public DebugHudSection(Transform parent, string name, HudArt art, string title, int width, int headerH,
                               Color headerColour, Color titleColour, Color markColour)
        {
            Width = width;
            _headerH = headerH;
            Root = HudRect.Make(name, parent, 0, 0, width, headerH);

            var headerRt = HudRect.Make("Header", Root, 0, 0, width, headerH);
            _header = headerRt.gameObject.AddComponent<Image>();
            _header.sprite = art.White;
            _header.type = Image.Type.Simple;
            _header.color = headerColour;
            _header.raycastTarget = true;           // the section's one click target
            var click = headerRt.gameObject.AddComponent<DebugHudClick>();
            click.Clicked = () => HeaderClicked?.Invoke();

            _mark = HudPixelText.Create(headerRt, "Mark", art, HudFontFace.Small, HudTextAlign.Left, 2, 0, 5, headerH);
            _mark.SetColour(markColour);
            _title = HudPixelText.Create(headerRt, "Title", art, HudFontFace.Small, HudTextAlign.Left, 8, 0, width / 2, headerH);
            _title.SetText(title);
            _title.SetColour(titleColour);
            _summary = HudPixelText.Create(headerRt, "Summary", art, HudFontFace.Small, HudTextAlign.Right,
                                           width / 2, 0, width / 2 - 2, headerH);

            Body = HudRect.Make("Body", Root, 0, 0, width, 0);
            RefreshMark();
        }

        public HudPixelText Summary => _summary;

        public void SetSummary(string text, Color colour)
        {
            _summary.SetText(text);
            _summary.SetColour(colour);
        }

        /// <summary>
        /// Places the section with its TOP at <paramref name="top"/> (texels from the panel's
        /// bottom) and a body of <paramref name="bodyHeight"/> texels when open. Returns the
        /// height it now occupies.
        /// </summary>
        public int Place(int x, int top, int bodyHeight)
        {
            int body = IsOpen ? Mathf.Max(0, bodyHeight) : 0;
            int h = _headerH + body;
            HudRect.Place(Root, x, top - h, Width, h);
            HudRect.Place((RectTransform)_header.transform, 0, body, Width, _headerH);
            HudRect.Place(Body, 0, 0, Width, body);
            if (Body.gameObject.activeSelf != IsOpen) Body.gameObject.SetActive(IsOpen);
            return h;
        }

        private void RefreshMark() => _mark.SetText(IsOpen ? "-" : "+");
    }
}
