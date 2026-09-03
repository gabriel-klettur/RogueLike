using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core.Editors
{
    /// <summary>
    /// Everything one runtime editor remembers between sessions: where its panels were,
    /// what it was doing, and what was selected.
    ///
    /// Lives in <c>Valkur.Core</c> because it is produced by <c>Valkur.UIKit</c>
    /// (<c>DraggablePanel</c> captures its own geometry), consumed by
    /// <c>Valkur.Gameplay</c> (the service and each editor) and written by
    /// <c>Valkur.Infrastructure</c> (the JSON store) — and Core is the only assembly all
    /// three may reference.
    ///
    /// Serialized with <see cref="JsonUtility"/>, which cannot round-trip a dictionary —
    /// hence the session bag is a list of pairs with lookup helpers rather than a
    /// <c>Dictionary</c>.
    /// </summary>
    [Serializable]
    public sealed class EditorWorkspace
    {
        /// <summary>
        /// Bumped whenever the shape of this document changes incompatibly. A document
        /// whose version this build does not know is DISCARDED WHOLE by the store — never
        /// read partially, because half a layout is worse than no layout: the author
        /// cannot tell a stale panel from a fresh one.
        /// </summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary><see cref="GameEditorManager.IGameEditor.EditorName"/> this belongs to.</summary>
        public string editorName = string.Empty;

        /// <summary>
        /// Canvas size (in canvas units) this workspace was captured at. The restore path
        /// compares it against the live canvas: a layout captured at 2560x1440 leaves
        /// panels unreachable at 1366x768, and rescuing them needs to know it happened.
        /// </summary>
        public Vector2 capturedCanvasSize = Vector2.zero;

        public List<EditorPanelState> panels = new List<EditorPanelState>();

        /// <summary>Free-form per-editor session state — active mode, tab, search text, zoom.</summary>
        public List<EditorWorkspaceEntry> session = new List<EditorWorkspaceEntry>();

        public EditorSelectionRecord selection = new EditorSelectionRecord();

        // ── Panels ──────────────────────────────────────────────────────────────

        public EditorPanelState FindPanel(string panelId)
        {
            if (string.IsNullOrEmpty(panelId) || panels == null) return null;
            for (int i = 0; i < panels.Count; i++)
                if (panels[i] != null && panels[i].panelId == panelId) return panels[i];
            return null;
        }

        public void UpsertPanel(EditorPanelState state)
        {
            if (state == null || string.IsNullOrEmpty(state.panelId)) return;
            panels ??= new List<EditorPanelState>();
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] != null && panels[i].panelId == state.panelId)
                {
                    panels[i] = state;
                    return;
                }
            }
            panels.Add(state);
        }

        // ── Session bag ─────────────────────────────────────────────────────────
        //
        // Every getter takes a fallback and returns it whenever the key is absent OR the
        // stored text no longer parses. Restore must tolerate every value being missing or
        // stale — a workspace written by an older build, or naming a category the author
        // has since deleted, is the normal case, not an error.

        public void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            session ??= new List<EditorWorkspaceEntry>();
            for (int i = 0; i < session.Count; i++)
            {
                if (session[i] != null && session[i].key == key)
                {
                    session[i].value = value ?? string.Empty;
                    return;
                }
            }
            session.Add(new EditorWorkspaceEntry { key = key, value = value ?? string.Empty });
        }

        public string GetString(string key, string fallback = "")
        {
            if (string.IsNullOrEmpty(key) || session == null) return fallback;
            for (int i = 0; i < session.Count; i++)
                if (session[i] != null && session[i].key == key) return session[i].value;
            return fallback;
        }

        public void SetInt(string key, int value)
            => SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public int GetInt(string key, int fallback)
        {
            var raw = GetString(key, null);
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public void SetFloat(string key, float value)
            => SetString(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        public float GetFloat(string key, float fallback)
        {
            var raw = GetString(key, null);
            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public void SetBool(string key, bool value) => SetInt(key, value ? 1 : 0);

        public bool GetBool(string key, bool fallback) => GetInt(key, fallback ? 1 : 0) != 0;

        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key) || session == null) return false;
            for (int i = 0; i < session.Count; i++)
                if (session[i] != null && session[i].key == key) return true;
            return false;
        }
    }

    /// <summary>One key/value pair of the session bag. See <see cref="EditorWorkspace"/>.</summary>
    [Serializable]
    public sealed class EditorWorkspaceEntry
    {
        public string key   = string.Empty;
        public string value = string.Empty;
    }
}
