using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors.Boss;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Boss Editor hand-off helper for the Entities Runtime Editor (F5).
    ///
    /// When a monster that is a <see cref="BossDefinition.baseMonster"/> is
    /// selected in the picker or by clicking on the map, an "Open Boss Editor →"
    /// button appears at the bottom of the Properties panel. Clicking it opens
    /// the Boss Editor with the matching <see cref="BossDefinition"/> pre-selected
    /// via <see cref="BossEditorManager.OpenWithBoss"/>.
    ///
    /// The boss-definition scan uses <c>AssetDatabase</c> (Editor-only) and
    /// results are cached until the editor deactivates so repeated clicks are fast.
    /// </summary>
    public partial class EntitiesRuntimeEditor
        : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Boss-definition lookup cache ──────────────────────────────────────

        // Maps monsterKey → BossDefinition (populated lazily on first access).
        private Dictionary<string, BossDefinition> _bossDefByKey;

        private void EnsureBossDefCache()
        {
            if (_bossDefByKey != null) return;
            _bossDefByKey = new Dictionary<string, BossDefinition>(
                System.StringComparer.OrdinalIgnoreCase);

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets(
                "t:BossDefinition",
                new[] { "Assets/_Project/Data" });
            foreach (var guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<BossDefinition>(path);
                if (def == null || def.baseMonster == null) continue;
                string key = def.baseMonster.monsterKey;
                if (!string.IsNullOrEmpty(key))
                    _bossDefByKey[key] = def;
            }
#endif
        }

        private BossDefinition FindBossDefForMonsterKey(string monsterKey)
        {
            if (string.IsNullOrEmpty(monsterKey)) return null;

            // Forward reference first. MonsterDefinition now carries `bossDefinition`
            // outright — it is what EntitySetup.ConfigureMonster reads to attach the phase
            // controller — so the monster already knows it is a boss and no lookup is
            // needed. This path also works in a build, unlike the scan below.
            if (_monsterCatalog != null)
            {
                var monster = _monsterCatalog.GetByKey(monsterKey);
                if (monster != null && monster.bossDefinition != null)
                    return monster.bossDefinition;
            }

            // Reverse scan, kept for a BossDefinition authored before the forward field
            // existed: it names its baseMonster but the monster does not name it back.
            // Editor-only by construction (AssetDatabase), which is exactly why it cannot
            // be the primary path.
            EnsureBossDefCache();
            _bossDefByKey.TryGetValue(monsterKey, out var def);
            return def;
        }

        // ── Boss button wiring ─────────────────────────────────────────────────

        /// <summary>
        /// Shows or hides the "Open Boss Editor →" button in the Properties panel.
        /// Called from ShowMonsterProperties whenever a monster is selected.
        /// </summary>
        private void UpdateBossHandoffButton(string monsterKey)
        {
            if (_ui.BossHandoffBtnGo == null) return;

            var bossDef = FindBossDefForMonsterKey(monsterKey);
            if (bossDef == null)
            {
                _ui.BossHandoffBtnGo.SetActive(false);
                return;
            }

            _ui.BossHandoffBtnGo.SetActive(true);

            // Wire the click on first reveal (guard against double-subscribe).
            var btn = _ui.BossHandoffBtnGo.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                var captured = bossDef;
                btn.onClick.AddListener(() => OpenBossEditor(captured));
            }
        }

        private void OpenBossEditor(BossDefinition bossDef)
        {
            var bossEditor = BossEditorManager.Instance;
            if (bossEditor == null)
            {
                Debug.LogWarning("[EntitiesEditor] BossEditorManager not found in scene.");
                return;
            }

            // Route through GameEditorManager so the Entities Editor is closed and
            // the Boss Editor is opened in exclusive mode — exactly one editor active.
            bossEditor.OpenWithBoss(bossDef);
        }
    }
}
