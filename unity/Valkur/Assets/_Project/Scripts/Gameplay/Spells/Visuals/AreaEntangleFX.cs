using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>Timings the snare shares between the profile that sizes it and the rig that runs it.</summary>
    internal static class AreaBurstTiming
    {
        /// <summary>
        /// How long a stem takes to let go. The release is not decoration: <c>entangle</c>
        /// authors <c>damage: 0</c>, so this rig is the ONLY feedback anyone was held, and a
        /// root that vanishes cleanly gives the player no frame in which to notice they can
        /// move again.
        /// </summary>
        internal const float WitherSeconds = 0.30f;
    }

    /// <summary>
    /// The snare. A patch of ground that has come alive, plus stems closed around the ankles
    /// of everything it caught — and the wither that tells the player it has let go.
    ///
    /// <para>IT REUSES <see cref="RootWhipFX"/> FOR THE PATCH. That rig already solves the hard
    /// half — cracked earth, a ground ring pinned to the damage radius, barbed stems that break
    /// the surface, sway and sink — and it was reachable from exactly one caller
    /// (<c>PuddleExecutor</c>). Writing a second field here would be two implementations of one
    /// idea, drifting apart on the first retune.</para>
    ///
    /// <para>THE GRIPS ARE NOT PART OF THAT PATCH, and that is the point. The patch says where
    /// the spell reaches; the grips say WHO it caught, which is the only information a
    /// zero-damage control spell produces. Scattering fifteen more stems over the same circle
    /// would say the first thing twice and the second thing not at all.</para>
    /// </summary>
    internal sealed class AreaEntangleFX : MonoBehaviour
    {
        /// <summary>Bounds on the per-target stem count, which the profile chooses. Two reads
        /// as a grip; one reads as a stalk that happens to be standing there, and six hides
        /// the creature the player is aiming at.</summary>
        private const int STEMS_MIN = 2;
        private const int STEMS_MAX = 6;

        private const float GROW_SECONDS = 0.18f;
        private const float STEM_HEIGHT_MIN = 0.55f;
        private const float STEM_HEIGHT_MAX = 0.95f;

        /// <summary>Seconds the whole patch takes to sink once the spell is over.</summary>
        private const float PATCH_FADE = 0.45f;

        private const int ORDER_GRIP = 70;

        /// <summary>Dead plant. Deliberately not a lerp to black: a rotting root goes BROWN,
        /// and fading to dark reads as the light going out rather than as the plant dying.</summary>
        private static readonly Color Withered = new Color(0.34f, 0.24f, 0.12f, 1f);

        private RootWhipFX _patch;
        private AreaBurstProfile _profile;
        private RootPalette _soil;
        private float _age;

        private readonly List<Grip> _grips = new List<Grip>();

        internal static void Play(Vector3 center, AreaBurstProfile profile, List<GameObject> caught)
        {
            ElementalSprites.EnsureAll();
            RootSprites.EnsureAll();

            var go = new GameObject("AreaSnare");
            go.transform.position = center;
            go.AddComponent<AreaEntangleFX>().Begin(profile, caught);
        }

        private void Begin(AreaBurstProfile profile, List<GameObject> caught)
        {
            _profile = profile;
            _soil = RootPalette.From(profile.Swatch);
            _patch = RootWhipFX.Attach(transform, profile.Radius, profile.Swatch);

            if (caught == null) return;
            for (int i = 0; i < caught.Count; i++)
            {
                if (caught[i] == null) continue;
                _grips.Add(BuildGrip(caught[i]));
                // The patch's own stems bend towards whoever was seized, which is the one
                // event a field made entirely of continuous motion otherwise never has.
                _patch.Lash(caught[i].transform.position);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;

            float remaining = _profile.Life - _age;
            _patch.Tick(dt, Mathf.Clamp01(remaining / PATCH_FADE));

            for (int i = 0; i < _grips.Count; i++)
                TickGrip(_grips[i], dt);

            if (_age >= _profile.Life) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // A persistent effect has five exit paths and only OnDestroy is on all of them —
            // its own timer, eviction, a zone change, the caster dying, scene unload. The
            // patch owns a Light2D, so releasing it anywhere else leaks a light per cast.
            _patch?.Destroy();
        }

        /// <summary>
        /// Three or four stems standing at the target's feet and leaning IN. The entity pivot
        /// is at the feet in this project, which is why they can be placed from the transform
        /// alone — measuring the sprite instead would put the grip at the waist of anything
        /// tall.
        /// </summary>
        private Grip BuildGrip(GameObject target)
        {
            var grip = new Grip
            {
                Target = target.transform,
                Status = target.GetComponentInParent<StatusEffectManager>(),
                LastKnown = target.transform.position,
            };

            // Off the profile, plus or minus one, so the grips are not all identical and the
            // number is still a fact the profile owns rather than a constant hidden in here.
            int count = Mathf.Clamp(_profile.EventCount + Random.Range(-1, 1),
                                    STEMS_MIN, STEMS_MAX);
            grip.Stems = new Transform[count];
            grip.Renderers = new SpriteRenderer[count];
            grip.BaseColors = new Color[count];
            grip.Lean = new float[count];
            grip.Height = new float[count];
            grip.Phase = new float[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Grip" + i);
                go.transform.SetParent(transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = RootSprites.Tendril;
                sr.color = Color.Lerp(_soil.Bark, _soil.Leaf, Random.Range(0f, 0.45f));
                // Opaque, like every other stem in this rig: a root is matter. On the additive
                // material a dark green would add almost nothing and the grip would vanish
                // with nothing failing.
                sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_GRIP + i;

                grip.Stems[i] = go.transform;
                grip.Renderers[i] = sr;
                // Per stem, not one shared base: a single cached colour would wither every
                // stem of a grip from whichever one happened to be built last.
                grip.BaseColors[i] = sr.color;
                // Splayed around the feet and leaning back over them, so the stems close on
                // the ankles rather than standing beside them.
                grip.Lean[i] = Mathf.Lerp(-38f, 38f, count > 1 ? i / (count - 1f) : 0.5f)
                             + Random.Range(-8f, 8f);
                grip.Height[i] = Random.Range(STEM_HEIGHT_MIN, STEM_HEIGHT_MAX);
                grip.Phase[i] = Random.Range(0f, Mathf.PI * 2f);
            }

            return grip;
        }

        private void TickGrip(Grip grip, float dt)
        {
            if (grip.Dropped) return;
            grip.Age += dt;

            if (grip.Target != null) grip.LastKnown = grip.Target.position;

            // Held, or let go? Three ways to be released and they must all reach the wither:
            // the root expiring, the bearer dying, and the bearer being despawned outright.
            bool held = grip.Target != null
                     && (grip.Status == null ? grip.Age < _profile.Life - AreaBurstTiming.WitherSeconds
                                             : grip.Status.IsRooted);
            if (!held) grip.Wither += dt;

            float wither = Mathf.Clamp01(grip.Wither / AreaBurstTiming.WitherSeconds);
            float grow = Mathf.Clamp01(grip.Age / GROW_SECONDS);

            for (int i = 0; i < grip.Stems.Length; i++)
            {
                float sway = Mathf.Sin(Time.time * 1.6f + grip.Phase[i]) * 3.5f;
                // Slack: a dying stem loses its grip before it loses its colour, so the lean
                // opens out and the height drops before the alpha does anything.
                float lean = Mathf.Lerp(grip.Lean[i], grip.Lean[i] * 2.1f + 22f, wither) + sway;
                float height = grip.Height[i] * grow * Mathf.Lerp(1f, 0.55f, wither);

                grip.Stems[i].position = grip.LastKnown;
                grip.Stems[i].localScale = new Vector3(
                    RootSprites.TendrilWorldWidth * (i % 2 == 0 ? 1f : -1f), height, 1f);
                grip.Stems[i].localRotation = Quaternion.Euler(0f, 0f, lean);

                Color c = Color.Lerp(grip.BaseColors[i], Withered, wither);
                grip.Renderers[i].color = new Color(c.r, c.g, c.b, 1f - wither * wither);
            }

            // A scrap of dry matter as the grip finally lets go, so the release leaves
            // something behind instead of the stems simply not being there any more. This is
            // the last frame of the only feedback a zero-damage spell produces.
            if (!grip.Dropped && wither >= 1f)
            {
                grip.Dropped = true;
                for (int i = 0; i < grip.Stems.Length; i++)
                {
                    AreaBurstPieces.WitheredScrap(grip.Stems[i].position, Withered, ORDER_GRIP);
                    grip.Stems[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>One caught target and the stems holding it.</summary>
        private sealed class Grip
        {
            public Transform Target;
            public StatusEffectManager Status;
            public Vector3 LastKnown;
            public Transform[] Stems;
            public SpriteRenderer[] Renderers;
            public Color[] BaseColors;
            public float[] Lean;
            public float[] Height;
            public float[] Phase;
            public float Age;
            public float Wither;
            public bool Dropped;
        }
    }
}
