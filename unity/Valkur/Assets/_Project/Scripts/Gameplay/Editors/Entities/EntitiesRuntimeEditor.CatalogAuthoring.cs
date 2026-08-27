using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Create / Duplicate / Rename a <see cref="MonsterDefinition"/> from F5.
    ///
    /// Closes the other half of the audit's Dimension-1 gap: Phase 3 made the properties
    /// panel editable, but "Add on System" / "Confirm" were still a status-string stub and
    /// there was no <c>ScriptableObject.CreateInstance&lt;MonsterDefinition&gt;()</c> anywhere
    /// in the project — a designer who wanted a new monster, or just a retunable copy of an
    /// existing one, had to leave Play Mode for the Inspector and hand-drag the result into
    /// <see cref="MonsterCatalog"/>.
    ///
    /// All three verbs share one text field (<c>_pendingKeyInput</c>, the Add/Remove panel's
    /// "New / Rename Key" row): Confirm reads it as the key for a brand-new definition,
    /// Duplicate reads it as the new key for a clone of the selected one (falling back to
    /// <c>&lt;source&gt;_copy</c> when left blank), and Rename reads it as the new key +
    /// display name for whichever definition is selected. <see cref="MonsterCatalog.UpsertDefinition"/>
    /// is keyed on <c>monsterKey</c>, which is what makes Rename safe to implement as "mutate the
    /// key on the same object and re-Upsert" rather than remove-then-add: the object stays the
    /// same list entry, so there is no window where the catalog holds two entries for one asset.
    ///
    /// Editor-only wherever <c>AssetDatabase</c> is needed — a built game has no <c>.asset</c>
    /// files to create or rewrite. Every entry point says so explicitly in a build rather than
    /// silently doing nothing, matching <see cref="SaveEditedDefinitions"/>.
    ///
    /// <c>EditorUtility.SetDirty</c> only, NEVER <c>Undo.RecordObject</c> — CLAUDE.md documents
    /// the incident where recording a bulk/asset-creation path onto the GLOBAL editor undo stack
    /// silently reverted 193 <c>BuildingTemplateData</c> assets in memory the first time anything
    /// else popped that stack.
    ///
    /// <c>templateDirOverride</c>-style parameters on the Create/Duplicate entry points mirror
    /// the seam <c>MonsterFramesImporter.Import</c> (<c>Scripts/Editor/Monsters/</c>) exposes for
    /// the same reason: production writes under <see cref="MONSTER_TEMPLATE_DIR"/>, and a test
    /// can point the same code at a scratch folder plus an in-memory <see cref="MonsterCatalog"/>
    /// so nothing shipped is ever at risk.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const string MONSTER_TEMPLATE_DIR = "Assets/_Project/Data/Catalogs/Monsters";

        /// <summary>
        /// FSM set every newly-created monster starts on. <c>Monster_Default</c> is the one set
        /// every shipped, working monster references (see <c>fsmSet:</c> across
        /// <c>Data/Catalogs/Monsters/*.asset</c>) — leaving this empty is what left
        /// <c>knight_red</c> on the hard-coded fallback for months.
        /// </summary>
        private const string DEFAULT_FSM_SET = "Monster_Default";

        // ── Confirm (Add-On-System → create) ────────────────────────────────────

        private void OnConfirmAddOnSystem()
        {
            if (_mode != EditorMode.AddOnSystem)
            {
                SetStatus("Confirm: switch to Add-On-System mode first.");
                return;
            }
#if UNITY_EDITOR
            if (_monsterCatalog == null)
            {
                SetStatus("Cannot create — no MonsterCatalog assigned.");
                return;
            }

            string key = ResolveUniqueKey(Slugify(_pendingKeyInput));
            var def = CreateAndRegisterDefinition(key);
            if (def == null)
            {
                SetStatus("Create failed — see console.");
                return;
            }

            _pendingKeyInput = "";
            SetMode(EditorMode.Select);
            SelectCategory(EntityCategory.Hostiles);
            SelectEntity(key);
            SetStatus($"Created monster '{key}'. Tune it in Properties, then Save.");
#else
            SetStatus("Add on System is Editor-only — a built game cannot create .asset files.");
#endif
        }

        /// <summary>
        /// Creates a brand-new <see cref="MonsterDefinition"/> with sane, non-zero starting
        /// stats (deliberately not the all-zero <c>mon1.asset</c> shape the audit flags),
        /// registers it on <see cref="_monsterCatalog"/>, and marks both dirty.
        ///
        /// Returns null — and writes nothing — when <paramref name="key"/> is empty or already
        /// claimed; the caller is expected to have resolved a unique key first
        /// (<see cref="ResolveUniqueKey"/>), so a non-null return here is "this call created
        /// exactly one new catalog entry."
        /// </summary>
        internal MonsterDefinition CreateAndRegisterDefinition(string key, string templateDirOverride = null)
        {
#if UNITY_EDITOR
            if (_monsterCatalog == null || string.IsNullOrWhiteSpace(key)) return null;
            if (_monsterCatalog.GetByKey(key) != null)
            {
                SetStatus($"Key '{key}' already exists.");
                return null;
            }

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey  = key;
            def.displayName = key;
            def.fsmSet      = DEFAULT_FSM_SET;
            def.stats       = NewMonsterDefaultStats();
            def.assetConfig = new EntityAssetConfig();

            string dir = string.IsNullOrEmpty(templateDirOverride) ? MONSTER_TEMPLATE_DIR : templateDirOverride;
            string assetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{dir}/{key}.asset");
            UnityEditor.AssetDatabase.CreateAsset(def, assetPath);

            _monsterCatalog.UpsertDefinition(def);
            UnityEditor.EditorUtility.SetDirty(_monsterCatalog);
            UnityEditor.EditorUtility.SetDirty(def);
            _pendingAssetWrites = true;

            RefreshPicker();
            return def;
#else
            return null;
#endif
        }

        // ── Duplicate ────────────────────────────────────────────────────────────

        /// <summary>
        /// Clones the definition selected in the Picker under a new key, so it is retunable
        /// immediately — the audit calls duplication "the most common real authoring action".
        /// <c>Object.Instantiate</c> deep-copies every serialized field (stats, sprite
        /// references, loot table, boss reference) exactly the way an Inspector "Duplicate"
        /// would; only the identity fields are then overwritten.
        /// </summary>
        internal MonsterDefinition DuplicateSelectedDefinition(string templateDirOverride = null)
        {
#if UNITY_EDITOR
            if (_monsterCatalog == null) { SetStatus("No MonsterCatalog assigned."); return null; }
            if (string.IsNullOrEmpty(_selectedKey) || _selectedIsPlayer)
            {
                SetStatus("Select a monster in the Picker to duplicate.");
                return null;
            }

            var source = _monsterCatalog.GetByKey(_selectedKey);
            if (source == null)
            {
                SetStatus($"'{_selectedKey}' not found in catalog.");
                return null;
            }

            string baseKey = string.IsNullOrWhiteSpace(_pendingKeyInput)
                ? source.monsterKey + "_copy"
                : Slugify(_pendingKeyInput);
            string newKey = ResolveUniqueKey(baseKey);

            var clone = Instantiate(source);
            clone.name       = newKey;
            clone.monsterKey = newKey;
            clone.displayName = string.IsNullOrWhiteSpace(_pendingKeyInput)
                ? (string.IsNullOrEmpty(source.displayName) ? newKey : source.displayName + " (Copy)")
                : _pendingKeyInput.Trim();

            string dir = string.IsNullOrEmpty(templateDirOverride) ? MONSTER_TEMPLATE_DIR : templateDirOverride;
            string assetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{dir}/{newKey}.asset");
            UnityEditor.AssetDatabase.CreateAsset(clone, assetPath);

            _monsterCatalog.UpsertDefinition(clone);
            UnityEditor.EditorUtility.SetDirty(_monsterCatalog);
            UnityEditor.EditorUtility.SetDirty(clone);
            _pendingAssetWrites = true;

            _pendingKeyInput = "";
            SelectEntity(newKey);
            SetStatus($"Duplicated '{source.monsterKey}' → '{newKey}'. Retune it in Properties.");
            return clone;
#else
            SetStatus("Duplicate is Editor-only — a built game cannot create .asset files.");
            return null;
#endif
        }

        // ── Rename ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-keys (and renames the display name of) whichever definition is selected in the
        /// Picker, using <paramref name="newKeyRaw"/> verbatim as the display name and its
        /// <see cref="Slugify"/>d form as <see cref="MonsterDefinition.monsterKey"/>.
        ///
        /// Mutates the SAME object and re-<c>Upsert</c>s it rather than removing the old entry
        /// and adding a new one: <see cref="MonsterCatalog.UpsertDefinition"/> matches by
        /// <c>monsterKey</c>, and since the definition already carries the new key by the time
        /// it is called, the list entry never needs touching — only its invalidated lookup
        /// cache does. Refuses a key collision with a DIFFERENT definition; refuses silently
        /// renaming the underlying <c>.asset</c> file only when the definition is not a
        /// persisted asset (e.g. an in-memory test fixture), in which case the catalog key
        /// still moves.
        /// </summary>
        internal bool RenameSelectedDefinition(string newKeyRaw)
        {
#if UNITY_EDITOR
            if (_monsterCatalog == null) { SetStatus("No MonsterCatalog assigned."); return false; }
            if (string.IsNullOrEmpty(_selectedKey) || _selectedIsPlayer)
            {
                SetStatus("Select a monster in the Picker to rename.");
                return false;
            }

            var def = _monsterCatalog.GetByKey(_selectedKey);
            if (def == null)
            {
                SetStatus($"'{_selectedKey}' not found in catalog.");
                return false;
            }

            string newKey = Slugify(newKeyRaw);
            if (string.IsNullOrEmpty(newKey))
            {
                SetStatus("Rename needs a non-empty key — type one in New/Rename Key first.");
                return false;
            }

            if (!string.Equals(newKey, def.monsterKey, System.StringComparison.Ordinal))
            {
                var collision = _monsterCatalog.GetByKey(newKey);
                if (collision != null && collision != def)
                {
                    SetStatus($"'{newKey}' is already used by another monster.");
                    return false;
                }
            }

            string oldKey = def.monsterKey;
            def.monsterKey  = newKey;
            def.displayName = string.IsNullOrWhiteSpace(newKeyRaw) ? def.displayName : newKeyRaw.Trim();

            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(def);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string err = UnityEditor.AssetDatabase.RenameAsset(assetPath, newKey);
                if (!string.IsNullOrEmpty(err))
                    Debug.LogWarning($"[EntitiesEditor] Asset file rename '{oldKey}' -> '{newKey}' failed: {err}");
            }

            // Same object reference stays in MonsterCatalog.Definitions; UpsertDefinition just
            // invalidates the by-key lookup so it re-indexes under the new key on next GetByKey.
            _monsterCatalog.UpsertDefinition(def);
            UnityEditor.EditorUtility.SetDirty(def);
            UnityEditor.EditorUtility.SetDirty(_monsterCatalog);
            _pendingAssetWrites = true;

            _pendingKeyInput = "";
            _selectedKey = newKey;
            RefreshPicker();
            ShowMonsterProperties(newKey);
            SetStatus($"Renamed '{oldKey}' → '{newKey}'.");
            return true;
