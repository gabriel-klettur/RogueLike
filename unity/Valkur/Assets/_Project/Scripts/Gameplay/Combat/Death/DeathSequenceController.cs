using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Data;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Orchestrates the player's death-and-revive flow:
    ///
    ///   ALIVE → DYING (a drama beat) → SPIRIT (walk to altar)
    ///         → REVIVING (grayscale fade-out) → ALIVE
    ///
    /// Subscribes to <c>GameEvents.OnPlayerDied</c> to enter DYING. The resurrection altar
    /// (<see cref="World.ResurrectionZone"/>) calls <see cref="Revive"/> when the spirit walks
    /// into its footprint; <see cref="ForceRevive"/> is the DevConsole cheat; and
    /// <see cref="DeathSequenceController.Rescue"/> — in the sibling partial — is the guarantee
    /// that a death always has an exit even when no altar does.
    ///
    /// <para>This controller is the only place that mutates layer masks, the grayscale volume
    /// weight and the corpse lifetime. Keeping the state machine in one file is what prevents the
    /// half-applied transitions that plagued the older DeathScreenUI / GrayscaleDeath split.</para>
    ///
    /// <para><b>It carries no tuning of its own any more.</b> Every number comes from
    /// <see cref="DeathTuning.Active"/>: the three timings used to be <c>[SerializeField]</c> on a
    /// component <c>GameplaySceneSetup</c> <c>AddComponent</c>s onto a bare GameObject, so there
    /// was no inspector anywhere in the project that could reach them — the same unreachable-field
    /// defect as <c>ChatSystem._catalog</c>, and the reason the Death Editor exists.</para>
    /// </summary>
    public partial class DeathSequenceController : MonoBehaviour
    {
        public enum Phase
        {
            Alive,
            Dying,
            Spirit,
            Reviving,
        }

        /// <summary>
        /// How the player got back up. Read by <see cref="XpLossOnDeathSystem"/>, which is what
        /// makes the distinction REAL rather than authored-and-inert: the flow fired two events
        /// whose doc comment promised exactly this difference, and nothing anywhere read them
        /// apart — so the DevConsole cheat charged the player 10 % of their XP.
        /// </summary>
        public enum ReviveKind
        {
            /// <summary>Reached an altar. The intended path — full HP, full cost.</summary>
            Altar = 0,

            /// <summary>The safety net fired. Partial HP, full cost.</summary>
            Rescue = 1,

            /// <summary>DevConsole / editor. Instant, and free unless the tuning says otherwise.</summary>
            Cheat = 2,
        }

        private GrayscaleVolumeController _grayscale;
        private PlayerCorpseMarker _activeCorpse;
        private Coroutine _activeCoroutine;

        // The collider exclusion applied while in spirit form, rebuilt from the tuning on every
        // entry rather than cached in Awake: the Death Editor can flip "atraviesa muros" while a
        // spirit is on screen, and a mask captured once would ignore it until the next death.
        private LayerMask _savedExcludeLayers;
        private bool _excludeCaptured;

        public Phase CurrentPhase { get; private set; } = Phase.Alive;
        public bool IsDeathFlowActive => CurrentPhase != Phase.Alive;

        /// <summary>How the player last got back up. <see cref="ReviveKind.Altar"/> before any revive.</summary>
        public ReviveKind LastReviveKind { get; private set; } = ReviveKind.Altar;

        /// <summary>Unscaled seconds spent in spirit form on the current death. 0 when alive.</summary>
        public float SpiritElapsed { get; private set; }

        /// <summary>Where the body fell on the current death. Used by the corpse compass and by rescue.</summary>
        public Vector3 LastDeathPosition { get; private set; }

        /// <summary>The corpse of the current death, or null.</summary>
        public PlayerCorpseMarker ActiveCorpse => _activeCorpse;

        private static DeathTuning Tuning => DeathTuning.Active;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        private void Awake()
        {
            ServiceLocator.Register<DeathSequenceController>(this);
        }

        private void OnEnable()
        {
            GameEvents.OnPlayerDied += OnPlayerDied;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerDied -= OnPlayerDied;
        }

        /// <summary>
        /// Two jobs, both of them safety nets.
        ///
        /// <para>The first: if <c>GameEvents.OnPlayerDied</c> fired before this controller
        /// subscribed (mid-Play recompile, a scene transition that called <c>GameEvents.Clear()</c>
        /// and re-spawned the controller after Health), a player sits at HP 0 with no flow ever
        /// having started. Poll for it.</para>
        ///
        /// <para>The second is <see cref="TickSpirit"/> — the clock that makes a dead end
        /// impossible.</para>
        /// </summary>
        private void Update()
        {
            if (CurrentPhase == Phase.Spirit) { TickSpirit(); return; }
            if (CurrentPhase != Phase.Alive) return;
            if (_activeCoroutine != null) return;

            var player = EntityRegistry.Player;
            if (player == null) return;
            var health = player.GetComponent<Health>();
            if (health == null || !health.IsDead) return;

            Debug.Log("[DeathSequence] Detected HP=0 player without an active death flow — recovering by dispatching DeathRoutine manually.");
            _activeCoroutine = StartCoroutine(DeathRoutine());
        }

        private void OnDestroy()
        {
            if (ServiceLocator.Get<DeathSequenceController>() == this)
                ServiceLocator.Unregister<DeathSequenceController>();
        }

        // ── Public API ──────────────────────────────────────────────────────────

        public void BindGrayscaleController(GrayscaleVolumeController controller)
        {
            _grayscale = controller;
        }

        /// <summary>Trigger the slow revive sequence. Called by <see cref="World.ResurrectionZone"/>.</summary>
        public void Revive()
        {
            if (CurrentPhase != Phase.Spirit) return;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(ReviveRoutine(ReviveKind.Altar, instant: false));
        }

        /// <summary>
        /// Skip the spirit phase entirely (DevConsole <c>resurrect</c>). Snaps grayscale to 0 and
        /// restores the player without waiting on fades — and, by default, charges nothing.
        /// </summary>
        public void ForceRevive()
        {
            if (CurrentPhase == Phase.Alive) return;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(ReviveRoutine(ReviveKind.Cheat, instant: true));
        }

        /// <summary>
        /// Kill the player from an authoring surface (the Death Editor's test button).
        ///
        /// <para>Routed through <c>Health.TakeDamage</c> rather than firing the event directly, so
        /// the whole pipeline runs exactly as it does in play — a test path that skips half the
        /// flow tests half the flow. The damage is deliberately a large multiple of max HP because
        /// <c>MitigateDamage</c> sits in the way and a resistance could otherwise absorb it.</para>
        ///
        /// <para>It REFUSES while invincible instead of clearing the flag.
        /// <c>SetInvincible</c> has three independent owners — god mode, the Spells editor and the
        /// shield — and writing false here would silently switch off whichever was holding it,
        /// which is a defect this project has already shipped twice. <paramref name="refusal"/>
        /// carries the reason so the caller can put it on screen.</para>
        /// </summary>
        public bool KillPlayerForTesting(out string refusal)
        {
            refusal = null;

            var player = EntityRegistry.Player;
            if (player == null) { refusal = "No hay jugador en el EntityRegistry."; return false; }

            var health = player.GetComponent<Health>();
            if (health == null) { refusal = "El jugador no tiene componente Health."; return false; }
            if (health.IsDead) { refusal = "El jugador ya esta muerto."; return false; }
            if (health.IsInvincible)
            {
                refusal = "El jugador es invulnerable ahora mismo (god mode, editor de hechizos o escudo). " +
                          "Quitalo desde donde lo pusiste: limpiarlo desde aqui apagaria el de otro sistema.";
                return false;
            }

            health.TakeDamage(health.MaxHp * 100);
            return true;
        }

        /// <summary>
        /// Put the flow straight back into SPIRIT after a save is loaded, without replaying the
        /// death: no drops (they were dropped and saved on the ground before), no dying beat, no
        /// second XP penalty.
        ///
        /// <para>The corpse is respawned at <paramref name="corpsePosition"/> rather than at the
        /// player, because those are two different places — a spirit halfway to the altar is
        /// nowhere near its own loot, and putting the marker under the player's feet would point
        /// the compass at nothing.</para>
        ///
        /// <para>It refuses when the flow is ALREADY active, which is the load-into-a-dead-session
        /// case: the poll in <see cref="Update"/> may have started a death routine from the
        /// restored HP before the restorer reached this call, and running both would drop the
        /// inventory a second time — into a bag the save has already emptied.</para>
        /// </summary>
        public bool RestoreSpiritState(Vector3 corpsePosition)
        {
            if (CurrentPhase != Phase.Alive) return false;

            var player = EntityRegistry.Player;
            if (player == null) return false;

            LastDeathPosition = corpsePosition;
            _activeCorpse = PlayerCorpseMarker.Spawn(corpsePosition, player);

            if (_grayscale != null) _grayscale.SetWeight(1f);

            EnterSpirit(player);
            SpiritElapsed = 0f;
            ResetRescueClock();
            CurrentPhase = Phase.Spirit;

            // The world's desaturation is driven by GameEvents rather than by the phase, so a
            // restored spirit needs it applied — but NOT by firing OnPlayerDied. That event has
            // four other subscribers and one of them is PermadeathSaveCleanupSystem, which would
            // delete the very save that was just loaded. Reach the one system that needs telling.
            ServiceLocator.Get<SpiritWorldGrayscale>()?.ForceApply();
            return true;
        }

        // ── Internal flow ───────────────────────────────────────────────────────

        private void OnPlayerDied()
        {
            if (CurrentPhase != Phase.Alive) return;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(DeathRoutine());
        }

        private IEnumerator DeathRoutine()
        {
            CurrentPhase = Phase.Dying;
            var tuning = Tuning;

            var player = EntityRegistry.Player;
            if (player == null)
            {
                Debug.LogWarning("[DeathSequence] OnPlayerDied with no player in EntityRegistry; aborting.");
                CurrentPhase = Phase.Alive;
                yield break;
            }

            Vector3 deathPos = player.transform.position;
            LastDeathPosition = deathPos;

            // Order matters: the previous death's litter goes BEFORE this death's drops, or the
            // cleanup sweep would find and remove the items that have just landed.
            if (tuning.cleanupPreviousDrops) DeathLitter.ClearAll();

            PlayerDeathDropSystem.DropEverything(player);
            _activeCorpse = PlayerCorpseMarker.Spawn(deathPos, player);

            PlayDeathSfx(tuning.sfxDeath);

            if (_grayscale != null) _grayscale.FadeIn(tuning.grayscaleFadeIn);

            float t = 0f;
            while (t < tuning.dyingFlashDuration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            EnterSpirit(player);
            SpiritElapsed = 0f;
            ResetRescueClock();
            CurrentPhase = Phase.Spirit;
            PlayDeathSfx(tuning.sfxSpiritEnter);
            _activeCoroutine = null;
        }

        private IEnumerator ReviveRoutine(ReviveKind kind, bool instant, Vector3? teleportTo = null)
        {
            CurrentPhase = Phase.Reviving;
            LastReviveKind = kind;
            var tuning = Tuning;
            var player = EntityRegistry.Player;

            // Move the body BEFORE the fade rather than after: a rescue that teleports once the
            // world is already back in colour reads as the screen having jumped.
            if (player != null && teleportTo.HasValue) TeleportPlayer(player, teleportTo.Value);

            if (instant)
            {
                if (_grayscale != null) _grayscale.SetWeight(0f);
            }
            else
            {
                if (_grayscale != null) _grayscale.FadeOut(tuning.grayscaleFadeOut);
                float t = 0f;
                while (t < tuning.grayscaleFadeOut)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (player != null) ExitSpirit(player, kind);

            RetireCorpse(tuning);

            // Getting back up, after the body is solid again and the corpse is gone, so the player
            // sees themselves rise rather than rise next to their own corpse. Skipped on the
            // instant path: that is the DevConsole cheat, where waiting out an animation is the
            // opposite of what was asked for.
            if (!instant && player != null)
            {
                var controller = player.GetComponent<PlayerController>();
                if (controller != null)
                {
                    // Length of the actual animation, not a constant: the elven rise is eight
                    // frames and a character with no recover art falls back to a one-frame idle,
                    // which must not hold locomotion for a fixed fifth of a second.
                    float duration = ResolveRecoverDuration(player);
                    if (duration > 0f)
                    {
                        controller.PlayRecoverAnimation(duration);
                        float elapsed = 0f;
                        while (elapsed < duration)
                        {
                            elapsed += Time.unscaledDeltaTime;
                            yield return null;
                        }
                    }
                }
            }

            SpiritElapsed = 0f;
            CurrentPhase = Phase.Alive;
            _activeCoroutine = null;
            PlayDeathSfx(tuning.sfxRevive);

            // Both events fire on every path, so every visual and UI listener behaves identically
            // however the player got up. The DIFFERENCE between an altar, a rescue and a cheat is
            // carried by LastReviveKind and read by the one system it actually changes — the XP
            // penalty. Encoding it in which event fires would make a UI listener's behaviour
            // depend on the cost model, which is how the banner ended up needing a safety net.
            GameEvents.FirePlayerResurrected();
            GameEvents.FirePlayerRevived();
        }

        /// <summary>
        /// Retire the corpse: immediately, or after <see cref="DeathTuning.corpseLingerSeconds"/>.
        ///
        /// <para>A lingering corpse is not decoration — it is the marker the player walks back to.
        /// The compass points at it, and a corpse that vanishes the instant they stand up leaves
        /// them looking for a pile of items with nothing above it.</para>
        /// </summary>
        private void RetireCorpse(DeathTuning tuning)
        {
            if (_activeCorpse == null) return;

            if (tuning.corpseLingerSeconds > 0f)
            {
                // The reference is KEPT, not cleared. The corpse compass hangs off ActiveCorpse, so
                // nulling it here would kill the trail on the very frame the player stands up —
                // i.e. at the start of the one walk the compass exists for. The marker destroys
                // itself on its own clock and the Unity-null check picks that up; the next death
                // overwrites the field before it could matter.
                _activeCorpse.DespawnAfter(tuning.corpseLingerSeconds);
                return;
            }

            _activeCorpse.Despawn();
            _activeCorpse = null;
        }

        /// <summary>
        /// How long the rise animation actually runs, or 0 when this character has none.
        ///
        /// <para>Returns 0 rather than a default when the set is missing so the caller skips the
        /// wait entirely: <c>DirectionalAnimator</c> falls Recover back to idle, and holding a
        /// character in an idle pose for a fixed duration after a revive would read as the game
        /// having frozen.</para>
        /// </summary>
        private static float ResolveRecoverDuration(GameObject player)
        {
            var animator = player.GetComponent<DirectionalAnimator>();
            if (animator == null) return 0f;

            var recover = animator.RecoverSprites;
            bool hasRecoverArt =
                (recover.south != null && recover.south.Length > 0) ||
                (recover.southEast != null && recover.southEast.Length > 0) ||
                (recover.east != null && recover.east.Length > 0) ||
                (recover.northEast != null && recover.northEast.Length > 0) ||
                (recover.north != null && recover.north.Length > 0) ||
                (recover.northWest != null && recover.northWest.Length > 0) ||
                (recover.west != null && recover.west.Length > 0) ||
                (recover.southWest != null && recover.southWest.Length > 0);
            if (!hasRecoverArt) return 0f;

            return animator.GetStateLength(DirectionalAnimator.AnimState.Recover);
        }

        private void EnterSpirit(GameObject player)
        {
            // Defensive AddComponent: EntitySetup attaches these during ConfigurePlayer, but a
            // live Player instance that predates a recompile will be missing them and the spirit
            // would never activate.
            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit == null) spirit = player.AddComponent<PlayerSpiritState>();
            spirit.EnterSpirit();

            var visuals = player.GetComponent<PlayerSpiritVisuals>();
            if (visuals == null) visuals = player.AddComponent<PlayerSpiritVisuals>();
            visuals.Activate();

            var col = player.GetComponent<Collider2D>();
            if (col != null && !_excludeCaptured)
            {
                _savedExcludeLayers = col.excludeLayers;
                _excludeCaptured = true;
                col.excludeLayers = _savedExcludeLayers | BuildSpiritExcludeMask();
            }
        }

        private void ExitSpirit(GameObject player, ReviveKind kind)
        {
            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit != null) spirit.ExitSpirit();

            var visuals = player.GetComponent<PlayerSpiritVisuals>();
            if (visuals != null) visuals.Deactivate();

            var col = player.GetComponent<Collider2D>();
            if (col != null && _excludeCaptured)
            {
                col.excludeLayers = _savedExcludeLayers;
                _excludeCaptured = false;
            }

            // An altar revives you whole; the safety net does not, or reaching the altar would be
            // the slower way to get the same thing and the altar would stop being worth walking to.
            var health = player.GetComponent<Health>();
            if (health != null)
            {
                float fraction = kind == ReviveKind.Rescue ? Mathf.Clamp01(Tuning.rescueHpFraction) : 1f;
                int hp = Mathf.Max(1, Mathf.RoundToInt(health.MaxHp * fraction));
                health.Initialize(health.MaxHp, hp);
            }

            var mana = player.GetComponent<Mana>();
            if (mana != null) mana.Restore(mana.MaxMana);

            var pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;

            // If anything left timeScale at 0 during the brief flash, restore it.
            if (Time.timeScale < 0.01f) Time.timeScale = 1f;
        }

        /// <summary>
        /// The layers the spirit's collider ignores.
        ///
        /// <para>NPC / Projectile / Pickup unless the tuning makes the spirit targetable, and —
        /// this is the half that was missing — World and Building when it may pass through walls.
        /// Without that second group the path highlighter drew a straight line through geometry
        /// the ghost then bounced off, which is a compass pointing down a route that does not
        /// exist. The two settings are meant to be read together and the editor says so.</para>
        /// </summary>
        private static LayerMask BuildSpiritExcludeMask()
        {
            var tuning = Tuning;
            int mask = 0;

            if (!tuning.spiritIsTargetable)
            {
                AddLayer(ref mask, "NPC");
                AddLayer(ref mask, "Projectile");
                AddLayer(ref mask, "Pickup");
            }

            if (tuning.spiritPassesThroughWalls)
            {
                AddLayer(ref mask, "World");
                AddLayer(ref mask, "Building");
            }

            return mask;
        }

        private static void AddLayer(ref int mask, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask |= 1 << layer;
        }

        /// <summary>
        /// Play a one-shot, but only when the catalogue really has it.
        ///
        /// <para><c>PlaySfxById</c> warns once per unresolved id BY DESIGN — an explicit id that
        /// fails to resolve is a data bug — so calling it blind on ids nobody has authored yet
        /// would push a warning into a console this project requires to be clean. The cone breath
        /// paid for exactly this.</para>
        /// </summary>
        private static void PlayDeathSfx(string sfxId)
        {
            if (string.IsNullOrWhiteSpace(sfxId)) return;
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null || !audio.HasSfx(sfxId)) return;
            audio.PlaySfxById(sfxId);
        }
    }
}
