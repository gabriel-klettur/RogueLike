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
    /// <see cref="ParticlePresetFieldWriter"/> and pushed live to every matching emitter.
    /// Edits also autosave to the preset .asset (Unity Editor only) after a short
    /// debounce — see <see cref="MarkParticlePresetDirty"/> /
    /// <c>ParticlesRuntimeEditor.PresetPersistence.cs</c>.
    ///
    /// Editing here edits the PRESET — by design. A preset is the shared definition, so a
    /// change reaches every placed emitter of that preset, the preview, and everything
    /// placed from it afterwards. Per-instance overrides are a different feature with a
    /// different storage story (the instance record only persists preset id, position and
    /// scale) and are deliberately not smuggled in through this panel.
    ///
    /// What is NOT here: customSprite, flipbookFrames, the layers list, the size/alpha curves
    /// and gravityVector. ParticlePresetFieldWriter refuses arrays and UnityEngine.Object
    /// references outright — and gravityVector one step later, in TryConvert — so none of
    /// them can get a row until its widget exists (an asset picker, a list editor, a curve
    /// editor, a two-field row); the footer under the form says so, because a field that
    /// silently is not offered reads as a bug. Colours and the over-life gradient ARE here — the form
    /// drives them through the virtual keys ParticlePresetFieldWriter exposes (vfx.colors.a/b
    /// and vfx.colorOverLife.start/mid/end), which project the underlying arrays onto
    /// scalar-shaped colour rows WITHOUT rewriting them: the gradient rows address key[0],
    /// the interior key nearest t = 0.5, and key[last], and leave every other key alone.
    ///
    /// Every row here — DEPTH included — addresses the preset's ROOT vfx block. A composite
    /// preset's layers are references to OTHER presets, each carrying its own vfx and
    /// therefore its own sorting layer / order / fudge, so ordering a stack internally means
    /// selecting each layer preset in turn. The same boundary applies to the live re-apply
    /// below: ReapplyPresetToLiveEmitters matches emitters PLACED as this preset, so editing
    /// a preset that is only ever used as someone else's layer refreshes the preview but not
    /// the composite emitters standing in the world until they are re-applied.
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

                // COPY ON PLACE: with an emitter selected the rows show and edit THAT
                // placement's own configuration; with only a preset selected they show the
                // asset, which decides what the next placement is born with. See
                // ParticlesRuntimeEditor.InstanceEditing.cs.
                var v = PropertyFormSource(def, pid);
                if (v == null) return;

                form.ValueChanged = OnPresetPropertyChanged;

                // How far an edit reaches, pinned above the tabs so it is on screen whichever
                // tab is open. It is the one thing about this panel that cannot be inferred by
                // looking at it, and getting it wrong used to mean changing eighty-four
                // emitters while believing you were changing one.
                form.AddHeader(PropertyFormScopeHeader(pid));

                form.AddText("displayName", "Name", def.displayName);
                form.AddText("vfx.kind", "Kind", v.kind);

                // Emission, Size, Spawn Area and Shape Extent: what the system emits and where.
                form.BeginTab("General");
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

                // Direction, Motion, Orbit, Rotation, Noise and Sway: how a particle travels.
                form.BeginTab("Motion");
                form.AddHeader("DIRECTION");
                form.AddFloat("vfx.directionDegrees", "Heading deg (-1 off)", v.directionDegrees);
                form.AddFloat("vfx.directionSpreadDegrees", "Spread deg", v.directionSpreadDegrees);

                form.AddHeader("MOTION");
                form.AddFloat("vfx.speed", "Speed", v.speed);
                form.AddFloat("vfx.gravity", "Gravity (accel)", v.gravity);
                form.AddBool("vfx.useGravityVector", "Constant Velocity", v.useGravityVector);
                form.AddFloat("vfx.drag", "Drag", v.drag);
                form.AddBool("vfx.worldSpace", "World Space", v.worldSpace);

                // Orbit sits under MOTION because it is motion the other rows cannot
                // express: Speed throws each particle along its own spawn direction, so a
                // swarm of them is a starburst, never a swirl. Reaching the centre takes
                // Radius / |Pull| seconds — the form cannot enforce that against Lifespan,
                // so the label says which direction the sign goes and the rest is authoring.
                form.AddHeader("ORBIT");
                form.AddFloat("vfx.orbitalSpeedDegrees", "Orbit deg/s", v.orbitalSpeedDegrees);
                form.AddFloat("vfx.radialSpeed", "Pull u/s (-in)", v.radialSpeed);

                // The colour ramp and its keys.
                form.BeginTab("Colour");
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

                // Three rows over an array that may hold up to eight keys. Which key each
                // row shows is resolved by ParticlePresetFieldWriter.MidStopIndex — the
                // SAME call the writer makes — because a row that displays one key and
                // writes another is a lie the author cannot see. Birth is key[0], Death is
                // key[last], Middle is the interior key nearest t = 0.5. Keys the rows do
                // not address keep their times and colours; the footer says how many.
                var col = v.colorOverLife;
                int nKeys  = col?.Length ?? 0;
                int midIdx = ParticlePresetFieldWriter.MidStopIndex(col);

                form.AddHeader(nKeys > 3 ? $"COLOUR OVER LIFE ({nKeys} KEYS)" : "COLOUR OVER LIFE");

                Color g0 = nKeys > 0 ? col[0].color : Color.white;
                Color g2 = nKeys > 0 ? col[nKeys - 1].color : Color.white;
                // Below three keys there is no interior key yet; show the colour the
                // gradient actually evaluates midway, which is what the writer will
                // materialise if this row is edited.
                Color g1 = midIdx >= 0 ? col[midIdx].color : Color.Lerp(g0, g2, 0.5f);

                // Past three keys the rows name the key they own, so "the middle one"
                // stops being a guess.
                string birthLbl  = nKeys > 3 ? $"Birth (1/{nKeys})" : "Birth";
                string middleLbl = nKeys > 3 ? $"Middle ({midIdx + 1}/{nKeys})" : "Middle";
                string deathLbl  = nKeys > 3 ? $"Death ({nKeys}/{nKeys})" : "Death";
                form.AddColor("vfx.colorOverLife.start", birthLbl, g0);
                form.AddColor("vfx.colorOverLife.mid", middleLbl, g1);
                form.AddColor("vfx.colorOverLife.end", deathLbl, g2);

                // Texture, Depth and Flipbook: how the quad is drawn and where in the stack.
                form.BeginTab("Render");
                form.AddHeader("TEXTURE");
                form.AddDropdown("vfx.textureShape", "Shape",
                    Enum.GetNames(typeof(ParticleTextureShape)), (int)v.textureShape);
                form.AddFloat("vfx.textureSoftness", "Softness", v.textureSoftness);
                form.AddBool("vfx.additive", "Additive (glow)", v.additive);

                // Directly under TEXTURE because the two sections together are everything
                // ConfigureRenderer writes — one trip through ApplyPreset settles both.
                //
                // Depth has to be authored per preset because the layer stack cannot express
                // what these effects need on its own. Buildings occupy WallsBottom and
                // WallsTop (BuildingObject picks by the instance's z offset) with Entities
                // between them, and there is NO layer between WallsBottom and Entities — so
                // "in front of a wall body" and "behind the player" cannot both be true.
                // Which one a preset wants is an art decision that changes per preset (a leaf
                // falling down a trunk wants one, a spell impact wants the other), so it
                // belongs to whoever authors the preset rather than to a constant in
                // ConfigureRenderer. Leaving it hard-coded at VFX is what put every falling
                // leaf in front of every wall top in the first place.
                form.AddHeader("DEPTH");

                // A dropdown off the LIVE SortingLayer.layers, never a text field. The stored
                // value is a name; an unknown one renders on VFX with a warning
                // (ParticleEmitter.ResolveSortingLayerName) rather than failing loudly, so a
                // typed typo would be invisible until someone read the console. The list is
                // also the draw-order stack itself, so the author picks a POSITION — "above
                // WallsBottom, below Entities" — instead of recalling a name.
                //
                // Options and selection both come from ParticlePresetFieldWriter, which is
                // where the index is resolved back into a name; see SortingLayerNames for why
                // an empty authored value shows as VFX instead of getting its own entry.
                form.AddDropdown("vfx.sortingLayer", "Sorting Layer",
                    ParticlePresetFieldWriter.SortingLayerNames(v.sortingLayer),
                    ParticlePresetFieldWriter.SortingLayerIndex(v.sortingLayer));
                form.AddInt("vfx.sortingOrder", "Order in Layer", v.sortingOrder);

                // Read as the third rung of the same ladder: layer, then order within the
                // layer, then this — a depth bias applied within one layer AND one order.
                // Unity adds it to the system's camera distance before the transparency
                // sort, so LOWER draws in front, which the label has to say because the sign
                // is the opposite of Order's. It is also the ONLY thing that can order the
                // co-located systems of a composite preset against each other: they share a
                // layer and an order and the loader pins every emitter to z = 0, so without
                // it their draw order is Unity's internal tie-break. PropertyForm has no
                // tooltip channel, so the full version lives on the field's [Tooltip] in
                // ParticleVfxParams and is visible from the Inspector.
                form.AddFloat("vfx.sortingFudge", "Bias (lower = front)", v.sortingFudge);

                form.BeginTab("Motion");
                form.AddHeader("ROTATION");
                form.AddFloat("vfx.rotationSpeedDegrees", "Spin deg/s", v.rotationSpeedDegrees);
                form.AddBool("vfx.rotationOneWay", "Spin One Way", v.rotationOneWay);
                form.AddFloat("vfx.startRotationJitterDegrees", "Start Jitter deg", v.startRotationJitterDegrees);
                form.AddInt("vfx.turnoverCycles", "Turnover Cycles", v.turnoverCycles);
                form.AddFloat("vfx.turnoverMinWidth", "Edge-on Width", v.turnoverMinWidth);

                form.AddHeader("NOISE");
                form.AddBool("vfx.noiseEnabled", "Enabled", v.noiseEnabled);
                form.AddFloat("vfx.noiseStrength", "Strength", v.noiseStrength);
                form.AddFloat("vfx.noiseFrequency", "Frequency", v.noiseFrequency);
                form.AddFloat("vfx.noiseScrollSpeed", "Scroll Speed", v.noiseScrollSpeed);
                form.AddFloat("vfx.noiseVerticalScale", "Vertical Share", v.noiseVerticalScale);

                // Sits directly under NOISE because the two are mutually exclusive: the
                // emitter takes the legacy sway branch only when noiseEnabled is OFF and the
                // kind is falling_leaf (ParticleEmitter.ParticleSystem.cs). For every legacy
                // falling preset — which is all of them, none has noise on — these two ARE
                // the flutter, and until now the form offered no way to reach them: the
                // author could see the motion was wrong and had no field to fix it.
                // swayAmp is a world-unit amplitude scaled by the emitter's scale;
                // swaySpeed is the flutter frequency in cycles per second.
                form.AddHeader("SWAY (LEGACY, NOISE OFF)");
                form.AddFloat("vfx.swayAmp", "Sway Amp", v.swayAmp);
                form.AddFloat("vfx.swaySpeed", "Sway Cycles/s", v.swaySpeed);

                form.BeginTab("General");
                form.AddHeader("SHAPE EXTENT");
                form.AddFloat("vfx.radius", "Radius", v.radius);
                // -1 means "whatever the kind hard-codes"; 0 rim, 1 whole area.
                form.AddFloat("vfx.shapeFill", "Fill (-1 kind)", v.shapeFill);
                form.AddFloat("vfx.outerRadius", "Outer Radius", v.outerRadius);
                form.AddFloat("vfx.arcRangeDegrees", "Arc deg", v.arcRangeDegrees);
                form.AddInt("vfx.segments", "Segments", v.segments);
                form.AddFloat("vfx.lightningOffset", "Zigzag Offset", v.lightningOffset);
                form.AddFloat("vfx.thickness", "Thickness", v.thickness);

                form.BeginTab("Render");
                form.AddHeader("FLIPBOOK");
                form.AddInt("vfx.flipbookCycles", "Cycles / Life", v.flipbookCycles);
                form.AddBool("vfx.flipbookRandomStartFrame", "Random Start", v.flipbookRandomStartFrame);
            }
            finally
            {
                _propsFormRebuilding = false;
            }
        }

        /// <summary>
        /// How many emitters standing in the world are running this preset — including the
        /// ones culled out of frame, which is why it counts the identity component rather than
        /// live <see cref="ParticleEmitter"/>s: the loader deactivates emitters off-camera and
        /// an author would otherwise watch the number drop as they walked away.
        ///
        /// Layers do not count. A composite spawns its layer presets through ONE placed
        /// instance, so a layer preset reads as "not placed" even though editing it changes
        /// every composite that carries it — that is the next thing to say here if it ever
        /// bites, and it needs the catalog walked rather than the scene.
        /// </summary>
        private static int CountPlacedUsing(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return 0;

            int count = 0;
            foreach (var instance in FindObjectsOfType<PersistedParticleInstance>(true))
            {
                if (instance == null) continue;
                if (string.Equals(instance.PresetId, presetId, System.StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        private void OnPresetPropertyChanged(string key, object value)
        {
            if (_propsFormRebuilding) return;

            // EditMode-test safety, same rule as the JSON stores: a fixture that builds this
            // editor and pokes a row must not be able to dirty a real .asset on disk.
            if (!Application.isPlaying) return;

            var def = _catalog?.GetById(_propsFormPresetId);
            if (def == null) return;

            bool editingInstance = ActiveConfigEmitter(_propsFormPresetId) != null;

            if (!TryApplyPropertyEdit(def, _propsFormPresetId, key, value, out string error))
            {
                SetStatus($"Edit rejected: {error}");
                // Put the row back to the real value so the UI cannot display a lie.
                RebuildPresetPropertyForm(_propsFormPresetId);
                return;
            }

            if (editingInstance)
            {
                // Nothing else in the world is touched, and nothing is written to the asset:
                // the instance edit already saved itself through ExecutePersistedEdit.
                RefreshTable();
                RebuildPresetPropertyForm(_propsFormPresetId);
                SetStatus($"{key} updated on this placement — the preset and every other " +
                          "placement of it are untouched.");
                return;
            }

            MarkParticlePresetDirty(def);

            // Placed emitters own their configuration and are deliberately NOT refreshed here;
            // the preview is what has to answer, because it is showing what a new placement
            // would look like.
            _previewService?.SetSelectedPreset(_propsFormPresetId, def);
            RefreshTable();

            // The clamp may have adjusted the typed value; reflect what was stored.
            RebuildPresetPropertyForm(_propsFormPresetId);

            int placed = CountPlacedUsing(_propsFormPresetId);
            SetStatus($"'{_propsFormPresetId}' {key} updated — reaches NEW placements. " +
                      $"{placed} already placed keep their own copy. Autosaves to the .asset.");
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
    }
}
