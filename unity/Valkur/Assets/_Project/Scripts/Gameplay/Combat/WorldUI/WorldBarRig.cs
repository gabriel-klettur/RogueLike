using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>What moved a health value, which decides what the bar does about it.</summary>
    public enum WorldBarChange
    {
        /// <summary>Set the value with no chip, no flash and no shake. Restores, max-HP edits, boot.</summary>
        Silent = 0,

        /// <summary>A blow. Leaves the delayed chip, flashes the plate and shakes the rig.</summary>
        Damage = 1,

        /// <summary>A heal. Overshoots bright and retires any chip it grows past.</summary>
        Heal = 2,
    }

    /// <summary>
    /// The single owner of everything drawn over one entity's head.
    ///
    /// <para><b>Why one component rather than three.</b> <c>WorldHealthBar</c>,
    /// <c>WorldManaBar</c> and <c>WorldDashBar</c> each built their own three quads, each carried
    /// their own copy of <c>CreateBarPart</c>, and the two upper ones carried private duplicates
    /// of the lower ones' geometry (<c>healthBarMargin 0.12</c>, <c>healthBarH 0.1</c>,
    /// <c>dashBarH 0.07</c>, <c>dashGap 0.06</c>) so they could stack above a bar they had no way
    /// to ask. Changing the health bar's height moved the health bar and left the other two
    /// hanging, silently. Those three components still exist and are still what
    /// <c>EntitySetup</c> attaches — they are now DRIVERS that report a number to this rig, which
    /// owns the drawing, the layout, the feel and the visibility.</para>
    ///
    /// <para><b>What it draws.</b> A health row and, when the entity has the resources for it, a
    /// second row carrying mana and the dash charge — two rows where there were three, because a
    /// dash charge is a COUNT and gets a pip rather than a third strip identical to the mana bar.
    /// Above them sits the status row, which is the first time in this project's life that Burn,
    /// Poison, Stun, Freeze, Slow, Root, Vulnerable or Marked have been visible at all.</para>
    ///
    /// <para><b>What it refuses to do.</b> It never touches the body's <c>SpriteRenderer.color</c>
    /// — that belongs to <c>SpriteTintStack</c> — and it never reads input, a faction string or a
    /// spell. It is a readout.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class WorldBarRig : MonoBehaviour
    {
        // -- Sorting layout ---------------------------------------------------
        // Offsets from the entity's own Y-derived base. A row claims five (frame, plate, chip,
        // fill, marks) and the pip two, so: health 0..4, resource 5..9, pip 10..11, status 12.
        // Measured live before this was spaced out, the pip's frame and the mana fill both landed
        // on +8 — harmless only because they sit at different X, which is the kind of collision
        // that surfaces the day something moves. The whole span is 13 orders, i.e. 0.13 world
        // units of Y granularity, well inside SORT_ORDER_HEADROOM.
        private const int SORT_HEALTH = 0;
        private const int SORT_RESOURCE = 5;
        private const int SORT_PIP = 10;
        private const int SORT_STATUS = 12;

        private const float Y_RESORT_THRESHOLD = 0.01f;

        private Transform _root;
        private Transform _shaker;
        private SpriteRenderer _body;

        private WorldBarLine _health;
        private WorldBarLine _mana;
        private WorldBarPip _pip;
        private WorldStatusIconRow _status;

        private WorldBarRank _rank = WorldBarRank.Normal;
        private Color _healthFillOverride;
        private Color _healthLowOverride;
        private bool _hasColourOverride;
        private bool _hideAtFullHealth = true;

        private bool _wantsMana;
        private bool _wantsDash;

        private float _barWidth;
        private float _lastSortY = float.NaN;
        private int _sortBase;

        private float _healthRatio = 1f;
        private bool _dead;
        private float _activityLeft;
        private float _alpha;
        private float _alphaTarget;

        private float _shakeLeft;
        private float _shakeSeed;

        private bool _built;
        private bool _layoutDirty = true;
        private bool _suppressed;
        private bool _healthSeeded;

        /// <summary>Alpha the whole rig is drawn at. 0 means faded out, not destroyed.</summary>
        public float Alpha => _alpha;

        /// <summary>Outer width of a row, in world units. On the texel grid by construction.</summary>
        public float BarWidth => _barWidth;

        /// <summary>The rank the frame is reporting.</summary>
        public WorldBarRank Rank => _rank;

        /// <summary>True while the second row exists — i.e. the entity has mana or a dash.</summary>
        public bool HasResourceRow => _mana != null || _pip != null;

        /// <summary>
        /// The health ratio actually being DRAWN, which trails the model while the fill catches
        /// up. Exposed so a test can assert on what the player sees rather than on what
        /// <c>Health</c> holds — the two disagree on purpose, and only one of them is the readout.
        /// </summary>
        public float HealthShown => _health != null ? _health.Shown : 0f;

        /// <summary>True while the delayed chip is still standing behind the health fill.</summary>
        public bool HealthChipActive => _health != null && _health.ChipActive;

        /// <summary>True while the rig is drawing anything at all.</summary>
        public bool IsVisible => _alpha > 0.001f;

        /// <summary>
        /// Where the fade is HEADING, which is the decision; <see cref="Alpha"/> is only how far
        /// it has got. They are separate because the decision is testable without a clock, and
        /// Edit Mode has no clock at all — <c>Time.deltaTime</c> is 0 there, so a fade started in
        /// a fixture never advances by a single frame.
        /// </summary>
        public float AlphaTarget => _alphaTarget;

        // -- Creation ---------------------------------------------------------

        /// <summary>
        /// The rig on <paramref name="go"/>, created if it is not there yet.
        ///
        /// <para>Every driver goes through this rather than <c>AddComponent</c> so the three of
        /// them cannot each build a rig of their own — which is the arrangement that produced
        /// three unrelated stacks in the first place.</para>
        /// </summary>
        public static WorldBarRig Ensure(GameObject go)
        {
            if (go == null) return null;
            var rig = go.GetComponent<WorldBarRig>();
            if (rig == null) rig = go.AddComponent<WorldBarRig>();
            rig.EnsureBuilt();
            return rig;
        }

        private void Awake() => EnsureBuilt();

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            // Resolved BEFORE any child of ours exists. Once the rig is built, a
            // GetComponentInChildren<SpriteRenderer> would find a bar quad and measure the bar
            // instead of the creature — which is how a readout ends up sized from itself.
            _body = GetComponent<SpriteRenderer>();
            if (_body == null) _body = FindBodyRenderer();

            var style = WorldBarStyle.Active;

            var rootGo = new GameObject("WorldBars");
            _root = rootGo.transform;
            _root.SetParent(transform, false);

            var shakerGo = new GameObject("Shake");
            _shaker = shakerGo.transform;
            _shaker.SetParent(_root, false);

            _health = new WorldBarLine(_shaker, "Health", WorldBarRow.Health,
                                       style.healthRowTexels, SORT_HEALTH, withNotches: true);
            _status = new WorldStatusIconRow(_shaker, style, SORT_STATUS);
            _status.Bind(GetComponent<StatusEffectManager>());

            _alpha = 1f;
            _alphaTarget = 1f;
            _activityLeft = style.idleFadeDelay;
            _shakeSeed = Random.value * 100f;

            _layoutDirty = true;
            ApplyColours();
            Relayout();
        }

        private SpriteRenderer FindBodyRenderer()
        {
            // Deliberately not GetComponentInChildren: an entity can carry outline and aura
            // renderers that are not its body, and the first one in the hierarchy is whichever
            // was attached first.
            var all = GetComponentsInChildren<SpriteRenderer>(true);
            SpriteRenderer best = null;
            float bestArea = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null || sr.sprite == null) continue;
                if (_root != null && sr.transform.IsChildOf(_root)) continue;
                var size = sr.bounds.size;
                float area = size.x * size.y;
                if (area > bestArea) { bestArea = area; best = sr; }
            }
            return best;
        }

        // -- Driver surface ---------------------------------------------------

        /// <summary>Which frame this entity gets. Ally beats elite beats boss beats normal.</summary>
        public void SetRank(WorldBarRank rank)
        {
            if (_rank == rank) return;
            _rank = rank;
            ApplyColours();
        }

        /// <summary>
        /// Override the health colours for this entity. Kept because <c>EntitySetup</c> has always
        /// coloured the player's bar by hand; passing nothing leaves the rank's own colours.
        /// </summary>
        public void SetHealthColours(Color fill, Color low)
        {
            _healthFillOverride = fill;
            _healthLowOverride = low;
            _hasColourOverride = true;
            ApplyColours();
        }

        /// <summary>Whether a full-health bar fades away. False keeps the player's on screen.</summary>
        public void SetHideAtFullHealth(bool hide)
        {
            _hideAtFullHealth = hide;
            MarkActivity();
        }

        /// <summary>Report health. <paramref name="change"/> decides the chip, flash and shake.</summary>
        public void SetHealth(int current, int max, WorldBarChange change)
        {
            EnsureBuilt();
            var style = WorldBarStyle.Active;
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            // The FIRST report is the entity's starting health and must not animate up from
            // nothing — every creature in the world would fill its bar on the frame it spawned.
            bool first = !_healthSeeded;
            _healthSeeded = true;

            _dead = current <= 0;
            _healthRatio = ratio;
            _health.SetRatio(ratio, instant: first, leaveChip: change == WorldBarChange.Damage, style);

            if (change == WorldBarChange.Damage)
            {
                _health.Flash(style.hitFlashSeconds);
                Shake(style);
            }
            if (change != WorldBarChange.Silent) MarkActivity();
        }

        /// <summary>Create or drop the mana half of the resource row.</summary>
        public void EnableMana(bool on)
        {
            EnsureBuilt();
            if (_wantsMana == on) return;
            _wantsMana = on;
            if (on && _mana == null)
            {
                var style = WorldBarStyle.Active;
                _mana = new WorldBarLine(_shaker, "Mana", WorldBarRow.Resource,
                                         style.resourceRowTexels, SORT_RESOURCE, withNotches: false);
                ApplyColours();
            }
            _mana?.SetActive(on);
            _layoutDirty = true;
        }

        /// <summary>Report mana. A spend leaves the same delayed ghost a blow leaves on health.</summary>
        public void SetMana(int current, int max, WorldBarChange change)
        {
            if (_mana == null) return;
            var style = WorldBarStyle.Active;
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            _mana.SetRatio(ratio, instant: false, leaveChip: change == WorldBarChange.Damage, style);
            if (change != WorldBarChange.Silent) MarkActivity();
        }

        /// <summary>Create or drop the dash pip.</summary>
        public void EnableDash(bool on)
        {
            EnsureBuilt();
            if (_wantsDash == on) return;
            _wantsDash = on;
            if (on && _pip == null)
            {
                var style = WorldBarStyle.Active;
                _pip = new WorldBarPip(_shaker, style.pipTexels, SORT_PIP);
                ApplyColours();
            }
            _pip?.SetActive(on);
            _layoutDirty = true;
        }

        /// <summary>Report the dash charge, 0..1.</summary>
        public void SetDashCharge(float charge)
        {
            if (_pip == null) return;
            if (_pip.SetCharge(charge, WorldBarStyle.Active)) MarkActivity();
        }

        /// <summary>
        /// Re-measure the body and rebuild the layout. Called after a loadout swap or a scale
        /// change: the old bars measured the sprite once in <c>Awake</c> and never again, so a
        /// character who put on a staff kept a bar sized for the character who had not.
        /// </summary>
        public void Remeasure()
        {
            if (!_built) return;
            if (_body == null || _body.sprite == null) _body = GetComponent<SpriteRenderer>() ?? FindBodyRenderer();
            _layoutDirty = true;
        }

        /// <summary>Something happened. Holds the rig on screen for the style's idle window.</summary>
        public void MarkActivity() => _activityLeft = WorldBarStyle.Active.idleFadeDelay;

        /// <summary>
        /// Put the whole readout away without destroying it.
        ///
        /// <para>The health driver raises this from <c>OnDisable</c>, which is how
        /// <c>UnconsciousState</c> has always put a downed NPC's bar away — it disables the three
        /// bar components. With the drawing living on a separate object, disabling a driver hides
        /// nothing unless the driver says so.</para>
        /// </summary>
        public void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed) return;
            _suppressed = suppressed;
            if (!suppressed) MarkActivity();
        }

        // -- Frame ------------------------------------------------------------

        private void LateUpdate()
        {
            if (!_built) return;
            var style = WorldBarStyle.Active;
            float dt = Time.deltaTime;

            if (_layoutDirty) Relayout();

            SyncSorting();
            TickVisibility(dt, style);
            TickShake(dt, style);

            // A fully faded rig writes nothing at all. That is the common case for a world full
            // of undamaged monsters, and it is why the old bars' unconditional per-frame transform
            // writes were worth removing rather than merely guarding.
            if (_alpha <= 0.001f && Mathf.Approximately(_alphaTarget, 0f)) return;

            float ppu = WorldBarPixelGrid.PixelsPerUnit;
            _health.Tick(dt, ppu, style, heartbeat: !_dead);
            _mana?.Tick(dt, ppu, style, heartbeat: false);
            _pip?.Tick(dt, style);
            if (_status.Tick(dt, style)) MarkActivityIfFading();
        }

        private void MarkActivityIfFading()
        {
            // A status is a standing fact rather than an event, so it holds the rig visible
            // without continually re-arming the idle window.
            if (_activityLeft < 0.25f) _activityLeft = 0.25f;
        }

        private void TickVisibility(float dt, WorldBarStyle style)
        {
            if (_activityLeft > 0f) _activityLeft -= dt;

            // One rule for every entity in the game. The old behaviour was two: monsters hid at
            // full health with a hard SetActive, the player never hid at all — so the player's
            // three bars were a permanent 34 screen pixels of silhouette repeating what the
            // corner HUD already said. Fading on an idle clock keeps the answer available the
            // instant it changes and costs nothing while it does not.
            bool newsworthy =
                _healthRatio < 0.999f ||
                _status.HasAny ||
                _activityLeft > 0f ||
                !_hideAtFullHealth ||
                !style.hostilesHideAtFullHealth;

            // Suppression wins over everything: it is UnconsciousState putting a downed NPC's
            // readout away, and a driver that has been disabled has no business drawing.
            if (_suppressed) newsworthy = false;

            // DEATH IS NOT THE SAME EVENT FOR A MONSTER AND FOR THE PLAYER, and treating it as
            // one is what this used to do — inherited verbatim from the old bars, whose whole
            // visibility rule was `if (_health.IsDead) show = false`.
            //
            // A dead monster is a corpse: its bar is noise on something that is about to despawn.
            // A dead PLAYER is a spirit walking to an altar against `spiritTimeLimitSeconds`, and
            // hiding their readout takes the display away at the one moment the run is actually
            // in danger. It also reads as a bug rather than as a decision, which is how it was
            // reported.
            else if (_dead) newsworthy = _rank == WorldBarRank.Player;

            _alphaTarget = newsworthy ? 1f : 0f;

            if (!Mathf.Approximately(_alpha, _alphaTarget))
            {
                float rate = style.fadeSeconds > 0f ? dt / style.fadeSeconds : 1f;
                _alpha = Mathf.MoveTowards(_alpha, _alphaTarget, rate);
                PushAlpha();
            }
        }

        private void PushAlpha()
        {
            _health.SetAlpha(_alpha);
            _mana?.SetAlpha(_alpha);
            _pip?.SetAlpha(_alpha);
            _status.SetAlpha(_alpha);

            bool visible = _alpha > 0.001f;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        private void Shake(WorldBarStyle style)
        {
            if (style.hitShakeSeconds <= 0f || style.hitShakeTexels <= 0f) return;
            _shakeLeft = style.hitShakeSeconds;
        }

        private void TickShake(float dt, WorldBarStyle style)
        {
            if (_shakeLeft <= 0f) return;
            _shakeLeft -= dt;
            if (_shakeLeft <= 0f)
            {
                _shaker.localPosition = Vector3.zero;
                return;
            }
            float t = _shakeLeft / Mathf.Max(0.0001f, style.hitShakeSeconds);
            float amp = WorldBarGeometry.Texels(style.hitShakeTexels) * t;
            // Quantised to a whole texel so the shake reads as the readout being knocked rather
            // than as it going soft: a sub-texel offset only resamples the art.
            float x = WorldBarGeometry.SnapToTexel(
                amp * Mathf.Sin((Time.time + _shakeSeed) * 46f));
            _shaker.localPosition = new Vector3(x, 0f, 0f);
        }
    }
}
