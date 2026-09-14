using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Shaping an entity's collision layers on the frame itself: the capsules of its HURTBOX
    /// (what a blow can land on) and the size of its FOOTPRINT (what stands on the ground).
    ///
    /// <para>The bake measures every frame from its silhouette and is right most of the time; this
    /// is where a human fixes the frames it is not — a raised weapon it took for an arm, a cape
    /// it took for a body. <b>An edited frame is written with its MIRROR</b>: the two halves are
    /// the same drawing, the profile stores X forward along the facing, so one number serves
    /// both, and fixing only the half being looked at would leave the creature half-fixed
    /// depending on which way it faces the player.</para>
    ///
    /// <para>Writes go through <see cref="CommitDefinitionEdit"/>, the editor's one mutation
    /// seam: undoable, live monsters reconfigured, and SAVE to reach the disk.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        private enum CollisionGesture { Move = 0, Resize = 1 }

        [Valkur.Core.SelfHealingStatic("Constant label table; written once at class init, never mutated.")]
        private static readonly string[] CollisionGestureNames = { "Mover", "Escalar" };

        private bool _collisionView;
        private bool _collisionEditing;
        private int _collisionShape;
        private CollisionGesture _collisionGesture = CollisionGesture.Move;
        private readonly List<HurtShape> _collisionScratch = new List<HurtShape>(EntityCollisionProfile.MaxShapesPerFrame);
        private readonly List<string> _collisionShapeLabels = new List<string>(EntityCollisionProfile.MaxShapesPerFrame);

        private EntitiesEditorUIBuilder.CollisionCallbacks BuildCollisionCallbacks() =>
            new EntitiesEditorUIBuilder.CollisionCallbacks
            {
                OnToggleView       = OnToggleCollisionView,
                OnToggleEdit       = OnToggleCollisionEdit,
                OnAddShape         = OnCollisionAddShape,
                OnRemoveShape      = OnCollisionRemoveShape,
                OnCopyToAnimation  = OnCollisionCopyToAnimation,
                OnRemeasure        = OnCollisionRemeasure,
                OnResetFrame       = OnCollisionResetFrame,
                OnShapeSelected    = i => { _collisionShape = Mathf.Max(0, i); RefreshCollisionEditor(); },
                OnGestureSelected  = i => { _collisionGesture = (CollisionGesture)Mathf.Clamp(i, 0, 1); RefreshCollisionEditor(); },
                OnFootprintWidth   = raw => OnFootprintCommitted(raw, width: true),
                OnFootprintDepth   = raw => OnFootprintCommitted(raw, width: false),
                OnHurtScale        = OnHurtScaleCommitted,
            };

        // -- Toggles --------------------------------------------------------------

        private void OnToggleCollisionView()
        {
            _collisionView = !_collisionView;
            if (!_collisionView) DisarmCollisionEditing();
            RefreshCollisionEditor();
        }

        /// <summary>
        /// Arm or disarm shaping. Arming shows the layers, and it disarms the muzzle picker: both
        /// gestures are a drag on the same stage, and one drag writing two things is a drag nobody
        /// can predict.
        /// </summary>
        private void OnToggleCollisionEdit()
        {
            if (CurrentEditableMonster()?.assetConfig == null)
            {
                SetStatus("Selecciona un monstruo del catalogo para editar sus colisiones.");
                return;
            }

            _collisionEditing = !_collisionEditing;
            if (_collisionEditing)
            {
                _collisionView = true;
                DisarmMuzzlePlacement();
                RaiseAnimationPanel();
            }
            ApplyStageProbeState();
            RefreshCollisionEditor();
            SetStatus(_collisionEditing
                ? "Arrastra sobre la vista previa: Mover lleva la capsula, Escalar cambia su tamano. Pausa para afinar un frame."
                : "Edicion de colisiones terminada.");
        }

        private void DisarmCollisionEditing()
        {
            if (!_collisionEditing) return;
            _collisionEditing = false;
            ApplyStageProbeState();
            RefreshCollisionEditor();
        }

        // -- Shape edits ------------------------------------------------------------

        private void OnCollisionAddShape()
        {
            if (!TryBeginFrameEdit(out var def, out var profile, out string frame)) return;
            if (_collisionScratch.Count >= EntityCollisionProfile.MaxShapesPerFrame)
            {
                SetStatus($"Un frame lleva como mucho {EntityCollisionProfile.MaxShapesPerFrame} capsulas.");
                return;
            }
            _collisionScratch.Add(new HurtShape(new Vector2(0f, 0f), new Vector2(0.25f, 0.25f)));
            _collisionShape = _collisionScratch.Count - 1;
            WriteFrame(def, profile, frame, _collisionScratch, handTuned: true, "Capsula anadida");
        }

        private void OnCollisionRemoveShape()
        {
            if (!TryBeginFrameEdit(out var def, out var profile, out string frame)) return;
            if (_collisionScratch.Count <= 1)
            {
                SetStatus("Un cuerpo necesita al menos una capsula. Usa Automatico para volver a la medida.");
                return;
            }
            int index = Mathf.Clamp(_collisionShape, 0, _collisionScratch.Count - 1);
            _collisionScratch.RemoveAt(index);
            _collisionShape = Mathf.Clamp(index - 1, 0, _collisionScratch.Count - 1);
            WriteFrame(def, profile, frame, _collisionScratch, handTuned: true, "Capsula quitada");
        }

        /// <summary>
        /// The frame on stage's capsules copied onto every frame of the animation being watched,
        /// and their mirrors — the unit an author thinks in when a whole swing is wrong the same way.
        /// </summary>
        private void OnCollisionCopyToAnimation()
        {
            if (!TryBeginFrameEdit(out var def, out var profile, out _)) return;
            var frames = _animPreview.CurrentFrames;
            if (frames == null || frames.Length == 0) return;

            int written = 0;
            foreach (var sprite in frames)
            {
                if (sprite == null) continue;
                WriteRowAndMirror(profile, sprite.name, _collisionScratch, handTuned: true);
                written++;
            }
            CommitCollisionEdit(def, $"Colisiones copiadas a {written} frames");
        }

        /// <summary>Re-run the bake's measurement on the frame on stage (Editor only).</summary>
        private void OnCollisionRemeasure()
        {
            var def = CurrentEditableMonster();
            var profile = def?.assetConfig?.collision;
            var sprite = _animPreview.PrimaryRenderer != null ? _animPreview.PrimaryRenderer.sprite : null;
            if (profile == null || sprite == null) return;

#if UNITY_EDITOR
            if (!CastMuzzle.TryReadSuffix(sprite.name, out float sign))
            {
                SetStatus("Este frame no dice que mitad es (_e/_w): se queda con la capsula automatica.");
                return;
            }
            var reader = new SpriteAlphaReader();
            if (!reader.TryRead(sprite, out byte[] alpha, out int w, out int h) ||
                !HurtShapeFitter.TryFit(alpha, w, h, sign > 0f, out var fit))
            {
                SetStatus("No se pudo medir este frame desde su PNG.");
                return;
            }
            WriteRowAndMirror(profile, sprite.name, fit.Shapes, handTuned: false);
            _collisionShape = 0;
            CommitCollisionEdit(def, "Colisiones re-medidas");
#else
            SetStatus("Re-medir lee el PNG y solo existe en el Editor.");
#endif
        }

        /// <summary>Drop the frame's own row (and its mirror): it falls back to the creature's shapes or the automatic capsule.</summary>
        private void OnCollisionResetFrame()
        {
            var def = CurrentEditableMonster();
            var profile = def?.assetConfig?.collision;
            var sprite = _animPreview.PrimaryRenderer != null ? _animPreview.PrimaryRenderer.sprite : null;
            if (profile == null || sprite == null) return;

            WriteRowAndMirror(profile, sprite.name, null, handTuned: false);
            _collisionShape = 0;
            CommitCollisionEdit(def, "Colisiones automaticas");
        }

        /// <summary>
        /// A drag on the stage while shaping. Fires every drag frame, so it rewrites the selected
        /// capsule in place rather than adding one.
        /// </summary>
        private void OnStageCollisionPoint(Vector2 viewport)
        {
            if (!_collisionEditing) return;
            var sr = _animPreview.PrimaryRenderer;
            if (sr == null || sr.sprite == null) return;
            if (!_animPreview.TryViewportToWorld(viewport, out Vector2 world)) return;
            if (!TryBeginFrameEdit(out var def, out var profile, out string frame)) return;

            int index = Mathf.Clamp(_collisionShape, 0, _collisionScratch.Count - 1);
            var shape = _collisionScratch[index];
            float sign = _animPreview.StageFacingSign();

            if (_collisionGesture == CollisionGesture.Move)
            {
                Vector2 c = HurtShapeSpace.WorldToShapeCenter(sr, sign, world);
                shape.center = new Vector2(Mathf.Clamp(c.x, -1.2f, 1.2f), Mathf.Clamp(c.y, -1.2f, 1.2f));
            }
            else
            {
                float scale = Mathf.Clamp(profile.hurtScale, 0.1f, 2f);
                HurtShapeSpace.ToWorld(sr, sign, shape, scale, out Vector2 center, out _);
                Vector2 half = new Vector2(Mathf.Abs(world.x - center.x), Mathf.Abs(world.y - center.y));
                Vector2 size = HurtShapeSpace.WorldSizeToShape(sr, half * 2f / scale);
                shape.size = new Vector2(Mathf.Clamp(size.x, 0.02f, 1.2f), Mathf.Clamp(size.y, 0.02f, 1.2f));
            }

            _collisionScratch[index] = shape;
            WriteFrame(def, profile, frame, _collisionScratch, handTuned: true, "Colisiones");
        }

        // -- Footprint and scale ------------------------------------------------------

        private void OnFootprintCommitted(string raw, bool width)
        {
            var def = CurrentEditableMonster();
            var profile = def?.assetConfig?.collision;
            if (profile == null) { RefreshCollisionEditor(); return; }
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                RefreshCollisionEditor();
                return;
            }

            // Zero on either axis returns the footprint to automatic — which is what an absent
            // footprint already means, and the only way back from a size typed by mistake.
            Vector2 size = profile.HasAuthoredFootprint ? profile.footprintSize : CurrentAutoFootprint();
            if (width) size.x = Mathf.Max(0f, value); else size.y = Mathf.Max(0f, value);
            bool automatic = size.x <= 0.0001f || size.y <= 0.0001f;
            profile.footprintSize = automatic ? Vector2.zero : size;
            profile.footprintHandTuned = !automatic;
            CommitCollisionEdit(def, automatic ? "Pisada automatica" : "Pisada");
        }

        private void OnHurtScaleCommitted(string raw)
        {
            var def = CurrentEditableMonster();
            var profile = def?.assetConfig?.collision;
            if (profile == null || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                RefreshCollisionEditor();
                return;
            }
            profile.hurtScale = Mathf.Clamp(value, 0.5f, 1.25f);
            CommitCollisionEdit(def, "Margen del cuerpo");
        }

        private Vector2 CurrentAutoFootprint()
        {
            var sr = _animPreview.PrimaryRenderer;
            if (sr == null || sr.sprite == null) return new Vector2(0.5f, 0.25f);
            Rect r = HurtShapeSpace.LocalRect(sr.sprite);
            return EntityCollisionProfile.AutoFootprintSize(r.width, r.height);
        }

        // -- Plumbing ------------------------------------------------------------------

        /// <summary>
        /// The editable monster, its profile, the frame on stage, and that frame's CURRENT
        /// capsules loaded into the scratch list — so the first edit of a baked or automatic
        /// frame starts from what the game is using rather than from nothing.
        /// </summary>
        private bool TryBeginFrameEdit(out MonsterDefinition def, out EntityCollisionProfile profile, out string frame)
        {
            def = CurrentEditableMonster();
            profile = null;
            frame = null;
            var sr = _animPreview.PrimaryRenderer;
            if (def?.assetConfig == null || sr == null || sr.sprite == null)
            {
                SetStatus("Selecciona un monstruo del catalogo y un frame para editar sus colisiones.");
                return false;
            }

            def.assetConfig.collision ??= new EntityCollisionProfile();
            profile = def.assetConfig.collision;
            frame = sr.sprite.name;
            profile.ResolveShapes(frame, _collisionScratch);
            return _collisionScratch.Count > 0;
        }

        private void WriteFrame(MonsterDefinition def, EntityCollisionProfile profile, string frame,
                                List<HurtShape> shapes, bool handTuned, string label)
        {
            WriteRowAndMirror(profile, frame, shapes, handTuned);
            CommitCollisionEdit(def, label);
        }

        private static void WriteRowAndMirror(EntityCollisionProfile profile, string frame,
                                              IReadOnlyList<HurtShape> shapes, bool handTuned)
        {
            profile.SetFrameShapes(frame, shapes, handTuned);
            string mirror = MirrorFrameName(frame);
            if (mirror != null) profile.SetFrameShapes(mirror, shapes, handTuned);
        }

        /// <summary>The other half's name — <c>x_cast_e3</c> for <c>x_cast_w3</c> — or null for art with no halves.</summary>
        internal static string MirrorFrameName(string frame)
        {
            if (!CastMuzzle.TryReadSuffix(frame, out float sign)) return null;
            int i = frame.Length - 1;
            while (i >= 0 && char.IsDigit(frame[i])) i--;
            char mirrored = sign > 0f ? 'w' : 'e';
            if (char.IsUpper(frame[i])) mirrored = char.ToUpperInvariant(mirrored);
            return frame.Substring(0, i) + mirrored + frame.Substring(i + 1);
        }

        private void CommitCollisionEdit(MonsterDefinition def, string label)
        {
            CommitDefinitionEdit(def, label);
            EntityColliderRig.NotifyProfilesChanged();
            RefreshCollisionEditor();
        }

        // -- Refresh ---------------------------------------------------------------------

        private void RefreshCollisionEditor()
        {
            var def = CurrentEditableMonster();
            var profile = def?.assetConfig?.collision;
            var sr = _animPreview != null ? _animPreview.PrimaryRenderer : null;

            if (_ui.AnimCollViewTmp != null) _ui.AnimCollViewTmp.text = _collisionView ? "Ocultar" : "Ver";
            if (_ui.AnimCollViewImg != null) _ui.AnimCollViewImg.color = _collisionView ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            if (_ui.AnimCollEditTmp != null) _ui.AnimCollEditTmp.text = _collisionEditing ? "Editando" : "Editar";
            if (_ui.AnimCollEditImg != null) _ui.AnimCollEditImg.color = _collisionEditing ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            if (_ui.AnimCollGestureDd != null) SetDropdownOptions(_ui.AnimCollGestureDd, CollisionGestureNames, (int)_collisionGesture);

            string frame = sr != null && sr.sprite != null ? sr.sprite.name : null;
            _collisionScratch.Clear();
            if (_animPreview != null) _animPreview.ResolveShapesOnStage(_collisionScratch);
            if (_collisionScratch.Count > 0) _collisionShape = Mathf.Clamp(_collisionShape, 0, _collisionScratch.Count - 1);

            if (_ui.AnimCollShapeDd != null)
            {
                _collisionShapeLabels.Clear();
                for (int i = 0; i < _collisionScratch.Count; i++) _collisionShapeLabels.Add("Capsula " + (i + 1));
                if (_collisionShapeLabels.Count == 0) _collisionShapeLabels.Add("(ninguna)");
                SetDropdownOptions(_ui.AnimCollShapeDd, _collisionShapeLabels, Mathf.Max(0, _collisionShape));
            }

            if (_animPreview != null)
                _animPreview.SetCollisionOverlay(_collisionView, _collisionEditing ? _collisionShape : -1);

            if (_ui.AnimCollFootWInput != null)
            {
                Vector2 foot = profile != null && profile.HasAuthoredFootprint ? profile.footprintSize : CurrentAutoFootprint();
                _ui.AnimCollFootWInput.SetTextWithoutNotify(foot.x.ToString("0.00", CultureInfo.InvariantCulture));
                _ui.AnimCollFootDInput.SetTextWithoutNotify(foot.y.ToString("0.00", CultureInfo.InvariantCulture));
                _ui.AnimCollHurtScaleInput.SetTextWithoutNotify(
                    (profile != null ? profile.hurtScale : 1f).ToString("0.00", CultureInfo.InvariantCulture));
            }

            if (_ui.AnimCollReadout == null) return;
            if (def?.assetConfig == null)
            {
                _ui.AnimCollReadout.text = _collisionView
                    ? "Personajes jugables: formas horneadas desde su wave (Valkur > Entities > Bake Collision Shapes)."
                    : "";
                return;
            }

            string source = (profile ?? new EntityCollisionProfile()).SourceFor(frame) switch
            {
                HurtShapeSource.HandTuned => "retocada a mano",
                HurtShapeSource.Baked     => "horneada del PNG",
                HurtShapeSource.Creature  => "de la criatura",
                _                         => "automatica",
            };
            var sb = new System.Text.StringBuilder();
            sb.Append(frame ?? "(sin frame)").Append(": ").Append(_collisionScratch.Count)
              .Append(" capsula(s), ").Append(source).Append('.');
            if (_collisionScratch.Count > 0 && sr != null && sr.sprite != null)
            {
                int i = Mathf.Clamp(_collisionShape, 0, _collisionScratch.Count - 1);
                float scale = profile != null ? Mathf.Clamp(profile.hurtScale, 0.1f, 2f) : 1f;
                HurtShapeSpace.ToWorld(sr, _animPreview.StageFacingSign(), _collisionScratch[i], scale, out _, out Vector2 size);
                sb.AppendLine().Append("Capsula ").Append(i + 1).Append(": ")
                  .Append(size.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(" x ")
                  .Append(size.y.ToString("0.00", CultureInfo.InvariantCulture)).Append(" u");
            }
            _ui.AnimCollReadout.text = sb.ToString();
        }
    }
}
