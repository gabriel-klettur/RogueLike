using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The stems, the earth they throw, and the lash — the half of the rig that makes the
    /// spell a ROOT WHIP rather than a green circle.
    /// </summary>
    internal sealed partial class RootWhipFX
    {
        private void BuildStems()
        {
            _stemPivots = new Transform[TENDRILS];
            _stemRenderers = new SpriteRenderer[TENDRILS];
            _stemHeight = new float[TENDRILS];
            _stemLean = new float[TENDRILS];
            _stemSwayHz = new float[TENDRILS];
            _stemSwayPhase = new float[TENDRILS];
            _stemAge = new float[TENDRILS];
            _stemLife = new float[TENDRILS];
            _stemMirror = new float[TENDRILS];
            _stemLash = new float[TENDRILS];
            _stemLashLean = new float[TENDRILS];

            for (int i = 0; i < TENDRILS; i++)
            {
                var sr = MakeSprite("Stem" + i, RootSprites.Tendril,
                    WithAlpha(_palette.Bark, 0f), ORDER_TENDRIL + i,
                    SortingConfig.LAYER_VFX, additive: false);
                _stemPivots[i] = sr.transform;
                _stemRenderers[i] = sr;

                Seed(i);
                // Stagger the opening burst so the field grows in over about a third of a
                // second instead of all fifteen stems appearing on one frame — which reads
                // as a sprite popping on, not as ground breaking.
                _stemAge[i] = -Random.Range(0f, 0.34f);
            }
        }

        /// <summary>
        /// Picks a new spot, height, lean and lifetime for one stem and parks it at zero
        /// height. Called at build time and again every time a stem finishes retracting, so
        /// a five-second field is a patch that keeps churning rather than fifteen sprites
        /// standing still.
        /// </summary>
        private void Seed(int i)
        {
            // Uniform over the DISC, not over the radius: sampling the radius linearly
            // piles every stem into the middle, because a ring at r covers area
            // proportional to r. sqrt is what makes the scatter even.
            float r = _radius * SEED_FRAC * Mathf.Sqrt(Random.value);
            float bearing = Random.Range(0f, Mathf.PI * 2f);
            var local = new Vector3(Mathf.Cos(bearing) * r,
                                    Mathf.Sin(bearing) * r * GROUND_SQUASH, 0f);

            _stemPivots[i].localPosition = local;
            _stemHeight[i] = Random.Range(STEM_HEIGHT_MIN, STEM_HEIGHT_MAX);
            _stemLean[i] = Random.Range(-16f, 16f);
            _stemSwayHz[i] = Random.Range(SWAY_HZ_MIN, SWAY_HZ_MAX);
            _stemSwayPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            _stemLife[i] = Random.Range(LIFE_MIN, LIFE_MAX);
            _stemAge[i] = 0f;
            _stemLash[i] = 0f;
            // The sprite bends one way. Mirroring half the field on X is the difference
            // between a patch of roots and fifteen copies of one root.
            _stemMirror[i] = Random.value < 0.5f ? -1f : 1f;

            // A stem further from the camera draws behind the ones in front of it. The
            // pivot's own Y is the depth cue the whole scene already uses.
            _stemRenderers[i].sortingOrder = ORDER_TENDRIL + TENDRILS
                                             - Mathf.Clamp(Mathf.RoundToInt(
                                                 (local.y / Mathf.Max(0.01f, _radius * GROUND_SQUASH) + 1f)
                                                 * 0.5f * TENDRILS), 0, TENDRILS);
        }

        private void BuildClods()
        {
            _clods = new Transform[CLODS];
            _clodRenderers = new SpriteRenderer[CLODS];
            _clodVelocity = new Vector2[CLODS];
            _clodAge = new float[CLODS];
            _clodLife = new float[CLODS];
            _clodSpin = new float[CLODS];

            for (int i = 0; i < CLODS; i++)
            {
                var sr = MakeSprite("Clod" + i, RootSprites.Clod,
                    WithAlpha(_palette.Soil, 0f), ORDER_CLOD,
                    SortingConfig.LAYER_VFX, additive: false);
                sr.transform.localScale = Vector3.one * Random.Range(0.08f, 0.19f);
                _clods[i] = sr.transform;
                _clodRenderers[i] = sr;
                // Dead until a stem throws it. Life 0 is the "available" marker.
                _clodLife[i] = 0f;
            }
        }

        private void BuildBursts()
        {
            _bursts = new SpriteRenderer[TENDRILS];
            _burstAge = new float[TENDRILS];
            for (int i = 0; i < TENDRILS; i++)
            {
                _bursts[i] = MakeSprite("Burst" + i, RootSprites.Burst,
                    WithAlpha(_palette.Sap, 0f), ORDER_BURST,
                    SortingConfig.LAYER_VFX, additive: true);
                _bursts[i].transform.localScale = Vector3.zero;
                _burstAge[i] = 999f;
            }
        }

        /// <summary>
        /// Throws a handful of earth out of the hole a stem has just opened, and pops an
        /// additive flash at the same spot. Both are one-shot: the clods are recycled from
        /// a fixed pool, so a field that churns for five seconds allocates nothing.
        /// </summary>
        private void EruptAt(Vector3 local, int count)
        {
            for (int n = 0; n < count; n++)
            {
                int slot = -1;
                for (int i = 0; i < CLODS; i++)
                {
                    if (_clodAge[i] >= _clodLife[i]) { slot = i; break; }
                }
                if (slot < 0) return;   // all busy: skip rather than steal a live chip

                _clods[slot].localPosition = local;
                float bearing = Random.Range(0f, Mathf.PI * 2f);
                // Mostly upward. A chip thrown flat slides across the floor and reads as
                // litter; the point is that the ground came UP.
                _clodVelocity[slot] = new Vector2(Mathf.Cos(bearing) * Random.Range(0.5f, 1.5f),
                                                  Random.Range(1.6f, 3.2f));
                _clodSpin[slot] = Random.Range(-420f, 420f);
                _clodAge[slot] = 0f;
                _clodLife[slot] = Random.Range(0.45f, 0.80f);
            }

            int burst = _burstCursor++ % TENDRILS;
            _bursts[burst].transform.localPosition = local;
            _burstAge[burst] = 0f;
        }

        /// <summary>
        /// The WHIP. Called once per damage tick per target by <c>PuddleController</c>: the
        /// stems nearest that target bend towards it and crack.
        ///
        /// <para>Nearest rather than all, and <see cref="LASH_STEMS"/> of them rather than
        /// one: every stem answering at once is the whole field pulsing, which the eye reads
        /// as one object breathing instead of as individual roots striking at somebody. One
        /// alone is lost among fourteen that are only swaying.</para>
        /// </summary>
        /// <param name="worldTarget">Where the thing being hit is standing.</param>
        public void Lash(Vector3 worldTarget)
        {
            if (_stemPivots == null || _root == null) return;

            Vector3 local = _root.InverseTransformPoint(worldTarget);

            for (int picked = 0; picked < LASH_STEMS; picked++)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < TENDRILS; i++)
                {
                    // Only a stem that is actually standing can strike; one still breaking
                    // the surface or already sinking would snap from nothing.
                    if (_stemAge[i] < SPROUT_SECONDS) continue;
                    if (_stemLash[i] > 0f) continue;

                    float d = (_stemPivots[i].localPosition - local).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                if (best < 0) return;

                _stemLash[best] = LASH_SECONDS;

                // Lean TOWARDS the target, in the ground plane. Un-squashing Y first is what
                // makes the angle read correctly: the field is drawn foreshortened, so a
                // bearing taken off the drawn offset points somewhere the target is not.
                Vector3 toTarget = local - _stemPivots[best].localPosition;
                float dx = toTarget.x;
                float dy = toTarget.y / GROUND_SQUASH;
                // A stem is drawn standing up, so leaning it at a target on the ground is a
                // roll about Z away from vertical, capped so it never lies flat.
                float side = Mathf.Sign(dx == 0f ? Random.Range(-1f, 1f) : dx);
                float horizontal = Mathf.Clamp01(Mathf.Abs(dx) / Mathf.Max(0.01f, Mathf.Abs(dx) + Mathf.Abs(dy)));
                _stemLashLean[best] = -side * Mathf.Lerp(22f, 58f, horizontal);

                // The crack is the only EVENT the field has, so it is the only thing that
                // earns a sound. Rate-limited inside RootWhipAudio: four stems answer every
                // damage tick and a field can hold several victims, so one clip per stem
                // would be a dozen overlapping transients reading as static.
                if (picked == 0) RootWhipAudio.PlayLashAt(_root.TransformPoint(_stemPivots[best].localPosition));
            }
        }
    }
}
