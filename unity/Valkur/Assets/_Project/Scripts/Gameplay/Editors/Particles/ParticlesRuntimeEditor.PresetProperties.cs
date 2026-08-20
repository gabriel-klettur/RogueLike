using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// The editable half of the Properties panel: one row per authorable
    /// <see cref="ParticleVfxParams"/> field, applied through
    /// <see cref="ParticlePresetFieldWriter"/> and pushed live.
    ///
    /// Editing here edits the PRESET — by design. A preset is the shared definition, so a
    /// change reaches every placed emitter of that preset, the preview, and everything
    /// placed from it afterwards. Per-instance overrides are a different feature with a
    /// different storage story (the instance record only persists preset id, position and
    /// scale) and are deliberately not smuggled in through this panel.
    ///
    /// What is NOT here: colour lists, over-life curves, sprites and gravityVector. Those
    /// need dedicated widgets (swatch, curve editor, picker, two-field row) that
    /// PropertyForm does not have yet; the footer under the form says so, because a field
    /// that silently is not offered reads as a bug.
    /// </summary>
    public partial class ParticlesRuntimeEditor
    {
        private string _propsFormPresetId;
        private bool   _propsFormRebuilding;

        /// <summary>Rows are keyed "vfx.field" (nested) or "field" (definition-level).</summary>
        private void RebuildPresetPropertyForm(string pid)
        {
            var form = _ui.PresetPropsForm;
            if (form == null) return;

            _propsFormPresetId = pid;
            var def = _catalog?.GetById(pid);

            // Rebuilding fires no ValueChanged, but guard anyway: SetValue during a future
            // refactor must never be able to loop back into OnPresetPropertyChanged.
            _propsFormRebuilding = true;
            try
            {
                form.Clear();
                if (def == null || def.vfx == null) return;
                var v = def.vfx;

                form.ValueChanged = OnPresetPropertyChanged;

                form.AddHeader("IDENTITY");
                form.AddText("displayName", "Name", def.displayName);
                form.AddText("vfx.kind", "Kind", v.kind);

                form.AddHeader("EMISSION");
                form.AddBool("vfx.loops", "Loops", v.loops);
                form.AddFloat("vfx.emitRate", "Emit Rate /s", v.emitRate);
                form.AddInt("vfx.count", "Burst Count", v.count);
                form.AddFloat("vfx.burstIntervalSeconds", "Burst Interval s", v.burstIntervalSeconds);
                form.AddFloat("vfx.lifespan", "Lifespan s", v.lifespan);

                form.AddHeader("SIZE");
                // sizeMin/sizeMax are the HEIGHT; aspect scales width against it. Labelled
                // that way or designers hunt for a Width field that does not exist.
                form.AddFloat("vfx.sizeMin", "Height Min", v.sizeMin);
                form.AddFloat("vfx.sizeMax", "Height Max", v.sizeMax);
                form.AddFloat("vfx.sizeAspect", "Width / Height", v.sizeAspect);

                form.AddHeader("SPAWN AREA");
                // 0 keeps the kind's built-in shape; either above 0 engages a centred box.
                form.AddFloat("vfx.spawnWidth", "Area Width", v.spawnWidth);
                form.AddFloat("vfx.spawnHeight", "Area Height", v.spawnHeight);
                form.AddFloat("vfx.dispersion", "Spread (kind shape)", v.dispersion);

                form.AddHeader("DIRECTION");
                form.AddFloat("vfx.directionDegrees", "Heading deg (-1 off)", v.directionDegrees);
                form.AddFloat("vfx.directionSpreadDegrees", "Spread deg", v.directionSpreadDegrees);

                form.AddHeader("MOTION");
                form.AddFloat("vfx.speed", "Speed", v.speed);
                form.AddFloat("vfx.gravity", "Gravity (accel)", v.gravity);
                form.AddBool("vfx.useGravityVector", "Constant Velocity", v.useGravityVector);
                form.AddFloat("vfx.drag", "Drag", v.drag);
                form.AddBool("vfx.worldSpace", "World Space", v.worldSpace);

                form.AddHeader("COLOUR");
                form.AddColor("vfx.color", "Base", v.color);
                // The variation pair: BuildColorParameter randomises between the first and
                // last entries and ignores everything between, so A and B are the whole
                // authorable surface.
                var colsArr = v.colors;
                Color varA = (colsArr != null && colsArr.Length > 0) ? colsArr[0] : v.color;
                Color varB = (colsArr != null && colsArr.Length > 1) ? colsArr[colsArr.Length - 1] : varA;
                form.AddColor("vfx.colors.a", "Variation A", varA);
                form.AddColor("vfx.colors.b", "Variation B", varB);
                form.AddFloat("vfx.colorIntensity", "Intensity", v.colorIntensity);

                form.AddHeader("COLOUR OVER LIFE");
                var col = v.colorOverLife;
                Color g0 = (col != null && col.Length > 0) ? col[0].color : Color.white;
                Color g2 = (col != null && col.Length > 0) ? col[col.Length - 1].color : Color.white;
                Color g1 = (col != null && col.Length >= 3) ? col[col.Length / 2].color
                                                            : Color.Lerp(g0, g2, 0.5f);
                form.AddColor("vfx.colorOverLife.start", "Birth", g0);
                form.AddColor("vfx.colorOverLife.mid", "Middle", g1);
                form.AddColor("vfx.colorOverLife.end", "Death", g2);

                form.AddHeader("TEXTURE");
                form.AddDropdown("vfx.textureShape", "Shape",
                    Enum.GetNames(typeof(ParticleTextureShape)), (int)v.textureShape);
                form.AddFloat("vfx.textureSoftness", "Softness", v.textureSoftness);
                form.AddBool("vfx.additive", "Additive (glow)", v.additive);

                form.AddHeader("ROTATION");
                form.AddFloat("vfx.rotationSpeedDegrees", "Spin deg/s", v.rotationSpeedDegrees);
                form.AddFloat("vfx.startRotationJitterDegrees", "Start Jitter deg", v.startRotationJitterDegrees);
                form.AddInt("vfx.turnoverCycles", "Turnover Cycles", v.turnoverCycles);
                form.AddFloat("vfx.turnoverMinWidth", "Edge-on Width", v.turnoverMinWidth);

                form.AddHeader("NOISE");
                form.AddBool("vfx.noiseEnabled", "Enabled", v.noiseEnabled);
                form.AddFloat("vfx.noiseStrength", "Strength", v.noiseStrength);
                form.AddFloat("vfx.noiseFrequency", "Frequency", v.noiseFrequency);
                form.AddFloat("vfx.noiseScrollSpeed", "Scroll Speed", v.noiseScrollSpeed);
                form.AddFloat("vfx.noiseVerticalScale", "Vertical Share", v.noiseVerticalScale);

                form.AddHeader("SHAPE EXTENT");
                form.AddFloat("vfx.radius", "Radius", v.radius);
                form.AddFloat("vfx.outerRadius", "Outer Radius", v.outerRadius);
                form.AddFloat("vfx.arcRangeDegrees", "Arc deg", v.arcRangeDegrees);
                form.AddInt("vfx.segments", "Segments", v.segments);
                form.AddFloat("vfx.lightningOffset", "Zigzag Offset", v.lightningOffset);
                form.AddFloat("vfx.thickness", "Thickness", v.thickness);

                form.AddHeader("FLIPBOOK");
                form.AddInt("vfx.flipbookCycles", "Cycles / Life", v.flipbookCycles);
                form.AddBool("vfx.flipbookRandomStartFrame", "Random Start", v.flipbookRandomStartFrame);
            }
            finally
            {
                _propsFormRebuilding = false;
            }
        }

        private void OnPresetPropertyChanged(string key, object value)
        {
            if (_propsFormRebuilding) return;

            // EditMode-test safety, same rule as the JSON stores: a fixture that builds this
            // editor and pokes a row must not be able to dirty a real .asset on disk.
            if (!Application.isPlaying) return;

            var def = _catalog?.GetById(_propsFormPresetId);
            if (def == null) return;

            if (!ParticlePresetFieldWriter.TrySetField(def, key, value, out string error))
            {
                SetStatus($"Edit rejected: {error}");
                // Put the row back to the real value so the UI cannot display a lie.
                RebuildPresetPropertyForm(_propsFormPresetId);
                return;
            }

            MarkParticlePresetDirty(def);

            int touched = ReapplyPresetToLiveEmitters(def);
            _previewService?.SetSelectedPreset(_propsFormPresetId, def);
            RefreshTable();

            // The clamp may have adjusted the typed value; reflect what was stored.
            RebuildPresetPropertyForm(_propsFormPresetId);

            SetStatus($"'{_propsFormPresetId}' {key} updated — {touched} live emitter(s) refreshed. " +
                      "Save writes it to the asset.");
        }

        /// <summary>
        /// Push the edited preset onto every live emitter of that preset, so the world
        /// answers the edit immediately. ApplyPreset already runs Stop → Configure → Play,
        /// which sidesteps Unity's refusal to assign main.duration on a playing system.
        /// </summary>
        private int ReapplyPresetToLiveEmitters(ParticlePresetDefinition def)
        {
            int n = 0;
            var all = FindObjectsOfType<ParticleEmitter>(includeInactive: true);
            foreach (var em in all)
            {
                if (em == null) continue;
                if (IsPreviewEmitter(em.gameObject)) continue;   // the service re-applies its own
                string pid = GetPresetIdFromGo(em.gameObject);
                if (!string.Equals(pid, def.id, StringComparison.OrdinalIgnoreCase)) continue;

                float scale = 1f;
                var identity = em.GetComponent<PersistedParticleInstance>();
                if (identity != null) scale = identity.ScaleMultiplier;
                em.ApplyPreset(def, scale);
                n++;
            }
            return n;
        }

        /// <summary>
        /// Flush edited preset assets to disk. Runtime edits mutate the loaded asset, and
        /// in the Unity Editor that survives Play Mode — but only reaches the .asset file
        /// on an AssetDatabase flush. In a build there is no asset database; saying so
        /// beats a Save button that silently lies (the Spells editor set this precedent).
        /// </summary>
        private void FlushPresetAssetsToDisk()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.SaveAssets();
            SetStatus("Preset changes written to their .asset files.");
#else
            SetStatus("Preset saving requires the Unity Editor; edits last this session only.");
#endif
        }
    }
}
