using UnityEngine;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The Properties form's filter.
    ///
    /// <para>The panel is eight sections and roughly fifty rows now that <c>aiTuning</c> and the
    /// rewards have somewhere to live. Scrolling for one field is what a search box is for, and
    /// a form an author has to scan is a form where the field they are hunting is the one they
    /// re-type from memory in the Inspector instead.</para>
    ///
    /// <para><b>It hides ROWS, it does not rebuild them.</b> Rebuilding on every keystroke is the
    /// shape that cost the Items editor 3.5 s and the Controls editor 213 ms per character — and
    /// here it would also destroy the input field's own focus, so the second letter would land
    /// somewhere else. A section whose every row is hidden hides its header too, or the form
    /// reads as a list of empty headings.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        private string _propsFilter = string.Empty;

        /// <summary>
        /// Sections the author has folded, BY NAME.
        ///
        /// <para>By name and not by Transform because the runtime editor destroys and rebuilds
        /// these bodies on every selection change: a set of object references would hold
        /// destroyed Transforms and lose the fold the moment another entity was clicked. The
        /// name is <c>Section_&lt;Title&gt;</c>, which is stable across rebuilds and is already
        /// what the filter walks.</para>
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string> _foldedSections =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// Fold or unfold a section and REMEMBER it, so a rebuilt panel comes back the shape
        /// the author left it.
        /// </summary>
        private void OnSectionFoldToggled(Transform section)
        {
            if (section == null) return;
            EntitiesEditorUIBuilder.ToggleSectionFold(section);
            if (EntitiesEditorUIBuilder.IsSectionOpen(section)) _foldedSections.Remove(section.name);
            else                                               _foldedSections.Add(section.name);
        }

        /// <summary>
        /// Re-apply the remembered folds after the panel has been rebuilt. Called at the end of
        /// <c>ShowMonsterProperties</c>, because the sections that exist then are brand-new
        /// objects that default to open.
        /// </summary>
        private void RestoreSectionFolds()
        {
            if (_ui.PropsFormRoot == null) return;
            for (int i = 0; i < _ui.PropsFormRoot.childCount; i++)
            {
                var section = _ui.PropsFormRoot.GetChild(i);
                if (section == null) continue;
                EntitiesEditorUIBuilder.SetSectionOpen(
                    section, !_foldedSections.Contains(section.name));
            }
        }

        private void OnPropsFilterChanged(string raw)
        {
            _propsFilter = raw ?? string.Empty;
            ApplyPropsFilter();
        }

        /// <summary>
        /// Show the rows whose label matches, and the headers that still have one.
        ///
        /// <para>Matching is on the ROW LABEL only, deliberately. Matching values too would make
        /// typing "1" light up half the form, and the question a filter answers here is "where is
        /// the field called X", never "which fields are currently 1".</para>
        /// </summary>
        private void ApplyPropsFilter()
        {
            if (_ui.PropsFormRoot == null) return;

            string needle = _propsFilter.Trim().ToLowerInvariant();
            bool filtering = needle.Length > 0;

            for (int i = 0; i < _ui.PropsFormRoot.childCount; i++)
            {
                // A section is "Section_<Title>" holding a "Header" and a "Body"; the ROWS are
                // the Body's children. The first pass assumed the header was child 0 of the row
                // container and the rows followed, which is what the section LOOKS like on
                // screen -- so it toggled the Body as though it were a row and never touched a
                // single one. Found by counting rows in a live editor, not by reading it.
                var section = _ui.PropsFormRoot.GetChild(i);
                var body = section != null ? section.Find("Body") : null;
                if (body == null) continue;

                // A FOLDED section still has to answer the filter. Its rows are all hidden by
                // the fold, so leaving it folded means typing a field's name and being told
                // nothing matches -- with the match sitting one collapsed header away. While
                // a filter is live the fold is suspended and the author's own fold state is
                // put back the moment the box is cleared, so filtering never silently
                // rearranges the panel they set up.
                if (filtering) EntitiesEditorUIBuilder.SetSectionOpen(section, true);
                else           EntitiesEditorUIBuilder.SetSectionOpen(
                                   section, !_foldedSections.Contains(section.name));

                int visible = 0;
                for (int r = 0; r < body.childCount; r++)
                {
                    var row = body.GetChild(r);
                    if (row == null) continue;

                    bool show = !filtering || RowMatches(row, needle);
                    if (row.gameObject.activeSelf != show) row.gameObject.SetActive(show);
                    if (show) visible++;
                }

                // The whole section goes, header included, when nothing in it matched: a form
                // filtered down to four headings and no fields reads as a broken filter.
                bool showSection = !filtering || visible > 0;
                if (section.gameObject.activeSelf != showSection)
                    section.gameObject.SetActive(showSection);
            }
        }

        /// <summary>
        /// Whether a row's LABEL contains the needle.
        ///
        /// <para>The label is the first TMP under the row. Reading every TMP would match the
        /// VALUE as well, which is the behaviour the doc above rejects — and on an editable row
        /// the value is a live input field, so it would also change what is visible as the author
        /// types into a completely different box.</para>
        /// </summary>
        private static bool RowMatches(Transform row, string needle)
        {
            var label = row.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label == null) return false;
            string text = label.text;
            return !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(needle);
        }
    }
}