#else
            SetStatus("Rename is Editor-only — a built game cannot rewrite .asset files.");
            return false;
#endif
        }

        // ── Shared helpers ───────────────────────────────────────────────────────

        /// <summary>Sane, non-zero starting stats for a brand-new monster. Chosen so a
        /// freshly-created definition is immediately fightable rather than shipping as another
        /// <c>mon1.asset</c>-style all-zero stub.</summary>
        private static EntityStats NewMonsterDefaultStats() => new EntityStats
        {
            hp                    = 20,
            speed                 = 2.5f,
            chasingSpeed          = 3.5f,
            defense               = 0,
            power                 = 0,
            meleeRange            = 1,
            meleeDamage           = 5,
            meleeCooldown         = 1f,
            aggroRange            = 6f,
            damageDuration        = 0.3f,
            damageStopProbability = 0.1f,
            attackWindupSeconds   = 0.3f,
            spawnCount            = 1,
            spawnPadding          = 0,
            spawnMargin           = 0,
            deathDisappearTime    = 3f,
            feetWidthFactor       = 0.5f,
            feetHeightFactor      = 0.3f,
            faction               = "EVIL",
            chatRange             = 0f,
        };

        /// <summary>Appends <c>_2</c>, <c>_3</c>, … until the key is not already claimed by
        /// <see cref="_monsterCatalog"/>. Empty/whitespace input falls back to "new_monster".</summary>
        private string ResolveUniqueKey(string baseKey)
        {
            string candidate = string.IsNullOrWhiteSpace(baseKey) ? "new_monster" : baseKey;
            if (_monsterCatalog.GetByKey(candidate) == null) return candidate;

            int n = 2;
            string result;
            do
            {
                result = $"{candidate}_{n}";
                n++;
            }
            while (_monsterCatalog.GetByKey(result) != null);
            return result;
        }

        /// <summary>
        /// Normalises free-typed text into the lowercase snake_case
        /// <c>MonsterFramesImporter</c> validates monster keys against
        /// (<c>char.IsLower(c) || char.IsDigit(c) || c == '_'</c>): lower-cases, folds
        /// spaces/dashes into underscores, drops anything else, and collapses repeats.
        ///
        /// Deliberately narrower than the importer's own check, which is Unicode-aware
        /// (<c>char.IsLower</c> accepts accented letters, e.g. 'ü') because it validates
        /// hand-authored manifest JSON rather than free-typed UI text. A monsterKey typed
        /// through a live text field is safer as plain ASCII a-z0-9_ — restricting here still
        /// produces a key the importer's own rule accepts, and avoids handing a designer a
        /// catalog key they cannot easily type back into the console (`spawn &lt;key&gt;`).
        /// </summary>
        private static string Slugify(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (c == '_' || c == '-' || c == ' ') sb.Append('_');
                // anything else (punctuation, accents, …) is dropped rather than rejected —
                // a designer typing a display name should not have to think about slugs.
            }

            string s = sb.ToString();
            while (s.Contains("__")) s = s.Replace("__", "_");
            return s.Trim('_');
        }
    }
}
