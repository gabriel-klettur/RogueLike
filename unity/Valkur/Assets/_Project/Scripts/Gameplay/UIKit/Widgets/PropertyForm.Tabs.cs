using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Opt-in tab grouping for <see cref="PropertyForm"/>.
    ///
    /// <see cref="PropertyForm.AddHeader"/> already says why this exists: forty-odd rows
    /// without grouping is a wall, and the Particles preset panel now stands at sixteen
    /// headers and fifty-six rows in a single scroll. Headers stopped being enough.
    ///
    /// The whole feature is opt-in through one branch, <see cref="RowParent"/>. A form that
    /// never calls <see cref="BeginTab"/> builds no strip, no page containers and no extra
    /// transforms; its rows stay direct children of the form in the order they were added,
    /// which is what every editor that builds this form today already depends on.
    ///
    /// Shape of a tabbed form:
    ///
    ///   PropertyForm          VerticalLayoutGroup + ContentSizeFitter
    ///    - Row_Name           rows added BEFORE the first BeginTab: pinned, always visible
    ///    - Row_Kind
    ///    - Tabs               TabStrip, created by the first BeginTab
    ///    - Tab_EMISSION       one page per tab, exactly one of them active
    ///    - Tab_MOTION
    ///
    /// WHY THE PAGES ARE HIDDEN WITH SetActive AND NOT A CanvasGroup. The form hangs inside
    /// a ScrollRect whose content is driven by a ContentSizeFitter, and tabs differ wildly
    /// in height. A CanvasGroup at alpha 0 still reports its full preferred height, so the
    /// scroll content would stay as tall as the TALLEST tab forever and every shorter tab
    /// would end in a lake of empty scroll. A deactivated GameObject is dropped from
    /// LayoutGroup.rectChildren outright, so the content collapses to exactly the tab on
    /// screen. uGUI even queues the work for us: LayoutGroup.OnDisable calls
    /// LayoutRebuilder.MarkLayoutForRebuild, whose walk climbs past this form to the
    /// ScrollRect content, so the fitter pass is already scheduled before we return.
    ///
    /// Rows built inside an already-hidden page never run Awake/OnEnable, and that is fine
    /// on purpose: uGUI and TMP both resync from their stored state the first time they are
    /// enabled - Graphic.OnEnable calls SetAllDirty, Toggle.OnEnable calls PlayEffect(true),
    /// and TMP_InputField.OnEnable ends in UpdateLabel(). So a value written into a row on a
    /// hidden tab renders correctly the first time that tab is revealed.
    ///
    /// WHERE THE SELECTED TAB LIVES. On this component, in <see cref="_desiredTab"/>, and
    /// nowhere else. Editors call Clear() and re-add every row whenever the selection
    /// changes, and the Particles panel goes further: it rebuilds the entire form after
    /// every accepted edit. A tab that reset on Clear() would throw the user back to the
    /// first tab on every value they committed, which is strictly worse than having no tabs.
    /// Clear() destroys children; it does not touch the component that owns them, so an
    /// instance field is exactly the lifetime this needs - the Particles panel builds its
    /// PropertyForm once in Start() and only ever hides it on close, so one field carries
    /// the choice for the whole session. A static keyed by panel name would survive even the
    /// form being destroyed, but it would equally survive Play-Stop-Play with Domain Reload
    /// off (so it would need a SubsystemRegistration reset), and it would leak one form's
    /// choice into any other form that happened to share a tab label. That is a lifetime
    /// nobody asked for. There is no static state in this file.
    ///
    /// The memory stores what the user LAST ASKED FOR, not what is on screen. The two differ
    /// when the tab set changes: pick a preset whose kind has no ORBIT section and the form
    /// falls back to the first tab, but it does NOT forget that ORBIT was wanted, so
    /// stepping back onto a preset that has one puts you straight back there.
    /// </summary>
    public sealed partial class PropertyForm
    {
        /// <summary>One tab: its label and the container every row of that tab lives in.</summary>
        private sealed class TabPage
        {
            public string     Label;
            public GameObject Go;
        }

        private readonly List<TabPage> _pages = new List<TabPage>();

        private TabStrip  _strip;
        private Transform _currentPage;

        /// <summary>Label of the page on screen; null while the form has no tabs.</summary>
        private string _shownTab;

        /// <summary>
        /// The tab the user last asked for. The one piece of state Clear() must NOT throw
        /// away - see the class comment.
        /// </summary>
        private string _desiredTab;

        /// <summary>
        /// Raised while BeginTab registers tabs, so the strip's own "select the first tab you
        /// are given" is not mistaken for the user stating a preference.
        /// </summary>
        private bool _restoringTabs;

        /// <summary>
        /// Bumped by every Clear(). ShowOnly re-reads it after each SetActive so it can tell
        /// that the form was rebuilt underneath it mid-walk - see ShowOnly for how that
        /// happens and why abandoning the walk is the correct answer.
        /// </summary>
        private int _tabGeneration;

        private int   _tabColumns       = 4;
        private float _tabLabelFontSize = 10f;

        private ScrollRect _ownerScroll;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Label of the tab on screen, or null when this form has no tabs.</summary>
        public string SelectedTab => _shownTab;

        /// <summary>
        /// Tabs per strip row. A single row divides the panel width evenly, so past four or
        /// five tabs every label wraps mid-word - "Portals" rendering as "Portal / s" is not
        /// a truncation the reader can undo. Above that count the strip wraps onto further
        /// rows, trading vertical space (which a docked properties panel has) for horizontal
        /// space (which it does not). Read when the strip is built, i.e. on the first
        /// BeginTab of a build, so a later change takes effect on the next rebuild.
        /// </summary>
        public int TabColumns
        {
            get => _tabColumns;
            set => _tabColumns = Mathf.Max(1, value);
        }

        /// <summary>
        /// Point size for tab labels. Below TabStrip's own default because a properties panel
        /// is narrower than the panels that widget was written for. Same timing rule as
        /// <see cref="TabColumns"/>.
        /// </summary>
        public float TabLabelFontSize
        {
            get => _tabLabelFontSize;
            set => _tabLabelFontSize = Mathf.Max(1f, value);
        }

        /// <summary>
        /// Every Add* row from here until the next BeginTab (or the next Clear) belongs to
        /// the tab named <paramref name="label"/>. The first call builds the strip, which
        /// lands after whatever rows already exist - so rows added BEFORE any BeginTab stay
        /// pinned above the strip and are on screen whichever tab is selected. That is where
        /// an editor puts the identity fields it wants visible in every tab.
        ///
        /// Naming a tab that already exists in this build resumes it, so a caller may return
        /// to a group instead of having to emit all of its rows in one run.
        /// </summary>
        public void BeginTab(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                Debug.LogWarning("[PropertyForm] BeginTab needs a label; ignoring. Rows that " +
                                 "should stay visible in every tab go BEFORE the first BeginTab.");
                return;
            }

            for (int i = 0; i < _pages.Count; i++)
            {
                if (_pages[i].Label != label || _pages[i].Go == null) continue;
                _currentPage = _pages[i].Go.transform;
                return;
            }

            EnsureStrip();

            var page = new TabPage { Label = label, Go = CreatePage(label) };
            _pages.Add(page);
            _currentPage = page.Go.transform;

            // Born hidden unless it is the page already on screen. Tabs are registered one at
            // a time, so the first one is what the strip auto-selects and every later one
            // arrives into an existing selection.
            page.Go.SetActive(_shownTab == null || _shownTab == label);

            _restoringTabs = true;
            try
            {
                // The page is deliberately NOT handed to TabStrip as its content, even though
                // TabStrip can toggle content itself. TabStrip raises TabChanged AFTER it has
                // already toggled the contents, and hiding a page can re-enter this form (see
                // ShowOnly) - so by the time we learned which tab the user clicked, a rebuild
                // would already have run against the OLD selection and the click would
                // visibly bounce back. Letting the strip own only the buttons puts TabChanged
                // FIRST: we record the choice, and only then touch anything that can call
                // back into us.
                _strip.AddTab(label, label, null);

                // Restore the remembered tab the moment it shows up. Doing it here rather
                // than at some end-of-build hook is what keeps the API to one method: there
                // is no EndTabs() to forget, and a tab set that no longer contains the
                // remembered label simply never triggers this and stays on the first tab.
                if (label == _desiredTab && _shownTab != label) _strip.SetActive(label);
            }
            finally
            {
                _restoringTabs = false;
            }
        }

        /// <summary>
        /// Shows the named tab and records it as the choice to restore after the next
        /// Clear() + rebuild. The intent is recorded even when no such tab exists yet, which
        /// lets an editor state the tab it wants before it builds the form. Returns whether a
        /// tab of that name was actually there to switch to.
        /// </summary>
        public bool SelectTab(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            _desiredTab = label;
            return _strip != null && _strip.SetActive(label);
        }

        // ── Internals ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Where the next row is parented, and the entire opt-in: a form that never calls
        /// BeginTab has no current page, so <see cref="AddHeader"/> and BuildRow parent onto
        /// the form itself exactly as they did before tabs existed.
        /// </summary>
        private Transform RowParent => _currentPage != null ? _currentPage : transform;

        /// <summary>
        /// Drops the tab bookkeeping. The strip and the pages are children of this transform
        /// like any other row, so Clear()'s destroy loop has already taken them; all that is
        /// left is the managed side. <see cref="_desiredTab"/> is deliberately untouched.
        /// </summary>
        private void ClearTabs()
        {
            _tabGeneration++;
            _strip       = null;
            _currentPage = null;
            _shownTab    = null;
            _pages.Clear();
        }

        private void EnsureStrip()
        {
            if (_strip != null) return;

            // CreateWrapped rather than Create even for a handful of tabs: with fewer tabs
            // than columns it lays out as a single row anyway, so one code path covers both,
            // and a panel that later grows a seventh tab does not silently turn its labels
            // into slivers.
            _strip = TabStrip.CreateWrapped(transform, "Tabs", _tabColumns);
            _strip.LabelFontSize = _tabLabelFontSize;
            _strip.TabChanged   += OnStripTabChanged;
        }

        /// <summary>
        /// The container holding one tab's rows. Its layout group mirrors the form's own so
        /// rows inside a tab measure and space identically to pinned rows - except for the
        /// padding, which stays at zero: the form already insets its children by 2 px a side
        /// and a nested group would double that, indenting every tabbed row against the
        /// pinned rows above the strip.
        ///
        /// No ContentSizeFitter here on purpose. This container is a child of a layout group,
        /// so its height is asked for through ILayoutElement (the VerticalLayoutGroup answers
        /// it from the rows); adding a fitter as well is the classic pair of size drivers
        /// fighting over one rect.
        /// </summary>
        private GameObject CreatePage(string label)
        {
            var go  = UIFactory.CreateUI("Tab_" + label, transform);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 4f;
            vlg.padding                = new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            return go;
        }

        private void OnStripTabChanged(int index, string label)
        {
            // TabStrip raises this for programmatic selection as well as for clicks, and
            // BeginTab registers tabs one at a time - the first AddTab auto-selects index 0.
            // Recording THAT as the user's choice would erase the remembered tab on every
            // rebuild, which is the exact failure this feature exists to avoid.
            bool userInitiated = !_restoringTabs;
            if (userInitiated) _desiredTab = label;
            ShowOnly(label, userInitiated);
        }

        private void ShowOnly(string label, bool scrollToTop)
        {
            int gen = _tabGeneration;

            for (int i = 0; i < _pages.Count; i++)
            {
                var page = _pages[i];
                if (page.Go == null) continue;

                bool on = page.Label == label;
                // Skipping a page that is already where it needs to be is not just a
                // saving: a redundant SetActive(false) would re-run OnDisable on every
                // row inside it, and OnDisable is exactly the callback that can fire
                // onEndEdit (see below).
                //
                // The comparison is against activeSelf, NOT activeInHierarchy. An editor
                // panel is hidden by deactivating its whole window, and while it is closed
                // every page reads as inactive in the hierarchy no matter which tab it
                // belongs to. A page that must go off would then look as if it already had,
                // keep activeSelf == true, and reappear alongside the selected tab the next
                // time the panel opens.
                if (page.Go.activeSelf == on) continue;

                page.Go.SetActive(on);

                // Hiding a page disables everything inside it, and a focused TMP_InputField
                // answers OnDisable with DeactivateInputField -> ReleaseSelection ->
                // onEndEdit. In the Particles panel that callback rebuilds the entire form,
                // so Clear() can run in the middle of this walk and empty _pages under us.
                // The generation counter is how we find out. Everything still in the old list
                // is already queued for Destroy and the rebuild has re-shown the right tab
                // from _desiredTab, so abandoning the walk is not merely safe, it is the
                // correct thing to do.
                if (_tabGeneration != gen) return;
            }

            _shownTab = label;

            // Belt and braces over LayoutGroup.OnDisable, which already marks this chain
            // dirty: the walk climbs from here to the ScrollRect content, so the
            // ContentSizeFitter re-measures against the tab that is now visible.
            LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);

            if (scrollToTop) ScrollFormToTop();
        }

        /// <summary>
        /// A tab switch starts at the top of the new tab. Without this, switching from a tall
        /// tab to a short one leaves the view scrolled into content that no longer exists and
        /// the ScrollRect elastically snaps back, which reads as the panel glitching. Only
        /// user-initiated switches do it: a rebuild triggered by an edit must leave the
        /// scroll exactly where the user left it, or committing a value at the bottom of a
        /// long tab would fling them to the top.
        ///
        /// Assigning verticalNormalizedPosition runs ScrollRect.EnsureLayoutHasRebuilt, which
        /// force-updates the canvas - wanted here, since "the top" is only meaningful once
        /// the fitter has absorbed the new tab's height. It costs one synchronous layout pass
        /// on a click, not per frame.
        /// </summary>
        private void ScrollFormToTop()
        {
            if (_ownerScroll == null) _ownerScroll = GetComponentInParent<ScrollRect>(true);
            if (_ownerScroll == null || _ownerScroll.content == null || !_ownerScroll.vertical) return;
            _ownerScroll.verticalNormalizedPosition = 1f;
        }
    }
}
