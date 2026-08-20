using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The whole presentation of a dash: a push-off at the point left, afterimages that
    /// draw themselves forward along the path, a tapered speed streak, a light that runs
    /// the distance, and a skid at the point arrived at.
    ///
    /// The previous version stamped six ghost sprites along the path in a single frame and
    /// faded them all at the same rate. Nothing about that moves: the streak exists in full
    /// on frame one and simply dims, which reads as a decal rather than as a body passing
    /// through. Motion has to be drawn over time even when the body itself teleports, so
    /// each afterimage here is born on its own beat and ages on its own clock — the streak
    /// takes about an eighth of a second to reach the destination.
    /// </summary>
    internal sealed class DashStreakFX : MonoBehaviour
    {
        private const float DURATION = 0.42f;

        /// <summary>How long the streak takes to draw itself from origin to destination.</summary>
        private const float DRAW_SECONDS = 0.13f;

        /// <summary>How long the speed streak stays visible after it finishes drawing.</summary>
        private const float STREAK_FADE_SECONDS = 0.10f;

        /// <summary>Life of a single afterimage, from its own birth.</summary>
        private const float GHOST_LIFE = 0.20f;

        private const int GHOST_COUNT = 6;
        private const int SKID_SPARK_COUNT = 8;

        private const int ORDER_GROUND = 42;
        private const int ORDER_STREAK_HAZE = 43;
        private const int ORDER_STREAK_BODY = 44;
        private const int ORDER_STREAK_CORE = 45;
        private const int ORDER_GHOST = 46;
        private const int ORDER_SPARK = 51;

        private float _age;
        private float _distance;
        private Vector3 _from;
        private Vector3 _to;

        /// <summary>Caster transform to feet. Zero for a feet-pivot sprite, negative for a centred one.</summary>
        private Vector3 _feetOffset;

        /// <summary>
        /// The body the streak is anchored to. Tracked live rather than aimed at the
        /// planned destination: a dash can be cut short by a wall, and a streak that ends
        /// where the character was *supposed* to arrive detaches from them when it is.
        /// </summary>
        private Transform _caster;
        private Color _tint;
        private Color _hot;

        private SlashLanceMesh[] _streak;
        private SpriteRenderer[] _ghosts;
        private float[] _ghostBirth;
        private SpriteRenderer _pushRing;
        private SpriteRenderer _skidRing;
        private SpriteRenderer _arrivalFlash;
        private Transform[] _sparkTransforms;
        private SpriteRenderer[] _sparkRenderers;
        private Vector2[] _sparkVelocities;
        private Component _light;

        /// <summary>
        /// <paramref name="body"/> is the caster's own renderer; its sprite, flip and scale
        /// are what make an afterimage read as the character rather than as a blue smear.
        /// </summary>
        public static void Spawn(Transform caster, Vector3 from, Vector3 to,
                                 SpriteRenderer body, Color tint)
        {
            if (caster == null) return;
            if ((to - from).magnitude < 0.05f) return;

            Vector3 feetOffset = FeetOffset(caster, body);

            var go = new GameObject("DashStreakFX");
            go.transform.position = from + feetOffset;

            var fx = go.AddComponent<DashStreakFX>();
            fx._caster = caster;
            fx._feetOffset = feetOffset;
            fx._from = from + feetOffset;
            fx._to = to + feetOffset;
            fx._distance = Mathf.Max(0.05f, (fx._to - fx._from).magnitude);
            fx._tint = tint.a > 0.05f ? tint : new Color(0.62f, 0.86f, 1f, 1f);
            fx._hot = Color.Lerp(fx._tint, Color.white, 0.7f);
            fx.AimAtEnd();
            fx.Build(body);
        }

        /// <summary>
        /// Transform-to-feet offset for a caster. The streak is a mark left on the ground,
        /// so it belongs at the feet; on a centred-pivot sprite the transform sits at the
        /// waist, which is where the trail used to start and end — floating half a body
        /// above the floor. Shared with the executor so the dust wake lands on the same
        /// line the streak is drawn on.
        /// </summary>
        public static Vector3 FeetOffset(Transform caster, SpriteRenderer body)
            => caster != null && body != null && body.sprite != null
                ? new Vector3(0f, body.bounds.min.y - caster.position.y, 0f)
                : Vector3.zero;

        /// <summary>
        /// Re-reads where the body actually is and points the whole rig at it. Called every
        /// frame, so the far end of the streak is always the character's feet rather than
        /// the position the dash was aiming for.
        /// </summary>
        private void AimAtEnd()
        {
            if (_caster != null)
            {
                // Rigidbody2D.MovePosition does not take effect until the next physics
                // step, so for the first frame or two the caster is still standing on the
                // origin. Adopting that would collapse the streak to nothing; the planned
                // destination holds until the body has actually left.
                Vector3 live = _caster.position + _feetOffset;
                if ((live - _from).sqrMagnitude >= 0.0025f) _to = live;
            }

            Vector3 delta = _to - _from;
            float distance = delta.magnitude;
            if (distance < 0.05f) return;

            _distance = distance;
            transform.rotation = Quaternion.Euler(
                0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void Build(SpriteRenderer body)
        {
            ElementalSprites.EnsureAll();

            float bodyHeight = body != null && body.sprite != null ? body.bounds.size.y : 1f;
            float bodyWidth = body != null && body.sprite != null ? body.bounds.size.x : 0.7f;

            BuildStreak(bodyHeight);
            BuildGhosts(body);
            BuildGround(bodyWidth);
            BuildSparks(bodyWidth);
            BuildLight(bodyHeight);
        }

        /// <summary>
        /// Three nested spindles along the travel axis. The same mesh the thrust uses: a
        /// dash and a stab are the same silhouette problem — something narrow driven along
        /// one line — so they share the geometry rather than each inventing it.
        /// </summary>
        private void BuildStreak(float bodyHeight)
        {
            // Half-widths are fractions of the travel distance, so they are expressed
            // relative to the body instead of to how far it happened to go.
            float half = Mathf.Max(0.04f, bodyHeight * 0.13f) / _distance;

            _streak = new[]
            {
                new SlashLanceMesh(transform, "StreakHaze", UnlitMeshMaterial.Shared, 24,
                    half, 0.95f, WithAlpha(_tint, 0.09f), ORDER_STREAK_HAZE),
                new SlashLanceMesh(transform, "StreakBody", UnlitMeshMaterial.Shared, 24,
                    half * 0.5f, 0.72f, WithAlpha(_tint, 0.22f), ORDER_STREAK_BODY),
                new SlashLanceMesh(transform, "StreakCore", UnlitMeshMaterial.Shared, 24,
                    half * 0.2f, 0.5f, WithAlpha(_hot, 0.38f), ORDER_STREAK_CORE),
            };
        }

        private void BuildGhosts(SpriteRenderer body)
        {
            _ghosts = new SpriteRenderer[GHOST_COUNT];
            _ghostBirth = new float[GHOST_COUNT];

            Sprite sprite = body != null && body.sprite != null ? body.sprite : ElementalSprites.Glow;

            for (int i = 0; i < GHOST_COUNT; i++)
            {
                float along = (i + 1f) / (GHOST_COUNT + 1f);

                var go = new GameObject("Afterimage_" + i.ToString("00"));
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.position = Vector3.Lerp(_from, _to, along) - _feetOffset;
                // Undo the parent's travel rotation: the character does not tilt because it
                // moved sideways, and a rotated afterimage is the fastest way to break the
                // illusion that it is the same body.
                go.transform.rotation = Quaternion.identity;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = WithAlpha(_tint, 0f);
                sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_GHOST;

                if (body != null)
                {
                    sr.flipX = body.flipX;
                    sr.flipY = body.flipY;
                    go.transform.localScale = body.transform.lossyScale;
                }

                _ghosts[i] = sr;
                _ghostBirth[i] = along * DRAW_SECONDS;
            }
        }

        private void BuildGround(float bodyWidth)
        {
            _pushRing = CreateSprite("PushOff", ElementalSprites.Ring, _tint, ORDER_GROUND);
            _pushRing.transform.position = _from;
            _pushRing.transform.rotation = Quaternion.identity;

            _skidRing = CreateSprite("Skid", ElementalSprites.Ring, _tint, ORDER_GROUND);
            _skidRing.transform.position = _to;
            _skidRing.transform.rotation = Quaternion.identity;

            _arrivalFlash = CreateSprite("ArrivalFlash", ElementalSprites.HotCore, _hot, ORDER_SPARK);
            _arrivalFlash.transform.position = _to;
            _arrivalFlash.transform.rotation = Quaternion.identity;
            _arrivalFlash.transform.localScale = Vector3.one * bodyWidth * 0.7f;
        }

        private void BuildSparks(float bodyWidth)
        {
            _sparkTransforms = new Transform[SKID_SPARK_COUNT];
            _sparkRenderers = new SpriteRenderer[SKID_SPARK_COUNT];
            _sparkVelocities = new Vector2[SKID_SPARK_COUNT];

            for (int i = 0; i < SKID_SPARK_COUNT; i++)
            {
                var sr = CreateSprite("SkidSpark_" + i.ToString("00"), ElementalSprites.Sparkle,
                    Color.Lerp(_tint, Color.white, Random.Range(0.2f, 0.9f)), ORDER_SPARK);
                sr.transform.position = _to;
                float size = Random.Range(0.045f, 0.11f);
                sr.transform.localScale = new Vector3(size * Random.Range(2.4f, 4.5f), size, 1f);

                // Thrown backwards along the travel axis: the debris keeps going after the
                // body has stopped, which is what a skid looks like.
                float spread = Random.Range(-55f, 55f) * Mathf.Deg2Rad;
                float speed = Random.Range(1.8f, 5.2f);
                _sparkVelocities[i] = new Vector2(-Mathf.Cos(spread), Mathf.Sin(spread)) * speed;
                _sparkTransforms[i] = sr.transform;
                _sparkRenderers[i] = sr;
                _ = bodyWidth;
            }
        }

        private void BuildLight(float bodyHeight)
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var go = new GameObject("DashLight");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = _from;
            try
            {
                _light = go.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 2));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _tint);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, bodyHeight * 2.4f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, worldPositionStays: false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        private void Update()
        {
            _age += Time.deltaTime;
            AimAtEnd();
            AnchorToPath();

            float draw01 = Mathf.Clamp01(_age / DRAW_SECONDS);
            float eased = draw01 * draw01 * (3f - 2f * draw01);
            float linger = _age <= DRAW_SECONDS
                ? 1f
                : 1f - Mathf.Clamp01((_age - DRAW_SECONDS) / (DURATION - DRAW_SECONDS));

            UpdateStreak(eased, linger);
            UpdateGhosts();
            UpdateGround(draw01, linger);
            UpdateSparks(draw01);
            UpdateLight(eased, linger);

            if (_age >= DURATION) Destroy(gameObject);
        }

        /// <summary>
        /// Keeps everything that is pinned to an end of the dash on that end, after the
        /// live re-aim has possibly moved it.
        /// </summary>
        private void AnchorToPath()
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                float along = (i + 1f) / (GHOST_COUNT + 1f);
                _ghosts[i].transform.position = Vector3.Lerp(_from, _to, along) - _feetOffset;
            }

            _skidRing.transform.position = _to;
            _arrivalFlash.transform.position = _to;

            // The sparks are parked on the destination until the body gets there; once it
            // has, they are on their own and must not be dragged around any more.
            if (_age >= DRAW_SECONDS) return;
            for (int i = 0; i < _sparkTransforms.Length; i++)
                _sparkTransforms[i].position = _to;
        }

        private void UpdateStreak(float eased, float linger)
        {
            // The streak runs on its own, much shorter clock than the rest of the effect.
            // Held for the full duration it stops reading as displaced air and starts
            // reading as paint on the floor.
            float streak = 1f - Mathf.Clamp01((_age - DRAW_SECONDS) / STREAK_FADE_SECONDS);
            streak = Mathf.Pow(Mathf.Max(0f, streak), 1.7f);

            // Once the body has landed the streak is pulled in behind it rather than left
            // fading in mid-air, so the effect ends where the player is looking.
            float contract = 1f - linger;
            for (int i = 0; i < _streak.Length; i++)
                _streak[i].Draw(eased, streak, _distance, 1f, contract);
        }

        private void UpdateGhosts()
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                float own = _age - _ghostBirth[i];
                float alpha = own <= 0f
                    ? 0f
                    : Mathf.Clamp01(own / 0.025f) *
                      Mathf.Pow(1f - Mathf.Clamp01(own / GHOST_LIFE), 1.6f);
                _ghosts[i].color = WithAlpha(Color.Lerp(_tint, Color.white, 0.3f), alpha * 0.34f);
            }
        }

        private void UpdateGround(float draw01, float linger)
        {
            // The push-off blooms at the instant of departure and is gone almost at once.
            float push = 1f - Mathf.Clamp01(_age / (DRAW_SECONDS * 1.6f));
            float pushScale = Mathf.Lerp(0.3f, 1.9f, 1f - push);
            _pushRing.transform.localScale = new Vector3(pushScale, pushScale * 0.45f, 1f);
            _pushRing.color = WithAlpha(_tint, push * push * 0.26f);

            // The skid only exists once the body is actually there.
            float skid = draw01 * linger;
            float skidScale = Mathf.Lerp(0.25f, 2.2f, draw01);
            _skidRing.transform.localScale = new Vector3(skidScale, skidScale * 0.45f, 1f);
            _skidRing.color = WithAlpha(_tint, skid * 0.24f);

            float flash = Mathf.Clamp01(draw01 * 3f - 2f) * linger;
            _arrivalFlash.color = WithAlpha(_hot, flash * flash * 0.55f);
        }

        private void UpdateSparks(float draw01)
        {
            if (draw01 < 1f) return;

            float dt = Time.deltaTime;
            float fade = Mathf.Clamp01((DURATION - _age) / (DURATION - DRAW_SECONDS));
            for (int i = 0; i < _sparkTransforms.Length; i++)
            {
                _sparkTransforms[i].position += (Vector3)(_sparkVelocities[i] * dt);
                _sparkVelocities[i] *= Mathf.Pow(0.02f, dt);
                _sparkRenderers[i].color =
                    WithAlpha(_sparkRenderers[i].color, fade * fade);
            }
        }

        private void UpdateLight(float eased, float linger)
        {
            if (_light == null) return;
            _light.transform.position = Vector3.Lerp(_from, _to, eased);
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.6f * linger);
            }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }

        private void OnDestroy()
        {
            if (_streak == null) return;
            for (int i = 0; i < _streak.Length; i++) _streak[i]?.Dispose();
            _streak = null;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
