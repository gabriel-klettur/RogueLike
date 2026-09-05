using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// <b>Shell</b> — armour of ice standing off the silhouette.
    ///
    /// <para>THE CHARACTER IS INSIDE IT, and that is the only statement in the rig that makes a
    /// shell a shell. Eight plates ride a slowly turning ring; each frame the ones on the near
    /// side sort in FRONT of the caster and the ones on the far side BEHIND, so the body is
    /// enclosed rather than standing behind a decal. <c>ShieldSphereFX</c> exists because four
    /// concentric discs on <c>LAYER_VFX</c> can never say this — they all draw in front, and
    /// the result reads as a sticker on the lens.</para>
    ///
    /// <para>THE BASE ORDER MUST BE RE-READ, NOT CAPTURED. <c>YSortEntity</c> rewrites the
    /// caster's sorting order whenever they walk, so a value taken once at build time pops the
    /// far half of the shell in front the first time the character takes a step — and it stays
    /// wrong for as long as they keep moving, which is most of a fight.</para>
    ///
    /// <para>THE PLATES ARE MATTER. Their bodies are on the UNLIT material and are the rig's
    /// one non-additive layer: ice armour is a thing, not a light. Only the thin frost edge is
    /// additive, and only eight of them exist, spread around the body so at most two or three
    /// ever overlap — which is what keeps the summed additive alpha under the ceiling while the
    /// plates still read as ice rather than as grey paper.</para>
    /// </summary>
    internal sealed partial class BuffAuraFX
    {
        /// <summary>Degrees per second the whole shell turns. Slow: armour is heavy.</summary>
        private const float SHELL_SPIN = 26f;

        /// <summary>Alpha of a plate's opaque body, near side and far side.</summary>
        private const float PLATE_BODY_FRONT = 0.62f;
        private const float PLATE_BODY_BACK = 0.30f;

        /// <summary>Alpha of a plate's additive frost edge. Deliberately small — see the class doc.</summary>
        private const float PLATE_EDGE_FRONT = 0.26f;
        private const float PLATE_EDGE_BACK = 0.10f;

        private SpriteRenderer[] _plateBodies;
        private SpriteRenderer[] _plateEdges;
        private Transform[] _plateRoots;
        private float[] _plateHeight;
        private float _shellSpin;
        private int _shellBaseOrder;

        private void ClearShellState()
        {
            _plateBodies = null;
            _plateEdges = null;
            _plateRoots = null;
            _plateHeight = null;
            _shellSpin = 0f;
            _shellBaseOrder = 0;
        }

        private void BuildShell()
        {
            int n = Mathf.Max(3, _profile.PieceCount);
            _plateRoots = new Transform[n];
            _plateBodies = new SpriteRenderer[n];
            _plateEdges = new SpriteRenderer[n];
            _plateHeight = new float[n];

            // Ice reads as matter when it is a shade DOWN from its own light. The body takes
            // the palette's halo (the dim end) and the edge takes the hot core, which is the
            // same top-to-bottom split that stops a flame's flanks washing out to cream.
            Color body = _profile.Palette.halo;
            Color edge = _profile.Palette.hotCore;

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Plate" + i);
                go.transform.SetParent(_root, false);
                _plateRoots[i] = go.transform;

                // Spread the plates up and down the silhouette as well as around it, or eight
                // plates on one horizontal band armour a character's waist and nothing else.
                _plateHeight[i] = Mathf.Lerp(-0.42f, 0.42f, (i * 5 % n) / (n - 1f));

                _plateBodies[i] = MakeSprite(_plateRoots[i], "Body", ShieldSprites.FacetSolid,
                    body, SortingConfig.LAYER_ENTITIES, ORDER_INFRONT_CASTER, additive: false);
                _plateEdges[i] = MakeSprite(_plateRoots[i], "Edge", ShieldSprites.Facet,
                    edge, SortingConfig.LAYER_ENTITIES, ORDER_INFRONT_CASTER, additive: true);
            }
        }

        /// <summary>
        /// Stores the caster's live order. The per-plate front/back decision is NOT made here,
        /// because it changes every frame as the shell turns — this only moves the whole stack
        /// up or down with the character.
        /// </summary>
        private void RebaseShellOrders(int casterOrder)
        {
            if (_profile.Silhouette != BuffSilhouette.Shell) return;
            _shellBaseOrder = casterOrder;
        }

        private void TickShell(float onset, float warn)
        {
            if (_plateRoots == null) return;

            _shellSpin += SHELL_SPIN * Mathf.Deg2Rad * Time.deltaTime;

            // The plates ASSEMBLE: they start wide and settle onto the body over the onset.
            // A shell that is simply there on frame one has appeared, not been raised.
            float standOff = _profile.StandOff * Mathf.Lerp(1.9f, 1f, EaseOutCubic(onset));
            float rx = _size.x * 0.5f * standOff;
            float ry = _size.y * 0.5f * standOff;

            // The warning is a FLICKER whose rate climbs, which is what a failing solid does.
            // A fade would say the armour is getting thinner; this says it is about to break.
            float flickerRate = Mathf.Lerp(4f, 22f, warn);
            float blink = Mathf.Sin(_age * flickerRate) > 0f ? 1f : 0.28f;
            float flicker = Mathf.Lerp(1f, blink, warn);

            int n = _plateRoots.Length;
            for (int i = 0; i < n; i++)
            {
                float a = _shellSpin + i * Mathf.PI * 2f / n;
                float x = Mathf.Cos(a);
                float depth = Mathf.Sin(a);
                bool inFront = depth > 0f;

                _plateRoots[i].localPosition = new Vector3(x * rx, _plateHeight[i] * ry * 2f, 0f);
                // A plate at the silhouette EDGE is seen edge-on and must be narrow; one
                // facing the camera is seen flat and full width. Without this the shell is a
                // ring of identical hexagons and reads as flat however it is sorted.
                float foreshorten = Mathf.Lerp(0.34f, 1f, Mathf.Abs(depth));
                float w = _size.x * 0.46f * foreshorten;
                float h = _size.y * 0.30f;
                _plateRoots[i].localScale = new Vector3(w, h, 1f);
                _plateRoots[i].localRotation = Quaternion.Euler(0f, 0f, -x * 16f);

                int order = _shellBaseOrder + (inFront ? ORDER_INFRONT_CASTER : ORDER_BEHIND_CASTER);
                _plateBodies[i].sortingOrder = order;
                _plateEdges[i].sortingOrder = order + 1;

                float depthFade = inFront ? 1f : 0.42f;
                float k = onset * flicker;
                _plateBodies[i].color = WithAlpha(_profile.Palette.halo,
                    (inFront ? PLATE_BODY_FRONT : PLATE_BODY_BACK) * k);
                _plateEdges[i].color = WithAlpha(_profile.Palette.hotCore,
                    (inFront ? PLATE_EDGE_FRONT : PLATE_EDGE_BACK) * depthFade * k);
            }
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - Mathf.Clamp01(t);
            return 1f - u * u * u;
        }
    }
}
