using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Editors.Skills
{
    /// <summary>
    /// Skills Editor — what it remembers between sessions.
    ///
    /// <para>The trade and the recipe being tuned, because both are otherwise re-chosen on
    /// every open and a tuning pass is exactly the workflow that reopens an editor repeatedly.
    /// Nothing here is destructive to restore: reopening on a recipe only shows it.</para>
    ///
    /// <para>The recipe is stored by ID rather than by index, because a re-import can reorder
    /// the catalog and an index would silently select a DIFFERENT recipe than the one the
    /// author left open — which on a live-edit surface means their next keystroke edits the
    /// wrong asset. An id that no longer resolves selects nothing, which is the safe failure.</para>
    /// </summary>
    public partial class SkillsRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_PROFESSION = "selectedProfession";
        private const string WS_RECIPE = "selectedRecipeId";
        private const string WS_SEARCH = "recipeSearch";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetInt(WS_PROFESSION, _selectedProfession);
            ws.SetString(WS_RECIPE, _selectedRecipeId ?? string.Empty);
            ws.SetString(WS_SEARCH, _search ?? string.Empty);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            _selectedProfession = Mathf.Max(0, ws.GetInt(WS_PROFESSION, 0));

            string recipeId = ws.GetString(WS_RECIPE, null);
            _selectedRecipeId = string.IsNullOrEmpty(recipeId) ? null : recipeId;

            // Restored because a tuning pass reopens the editor repeatedly and a
            // filter the author has to retype every time is a filter they stop using.
            _search = ws.GetString(WS_SEARCH, string.Empty) ?? string.Empty;
        }
    }
}
