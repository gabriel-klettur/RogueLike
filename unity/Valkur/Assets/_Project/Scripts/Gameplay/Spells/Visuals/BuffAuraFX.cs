using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The look of a sustained self-buff: a ring orbiting the body on the ground plane, a
    /// soft rim on the silhouette, occasional motes lifting off the shoulders, and an
    /// expiry warning.
    ///
    /// <para>WHY IT IS QUIET. A buff lasts eight to fifteen seconds, and CLAUDE.md's L4
    /// records what happens to an effect made only of continuous motion: after about a
    /// second the eye files it as one texture. The answer for a BURST is a busy event layer;
    /// the answer for a STATE is the opposite — a very low duty cycle, around one mote every
    /// 0.7 s, so the effect stays legible as "something is on me" without competing with the
    /// fight for attention. A buff rig authored at a burst's density is a fifteen-second
    /// distraction.</para>
    ///
    /// <para>WHY IT FOLLOWS RATHER THAN PARENTS. Parenting inherits the entity scale, which
    /// would scale the <c>Light2D</c> radius with it — the same trap that rendered the
    /// vortex's light at 367 world units. <c>WeaponSwapFlashFX</c> follows for the identical
    /// reason and this rig copies its shape.</para>
    ///
    /// <para>WHY THE LAST 1.5 SECONDS ARE DIFFERENT. A buff that simply stops is a buff the
    /// player cannot plan around. The ring's rate climbs and its radius contracts, which is
    /// the one beat that turns a duration into information.</para>
    /// </summary>
    internal sealed class BuffAuraFX : MonoBehaviour
    {
        /// <summary>Seconds of visible warning before the buff ends.</summary>
        private const float WARN_SECONDS = 1.5f;

        /// <summary>Ring revolutions per second at rest, and at the end of the warning.</summary>
        private const float SPIN_CALM = 0.35f;
        private const float SPIN_WARN = 2.4f;

        /// <summary>Seconds between motes. Deliberately long — see the class doc.</summary>
        private const float MOTE_INTERVAL = 0.7f;
        private const int   MOTE_POOL = 6;
        private const float MOTE_LIFE = 1.1f;

        /// <summary>
        /// Summed additive alpha across the whole rig at its brightest. Held well under the
        /// ~3 ceiling CLAUDE.md's L2 records, because a sustained effect sits on screen long
        /// enough for any wash-out to become the character's permanent appearance.
        /// </summary>
        private const float RIM_ALPHA  = 0.30f;
        private const float RING_ALPHA = 0.55f;
        private const float MOTE_ALPHA = 0.70f;

        private const int ORDER_RING = 40;
        private const int ORDER_RIM  = 41;
        private const int ORDER_MOTE = 42;

        private Transform _owner;
        private Vector3 _centerOffset;
        private Vector2 _size;
        private float _duration;
        private float _age;
        private Color _tint;
        private Color _hot;

        private Transform _groundPlane;
        private SpriteRenderer _ring;
        private SpriteRenderer _rim;
        private SpriteRenderer[] _motes;
        private float[] _moteAge;
        private Vector3[] _moteDrift;
        private int _nextMote;
        private float _moteTimer;
        private SpriteTintStack _bodyTint;
        private Component _light;
        private float _spin;

        /// <summary>
        /// Build the rig for <paramref name="spell"/> on <paramref name="owner"/>. Refused
        /// outside Play Mode: the rig destroys itself from Update, which never runs there, so
        /// building one would leave a permanent cluster in the scene rather than a timed
        /// effect. Same guard <c>WeaponSwapFlashFX</c> uses.
        /// </summary>
        public static void Attach(Transform owner, SpellDefinition spell)
        {
            if (owner == null || spell == null || spell.duration <= 0f) return;
            if (!Application.isPlaying) return;

            // One rig per owner: a recast REFRESHES the buff rather than stacking it, so it
            // must refresh the picture too. Two rigs would double every additive layer and
            // make a re-buffed character twice as bright for no authored reason.
            var existing = owner.GetComponentInChildren<BuffAuraFX>();
            if (existing != null) { existing.Restart(spell); return; }

            SpriteRenderer body = ResolveBodyRenderer(owner);
            Vector2 size = body != null && body.sprite != null
                ? (Vector2)body.bounds.size
                : new Vector2(0.9f, 1.6f);
            Vector3 centerOffset = body != null && body.sprite != null
                ? body.bounds.center - owner.position
                : new Vector3(0f, 0.8f, 0f);

            var go = new GameObject("BuffAuraFX");
            go.transform.position = owner.position + centerOffset;

            var fx = go.AddComponent<BuffAuraFX>();
            fx._owner = owner;
            fx._centerOffset = centerOffset;
            fx._size = new Vector2(Mathf.Max(0.3f, size.x), Mathf.Max(0.5f, size.y));
            fx._bodyTint = SpriteTintStack.Attach(owner.gameObject);
            fx.Restart(spell);
            fx.BuildRig();
        }

        private void Restart(SpellDefinition spell)
        {
            _duration = spell.duration;
            _age = 0f;
            ResolveColours(spell);
            if (_ring != null) _ring.color = WithAlpha(_tint, 0f);
        }

        /// <summary>
        /// Colour comes from the spell's own swatch, with the project's three-way sentinel
        /// order (CLAUDE.md L10): opaque white means UNAUTHORED and keeps a neutral pale
        /// gold, a real achromatic value is a deliberate request for the absence of colour,
        /// and anything else is a hue to be honoured. Testing saturation first would catch
        /// white in the grey branch and desaturate eleven correctly-authored spells.
        /// </summary>
        private void ResolveColours(SpellDefinition spell)
        {
            Color swatch = spell.particleColor;
            bool unauthored = KiPalette.IsUnauthored(swatch);

            if (unauthored)
            {
                _tint = new Color(1f, 0.93f, 0.72f);
                _hot  = new Color(1f, 0.98f, 0.88f);
                return;
            }

            Color.RGBToHSV(swatch, out float h, out float sat, out _);
            if (sat < 0.02f)
            {
                // Grey is a request for the ABSENCE of colour. Honour it rather than
                // blending, because RGBToHSV reports hue 0 for an achromatic value and
                // hue 0 is RED — a naive blend lights a grey spell pink.
                _tint = new Color(0.82f, 0.82f, 0.84f);
                _hot  = new Color(0.96f, 0.96f, 0.98f);
                return;
            }

            // Keep the VALUE high on both: on an additive material a dark colour adds
            // almost nothing, so a dim swatch would make the rig disappear rather than
            // darken it.
            _tint = Color.HSVToRGB(h, Mathf.Clamp(sat, 0.35f, 0.85f), 1f);
            _hot  = Color.HSVToRGB(h, Mathf.Clamp(sat * 0.45f, 0.10f, 0.40f), 1f);
        }

        private static SpriteRenderer ResolveBodyRenderer(Transform owner)
        {
            var sr = owner.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;
            foreach (var candidate in owner.GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;
            return null;
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildRig()
        {
            // ONE ground-plane squash parent with the rotation on its CHILD, never a squash
            // per item: a ring squashed on its own axis is foreshortened in length without
            // being turned, and slides across the floor instead of lying on it (L7).
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localPosition = new Vector3(0f, -_size.y * 0.42f, 0f);
            plane.transform.localScale = new Vector3(1f, 0.34f, 1f);
            _groundPlane = plane.transform;

            float ringWorld = _size.x * 1.35f;
            _ring = CreateSprite("Ring", ElementalSprites.Ring, _tint, ORDER_RING, _groundPlane);
            _ring.transform.localScale = Vector3.one * ringWorld;

            _rim = CreateSprite("Rim", ElementalSprites.Glow, _hot, ORDER_RIM, transform);
            _rim.transform.localScale = new Vector3(_size.x * 1.5f, _size.y * 1.25f, 1f);

            _motes = new SpriteRenderer[MOTE_POOL];
            _moteAge = new float[MOTE_POOL];
            _moteDrift = new Vector3[MOTE_POOL];
            for (int i = 0; i < MOTE_POOL; i++)
            {
                _motes[i] = CreateSprite($"Mote{i}", ElementalSprites.Sparkle, _hot, ORDER_MOTE, transform);
                _motes[i].transform.localScale = Vector3.one * 0.22f;
                _moteAge[i] = MOTE_LIFE;   // start spent, so none pop on frame one
            }

            BuildLight();
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color,
                                            int order, Transform parent)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            // Additive: on the alpha material the brightest pixel a glow can produce is its
            // own colour, so a rim meant to read as light on the body cannot.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        private void BuildLight()
        {
            try
            {
                var lightType = ElementalProjectileVisual.GetLight2DType();
                if (lightType == null) return;

                var go = new GameObject("BuffLight");
                go.transform.SetParent(transform, false);
                _light = go.AddComponent(lightType);

                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. The literal 3 is the one
                // every other effect in this folder uses; the wrong value here is what left
                // the whole day/night cycle unlit for months.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _tint);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _size.y * 1.9f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.1f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
            }
            catch { _light = null; }
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        private void Update()
        {
            _age += Time.deltaTime;

            if (_owner != null)
                transform.position = _owner.position + _centerOffset;

            // 0 while the buff is comfortable, ramping to 1 as it runs out.
            float remaining = _duration - _age;
            float warn = remaining >= WARN_SECONDS
                ? 0f
                : 1f - Mathf.Clamp01(remaining / WARN_SECONDS);

            // Fade in over the first fifth of a second so the rig does not pop at full
            // alpha on the frame it is built — the same beat an ignition ramp needs.
            float ignite = Mathf.Clamp01(_age / 0.2f);

            UpdateRing(warn, ignite);
            UpdateRim(warn, ignite);
            UpdateMotes(warn, ignite);
            UpdateBody(ignite, warn);
            UpdateLight(ignite, warn);

            if (_age >= _duration) Destroy(gameObject);
        }

        private void UpdateRing(float warn, float ignite)
        {
            if (_ring == null || _groundPlane == null) return;

            _spin += Mathf.Lerp(SPIN_CALM, SPIN_WARN, warn) * 360f * Time.deltaTime;
            _ring.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);

            // The ring CONTRACTS as it warns. A ring that grew would read as the buff
            // getting stronger, which is the opposite of what is about to happen.
            float shrink = Mathf.Lerp(1f, 0.72f, warn);
            _ring.transform.localScale = Vector3.one * (_size.x * 1.35f * shrink);
            _ring.color = WithAlpha(_tint, RING_ALPHA * ignite * Mathf.Lerp(1f, 0.55f, warn));
        }

        private void UpdateRim(float warn, float ignite)
        {
            if (_rim == null) return;
            // A slow breath, well under the duty ceiling: the rim is the STATE layer and
            // the motes are the event layer, so this one must not compete with them.
            float breath = 0.82f + 0.18f * Mathf.Sin(_age * 2.1f);
            _rim.color = WithAlpha(_hot, RIM_ALPHA * ignite * breath * Mathf.Lerp(1f, 0.4f, warn));
        }

        private void UpdateMotes(float warn, float ignite)
        {
            if (_motes == null) return;

            _moteTimer -= Time.deltaTime;
            if (_moteTimer <= 0f && warn < 0.9f)
            {
                _moteTimer = Mathf.Lerp(MOTE_INTERVAL, MOTE_INTERVAL * 0.45f, warn);
                _moteAge[_nextMote] = 0f;
                var t = _motes[_nextMote].transform;
                t.localPosition = new Vector3(
                    Random.Range(-_size.x * 0.5f, _size.x * 0.5f),
                    Random.Range(-_size.y * 0.15f, _size.y * 0.35f), 0f);
                _moteDrift[_nextMote] = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.5f, 0.9f), 0f);
                _nextMote = (_nextMote + 1) % MOTE_POOL;
            }

            for (int i = 0; i < _motes.Length; i++)
            {
                if (_moteAge[i] >= MOTE_LIFE) { _motes[i].color = WithAlpha(_hot, 0f); continue; }
                _moteAge[i] += Time.deltaTime;
                float k = Mathf.Clamp01(_moteAge[i] / MOTE_LIFE);
                _motes[i].transform.localPosition += _moteDrift[i] * Time.deltaTime;
                // Rise, brighten, then fade. A linear fade reads as a dimmer switch; this
                // reads as something lifting off and going out.
                float a = Mathf.Sin(k * Mathf.PI);
                _motes[i].color = WithAlpha(_hot, MOTE_ALPHA * a * ignite);
                _motes[i].transform.localScale = Vector3.one * (0.22f * (0.6f + 0.4f * a));
            }
        }

        private void UpdateBody(float ignite, float warn)
        {
            if (_bodyTint == null) return;
            // This layer MULTIPLIES, so it is held near white on purpose: driving it hard
            // reads as the character being dimmed rather than as power sitting on them.
            // Same restraint TintLayer.Charge and TintLayer.Root document.
            float k = 0.16f * ignite * Mathf.Lerp(1f, 0.35f, warn);
            _bodyTint.Set(TintLayer.Buff, Color.Lerp(Color.white, _tint, k));
        }

        private void UpdateLight(float ignite, float warn)
        {
            if (_light == null) return;
            float breath = 0.85f + 0.15f * Mathf.Sin(_age * 2.1f);
            ElementalProjectileVisual.GetLight2DIntensityProp()
                ?.SetValue(_light, 0.55f * ignite * breath * Mathf.Lerp(1f, 0.4f, warn));
        }

        /// <summary>
        /// Clearing the tint here rather than in Update is what makes the rig safe on the
        /// five exit paths a persistent effect has (CLAUDE.md L13): its own timer, a zone
        /// change, the caster dying, scene unload, and being replaced by a recast. Only
        /// OnDestroy is on all of them, so a body left tinted by any of the other four
        /// would stay tinted for the rest of the run.
        /// </summary>
        private void OnDestroy()
        {
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Buff);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
