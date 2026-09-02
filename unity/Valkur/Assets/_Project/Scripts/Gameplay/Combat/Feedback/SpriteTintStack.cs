using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Which system a tint contribution belongs to. One slot each, so two systems can
    /// never overwrite one another and a system leaving only removes its own colour.
    /// </summary>
    public enum TintLayer
    {
        Burn = 0,
        Poison = 1,
        Freeze = 2,
        Slow = 3,
        Stun = 4,
        Death = 5,
        Spirit = 6,
        Teleport = 7,
        /// <summary>
        /// Standing inside an arcane hazard. Held for as long as the entity is in the
        /// zone rather than pulsed per tick — a 0.14 s hit flash covers a fifth of a
        /// 0.6 s beat, so without this a monster in the fire looks identical to one
        /// outside it most of the time.
        /// </summary>
        Arcane = 8,

        /// <summary>
        /// The body catching the light of a weapon being drawn or stowed. A short punch, not
        /// a state: <c>WeaponSwapFlashFX</c> owns it for the length of its own cycle and
        /// clears it in <c>OnDestroy</c>, so a swap interrupted by a zone change or a death
        /// cannot leave the character glowing.
        /// </summary>
        Equip = 9,

        /// <summary>
        /// The body catching the light of the spell it is casting. Owned by
        /// <c>SpellCastFlourishFX</c> for the length of one flourish and cleared in its
        /// <c>OnDestroy</c>, so a cast interrupted by a zone change or a death cannot leave
        /// the character glowing. Multiplies with <see cref="Equip"/> rather than fighting
        /// it: a spell cast during a weapon swap reads as both.
        /// </summary>
        Cast = 10,

        /// <summary>
        /// The body lit by a sustained energy charge. Unlike <see cref="Cast"/> this is a
        /// STATE, held for as long as the aura burns, so it is deliberately gentle: the aura
        /// itself is additive and blows out on its own, while this layer MULTIPLIES and would
        /// darken the character toward the aura's colour if it were driven hard.
        /// </summary>
        Charge = 11,
    }

    /// <summary>
    /// The single owner of an entity body sprite's <c>color</c>.
    ///
    /// Nine systems used to write that one field, and every one of them did it the same
    /// wrong way: cache <c>sr.color</c> as "the original", tint for a while, then write the
    /// cache back. That is correct exactly when no other system is running. When two
    /// overlap, whichever starts second captures the FIRST one's tint as its baseline, and
    /// whichever finishes last restores it — so a monster that got hit while burning stayed
    /// orange after the burn ended, permanently, with nothing in the scene still tinting it.
    ///
    /// Here each system owns a <see cref="TintLayer"/> and never touches the renderer. The
    /// stack keeps the pristine colour, composes the active layers and writes the result.
    /// Layers multiply, so overlapping effects blend instead of fighting:
    /// burning and poisoned reads as both, and removing either leaves the other intact.
    ///
    /// Layer colours are expressed <em>as if the base were white</em> — the colour you want
    /// on an untinted sprite. For the white sprites this project ships that is exact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpriteTintStack : MonoBehaviour
    {
        // Derived from the enum rather than typed out, because the literal it replaces was a
        // hand-maintained duplicate of a fact the compiler already knows — and the failure
        // mode was an IndexOutOfRange on the FIRST Set of whatever new layer forgot to bump
        // it, i.e. in the new effect rather than here. Adding TintLayer.Equip walked straight
        // into it. Still a fixed width per instance: the mask below is an int, so 32 layers
        // is the real ceiling and SpriteTintStackTests guards it.
        private static readonly int LAYER_COUNT = System.Enum.GetValues(typeof(TintLayer)).Length;

        private readonly Color[] _layers = new Color[LAYER_COUNT];
        private int _activeMask;

        private SpriteRenderer _sr;
        private Color _base = Color.white;
        private bool _resolved;

        private Color _flashColor = Color.white;
        private float _flashAmount;

        /// <summary>
        /// The body sprite: the renderer on this object, or the first one below it. Same
        /// rule the rest of the entity code uses — the world-space health, mana and dash
        /// bars are child SpriteRenderers on the same transform, so "all renderers" would
        /// tint the HP bar along with the monster.
        /// </summary>
        public static SpriteRenderer ResolveBodyRenderer(GameObject go)
        {
            if (go == null) return null;
            var sr = go.GetComponent<SpriteRenderer>();
            return sr != null ? sr : go.GetComponentInChildren<SpriteRenderer>();
        }

        /// <summary>
        /// Get the stack for an entity, creating it on first use. Returns null only when
        /// the entity has no body sprite at all, which callers must tolerate — spawners,
        /// triggers and test doubles all reach this code without one.
        /// </summary>
        public static SpriteTintStack Attach(GameObject go)
        {
            if (go == null) return null;

            var existing = go.GetComponent<SpriteTintStack>();
            if (existing != null) return existing;

            if (ResolveBodyRenderer(go) == null) return null;
            return go.AddComponent<SpriteTintStack>();
        }

        /// <summary>Convenience for the common "tint the thing this component is on" call.</summary>
        public static SpriteTintStack Attach(Component c) => c != null ? Attach(c.gameObject) : null;

        public SpriteRenderer BodyRenderer { get { EnsureResolved(); return _sr; } }

        /// <summary>The untinted colour every layer composes against.</summary>
        public Color BaseColor { get { EnsureResolved(); return _base; } }

        public bool IsActive(TintLayer layer) => (_activeMask & (1 << (int)layer)) != 0;

        /// <summary>Current flash strength, 0 when the sprite is not flashing.</summary>
        public float FlashAmount => _flashAmount;

        private void Awake() => EnsureResolved();

        private void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;
            _sr = ResolveBodyRenderer(gameObject);
            if (_sr != null) _base = _sr.color;
        }

        /// <summary>
        /// Declare a new pristine colour. For the one case that legitimately changes what
        /// "untinted" means — an entity swapping sprites on a pooled respawn. Not for
        /// effects: an effect that rebases makes its own tint permanent, which is the bug
        /// this class exists to remove.
        /// </summary>
        public void Rebase(Color pristine)
        {
            EnsureResolved();
            _base = pristine;
            Apply();
        }

        /// <summary>Set this layer's contribution, as the colour it would produce on a white sprite.</summary>
        public void Set(TintLayer layer, Color tint)
        {
            EnsureResolved();
            _layers[(int)layer] = tint;
            _activeMask |= 1 << (int)layer;
            Apply();
        }

        public void Clear(TintLayer layer)
        {
            _activeMask &= ~(1 << (int)layer);
            Apply();
        }

        /// <summary>
        /// Push the sprite toward <paramref name="color"/>. Separate from the layers because
        /// a flash overrides rather than blends — a hit reads as a hit whatever the victim
        /// is currently tinted, and multiplying white into an orange sprite produces orange.
        /// </summary>
        public void SetFlash(Color color, float amount01)
        {
            _flashColor = color;
            _flashAmount = Mathf.Clamp01(amount01);
            Apply();
        }

        public void ClearFlash()
        {
            _flashAmount = 0f;
            Apply();
        }

        /// <summary>
        /// The colour the sprite should currently be. Public so tests can assert the
        /// composition without a renderer, and so callers can read the result they caused.
        /// </summary>
        public Color Compose()
        {
            EnsureResolved();

            Color c = _base;
            for (int i = 0; i < LAYER_COUNT; i++)
            {
                if ((_activeMask & (1 << i)) == 0) continue;
                Color t = _layers[i];
                c.r *= t.r; c.g *= t.g; c.b *= t.b; c.a *= t.a;
            }

            if (_flashAmount > 0f)
            {
                float alpha = c.a;   // a flash brightens; it must not fade a dematerialising body in
                c = Color.Lerp(c, _flashColor, _flashAmount);
                c.a = alpha;
            }
            return c;
        }

        /// <summary>
        /// Write the composed colour now rather than at the end of the frame. Deferring
        /// would be marginally cheaper, but it makes every caller's effect invisible until
        /// the next frame — including to the tests that assert it, and to any code that
        /// reads the colour back after setting it.
        /// </summary>
        private void Apply()
        {
            EnsureResolved();
            if (_sr != null) _sr.color = Compose();
        }

        /// <summary>
        /// Drop every contribution and restore the pristine colour immediately. For pooled
        /// entities going back into the pool: a returning object must not carry the tint of
        /// whatever killed it last time.
        /// </summary>
        public void ResetAll()
        {
            _activeMask = 0;
            _flashAmount = 0f;
            Apply();
        }
    }
}
