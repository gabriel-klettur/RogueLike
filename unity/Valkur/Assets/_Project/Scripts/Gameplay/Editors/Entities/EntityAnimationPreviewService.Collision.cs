using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The collision layers drawn over the stage: the footprint at the feet and the hurtbox
    /// capsules on the frame being shown.
    ///
    /// <para>Drawn from the PROFILE through <see cref="HurtShapeSpace"/>, the same conversion the
    /// live <see cref="EntityColliderRig"/> uses, and never from a collider — the preview rigs
    /// carry none, on purpose (they would sit in the physics world on the preview layer). One
    /// conversion for both is what makes the stage a promise about the game rather than a
    /// second opinion.</para>
    /// </summary>
    public sealed partial class EntityAnimationPreviewService
    {
        private const float COLLISION_LINE_WIDTH = 0.035f;

        private readonly List<LineRenderer> _collisionLines = new List<LineRenderer>();
        private readonly List<Vector2> _collisionPoints = new List<Vector2>(64);
        private readonly List<HurtShape> _collisionShapes = new List<HurtShape>(EntityCollisionProfile.MaxShapesPerFrame);
        private GameObject _collisionRoot;
        private Material _collisionMaterial;
        private bool _collisionVisible;
        private int _collisionSelected = -1;

        /// <summary>Show or hide the collision layers, and which capsule is selected (-1 = none).</summary>
        public void SetCollisionOverlay(bool visible, int selectedShape)
        {
            _collisionVisible = visible;
            _collisionSelected = selectedShape;
            UpdateCollisionOverlay();
        }

        /// <summary>
        /// The capsules that answer for the frame on screen, in the order the game resolves them.
        /// </summary>
        public void ResolveShapesOnStage(List<HurtShape> result)
        {
            result.Clear();
            var sr = PrimaryRenderer;
            if (_config == null || sr == null || sr.sprite == null) return;
            (_config.collision ?? new EntityCollisionProfile()).ResolveShapes(sr.sprite.name, result);
        }

        /// <summary>A viewport point on the stage as a WORLD point.</summary>
        public bool TryViewportToWorld(Vector2 viewport, out Vector2 world)
        {
            world = Vector2.zero;
            if (_camera == null) return false;
            world = _camera.ViewportToWorldPoint(
                new Vector3(viewport.x, viewport.y, Mathf.Abs(_camera.transform.position.z)));
            return true;
        }

        /// <summary>The drawn facing sign of the frame on stage.</summary>
        public float StageFacingSign()
            => HurtShapeSpace.FacingSign(PrimaryRenderer, Animator);

        private void UpdateCollisionOverlay()
        {
            var sr = PrimaryRenderer;
            bool show = _collisionVisible && _config != null && sr != null && sr.sprite != null && _stageRoot != null;
            if (!show)
            {
                for (int i = 0; i < _collisionLines.Count; i++)
                    if (_collisionLines[i] != null) _collisionLines[i].enabled = false;
                return;
            }

            EnsureCollisionRoot();
            var profile = _config.collision ?? new EntityCollisionProfile();
            float sign = StageFacingSign();
            float scale = Mathf.Clamp(profile.hurtScale, 0.1f, 2f);

            profile.ResolveShapes(sr.sprite.name, _collisionShapes);
            int used = 0;
            for (int i = 0; i < _collisionShapes.Count; i++)
            {
                HurtShapeSpace.ToWorld(sr, sign, _collisionShapes[i], scale, out Vector2 c, out Vector2 s);
                _collisionPoints.Clear();
                HurtShapeSpace.AppendCapsuleLoop(c, s, _collisionPoints);
                bool selected = i == _collisionSelected;
                DrawCollisionLoop(used++, selected ? UITheme.MARKER_RING : UITheme.COLLISION_HURTBOX,
                                  selected ? COLLISION_LINE_WIDTH * 1.8f : COLLISION_LINE_WIDTH);
            }

            // The footprint: fixed, at the feet. Sized the way EntitySetup sizes it.
            Vector2 foot = profile.HasAuthoredFootprint
                ? Vector2.Scale(profile.footprintSize, Abs(sr.transform.lossyScale))
                : FootprintOnStage(sr);
            Vector2 footCenter = (Vector2)sr.transform.position +
                                 Vector2.Scale(profile.footprintOffset, Abs(sr.transform.lossyScale));
            _collisionPoints.Clear();
            HurtShapeSpace.AppendCapsuleLoop(footCenter, foot, _collisionPoints);
            DrawCollisionLoop(used++, UITheme.COLLISION_FOOTPRINT, COLLISION_LINE_WIDTH);

            for (int i = used; i < _collisionLines.Count; i++)
                if (_collisionLines[i] != null) _collisionLines[i].enabled = false;
        }

        private static Vector2 FootprintOnStage(SpriteRenderer sr)
        {
            Rect r = HurtShapeSpace.LocalRect(sr.sprite);
            Vector3 s = sr.transform.lossyScale;
            return EntityCollisionProfile.AutoFootprintSize(r.width * Mathf.Abs(s.x), r.height * Mathf.Abs(s.y));
        }

        private static Vector2 Abs(Vector3 v) => new Vector2(Mathf.Abs(v.x), Mathf.Abs(v.y));

        private void DrawCollisionLoop(int index, Color color, float width)
        {
            while (_collisionLines.Count <= index) _collisionLines.Add(null);
            var line = _collisionLines[index];
            if (line == null)
            {
                var go = new GameObject("CollisionLine" + index);
                go.transform.SetParent(_collisionRoot.transform, false);
                go.layer = _collisionRoot.layer;
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.sharedMaterial = _collisionMaterial;
                line.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                // Above the body and just under the muzzle crosshair.
                line.sortingOrder = 55;
                line.numCapVertices = 0;
                _collisionLines[index] = line;
            }

            line.enabled = true;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.positionCount = _collisionPoints.Count;
            for (int i = 0; i < _collisionPoints.Count; i++)
                line.SetPosition(i, new Vector3(_collisionPoints[i].x, _collisionPoints[i].y, 0f));
        }

        private void EnsureCollisionRoot()
        {
            if (_collisionMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                          ?? Shader.Find("Sprites/Default");
                _collisionMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (_collisionRoot != null) return;
            _collisionLines.Clear();
            _collisionRoot = new GameObject("CollisionOverlay");
            // Under the STAGE, not a rig: rigs are rebuilt on every state switch, and the lines
            // are world-space anyway.
            _collisionRoot.transform.SetParent(_stageRoot.transform, false);
            _collisionRoot.layer = _stageRoot.layer;
        }

        private void ShutdownCollisionOverlay()
        {
            _collisionLines.Clear();
            _collisionRoot = null;   // destroyed with the stage root
            if (_collisionMaterial != null) { SafeDestroy.Of(_collisionMaterial); _collisionMaterial = null; }
        }
    }
}
