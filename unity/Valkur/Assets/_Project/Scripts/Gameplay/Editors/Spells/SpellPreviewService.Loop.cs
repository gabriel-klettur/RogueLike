using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    public sealed partial class SpellPreviewService
    {
        // ── Firing and stage lifecycle ────────────────────────────────────────────

        private void FireOnce()
        {
            if (_spell == null || _casterTransform == null) return;

            // Reset the caster to the centre of the stage at the start of every
            // cast so spells that displace the caster (Dash, Teleport) don't
            // progressively drift off-screen across cycles.
            _casterTransform.localPosition = Vector3.zero;

            int layer = ResolvePreviewLayer();

            var executor = SpellCaster.GetExecutor(_spell.type);
            if (executor == null)
            {
                if (!_warnedNoExecutor)
                {
                    Debug.LogWarning($"[SpellPreview] No executor registered for SpellType {_spell.type}");
                    _warnedNoExecutor = true;
                }
                return;
            }
            _warnedNoExecutor = false;

            var ctx = new SpellContext
            {
                Spell            = _spell,
                Caster           = _casterTransform,
                Direction        = _direction,
                TargetLayers     = 0,
                ProjectilePrefab = ResolveProjectilePrefab(),
            };

            _baselineSceneRoots.Clear();
            foreach (var go in SnapshotSceneRootGameObjects())
                _baselineSceneRoots.Add(go);

            try { executor.Execute(ctx); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SpellPreview] Executor '{executor.GetType().Name}' threw for spell '{_spell.spellKey}': {ex.Message}");
            }

            SetLayerRecursive(_stageRoot, layer);
            AbsorbNewSceneRoots();
        }

        /// <summary>
        /// Diffs the current scene-root GameObjects against _baselineSceneRoots and
        /// re-layers + tracks any new entries onto the SpellPreview layer.
        /// Self-deduplicating — once a GO is absorbed it's added to the baseline.
        /// </summary>
        private void AbsorbNewSceneRoots()
        {
            int layer = ResolvePreviewLayer();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go == null) continue;
                if (_baselineSceneRoots.Contains(go)) continue;
                if (go == _stageRoot) continue;
                if (_camera != null && go == _camera.gameObject) continue;
                SetLayerRecursive(go, layer);
                _trackedWorldSpawns.Add(go);
                _baselineSceneRoots.Add(go);
            }
        }

        private static HashSet<GameObject> SnapshotSceneRootGameObjects()
        {
            var set = new HashSet<GameObject>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
                if (go != null) set.Add(go);
            return set;
        }

        /// <summary>
        /// Destroys the current synthetic caster GO and replaces it with a fresh one
        /// parented under _stageRoot. This is the only reliable way to strip all
        /// components added directly to the caster by executors (e.g. LaserBeamController).
        /// </summary>
        private void RebuildCasterGo()
        {
            if (_stageRoot == null) return;
            int layer = ResolvePreviewLayer();

            // Null the reference before SafeDestroy so the old _characterGo
            // (child of _casterGo) doesn't dangle after the parent is destroyed.
            _characterGo = null;
            if (_casterGo != null) SafeDestroy.Of(_casterGo);

            _casterGo = new GameObject("SpellPreviewCaster");
            _casterGo.transform.SetParent(_stageRoot.transform, false);
            _casterGo.layer = layer;
            _casterTransform = _casterGo.transform;

            ApplyCharacterState();
        }

        private void ClearStage()
        {
            if (_stageRoot != null && _casterGo != null)
            {
                for (int i = _stageRoot.transform.childCount - 1; i >= 0; i--)
                {
                    var c = _stageRoot.transform.GetChild(i);
                    if (c != null && c.gameObject != _casterGo)
                        SafeDestroy.Of(c.gameObject);
                }
            }

            // Skip the character overlay so it persists across cycles.
            if (_casterTransform != null)
            {
                for (int i = _casterTransform.childCount - 1; i >= 0; i--)
                {
                    var c = _casterTransform.GetChild(i);
                    if (c == null) continue;
                    if (_characterGo != null && c.gameObject == _characterGo) continue;
                    SafeDestroy.Of(c.gameObject);
                }
            }

            for (int i = 0; i < _trackedWorldSpawns.Count; i++)
            {
                var go = _trackedWorldSpawns[i];
                if (go != null) SafeDestroy.Of(go);
            }
            _trackedWorldSpawns.Clear();

            // Absorbed VFX are owned by VFXManager's pool — just reset tracking so
            // the next cycle's absorber re-layers them if Unity re-uses the instance.
            _absorbedWorldVfx.Clear();
        }

        // ── VFX absorption ────────────────────────────────────────────────────────

        private void CaptureWorldVfxBaseline()
        {
            _baselineWorldVfx.Clear();
            _absorbedWorldVfx.Clear();
            var root = ResolveVfxScanRoot();
            if (root != null) CaptureSubtreeBaseline(root);
        }

        private void CaptureSubtreeBaseline(Transform t)
        {
            if (t == null) return;
            _baselineWorldVfx.Add(t.gameObject);
            int n = t.childCount;
            for (int i = 0; i < n; i++)
                CaptureSubtreeBaseline(t.GetChild(i));
        }

        /// <summary>
        /// Re-layers every new GameObject under the VFXManager subtree onto the
        /// SpellPreview Unity layer so the dedicated preview camera renders it.
        /// Scanning only the VFXManager subtree keeps the cost low and avoids
        /// re-layering unrelated world VFX.
        /// </summary>
        private void AbsorbNewWorldVfx()
        {
            var root = ResolveVfxScanRoot();
            if (root == null) return;
            int layer = ResolvePreviewLayer();
            AbsorbSubtree(root, layer);
        }

        private void AbsorbSubtree(Transform t, int layer)
        {
            if (t == null) return;
            var go = t.gameObject;
            if (!_baselineWorldVfx.Contains(go) && !_absorbedWorldVfx.Contains(go))
            {
                if (go.layer != layer) go.layer = layer;
                _absorbedWorldVfx.Add(go);
            }
            int n = t.childCount;
            for (int i = 0; i < n; i++)
                AbsorbSubtree(t.GetChild(i), layer);
        }

        private static Transform ResolveVfxScanRoot()
        {
            return VFXManager.Instance != null ? VFXManager.Instance.transform : null;
        }

        // ── Projectile prefab resolution ──────────────────────────────────────────

        private GameObject ResolveProjectilePrefab()
        {
            if (_projectilePrefabResolved) return _projectilePrefab;
            _projectilePrefabResolved = true;
            var caster = Object.FindObjectOfType<SpellCaster>();
            _projectilePrefab = caster != null ? caster.ProjectilePrefab : null;
            return _projectilePrefab;
        }

        // ── Cycle timing ──────────────────────────────────────────────────────────

        private static float ComputeCycleTime(SpellDefinition s)
        {
            // Meteor showers fire one missile per meteorInterval over meteorCount
            // events, plus fall time per missile. Without this branch the cycle ends
            // at MIN_CYCLE_SECONDS before the last meteors land.
            if (s.type == SpellType.Meteor && s.meteorCount > 0)
            {
                float interval = s.meteorInterval > 0 ? s.meteorInterval : 0.25f;
                return s.meteorCount * interval + 1.0f + CYCLE_TAIL_SECONDS;
            }

            bool persistent = s.type == SpellType.Aura
                           || s.type == SpellType.Puddle
                           || s.type == SpellType.VortexField
                           || s.type == SpellType.Wall
                           || s.type == SpellType.Totem
                           || s.type == SpellType.SphereMagicShield;

            float t = persistent
                ? Mathf.Max(s.duration, MIN_PERSISTENT_SECONDS)
                : Mathf.Max(s.prepareDuration + s.channelDuration + s.lifetime, MIN_CYCLE_SECONDS);
            return t + CYCLE_TAIL_SECONDS;
        }
    }
}
