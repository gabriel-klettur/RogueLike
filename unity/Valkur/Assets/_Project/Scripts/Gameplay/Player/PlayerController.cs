using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Player movement, combat, and ability controller.
    /// Maps to Python's player movement + combat + spell casting systems.
    ///
    /// <para>
    /// The five core actions (Move, Look, PrimaryAttack, SecondaryAttack, Dash)
    /// come from the canonical <see cref="InputService.Gameplay"/> action map
    /// — bindings live in <c>Resources/Input/ValkurInputActions.inputactions</c>
    /// so a remap there propagates here automatically.
    /// </para>
    /// <para>
    /// MiddleClick + the 23 spell bindings (1-0, q/e/r/t/f/g/c/v/x/p/l/u/m)
    /// remain ad-hoc because the canonical asset's Gameplay map only ships 4
    /// generic spell slots — expanding it to 23 is a follow-up.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Health))]
    public partial class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4f;

        [Header("References")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        private Rigidbody2D _rb;
        private Health _health;
        private DirectionalAnimator _animator;
        private MeleeCombat _meleeCombat;
        private DashAbility _dashAbility;
        private SpellCaster _spellCaster;
        private StatusEffectManager _statusEffects;
        private Vector2 _moveInput;
        private Vector2 _facingDirection = Vector2.down;
        private Camera _mainCamera;

        // Resolved on demand from InputService.Gameplay — never cached as a long-lived
        // reference (avoids the zombie-after-hot-reload class of bug). Use the helpers
        // below (MoveAction, LookAction, …) at every read.
        private InputAction MoveAction            => InputService.Instance?.Gameplay?.Move;
        private InputAction LookAction            => InputService.Instance?.Gameplay?.Look;
        private InputAction PrimaryAttackAction   => InputService.Instance?.Gameplay?.PrimaryAttack;
        private InputAction SecondaryAttackAction => InputService.Instance?.Gameplay?.SecondaryAttack;
        private InputAction DashAction            => InputService.Instance?.Gameplay?.Dash;

        // Ad-hoc actions kept locally because the canonical asset doesn't model them.
        // EnsureInputActionsLive() rebuilds these if a hot-recompile zombifies the fields.
        private InputAction _middleClickAction;
        private readonly List<(InputAction action, string spellKey)> _spellBindings =
            new List<(InputAction, string)>();

        public Vector2 FacingDirection => _facingDirection;
        public Vector2 MoveInput => _moveInput;
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _animator = GetComponent<DirectionalAnimator>();
            _meleeCombat = GetComponent<MeleeCombat>();
            _dashAbility = GetComponent<DashAbility>();
            _spellCaster = GetComponent<SpellCaster>();
            _statusEffects = GetComponent<StatusEffectManager>();
            _mainCamera = Camera.main;

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // Ensure the canonical Gameplay map is live (InputService leaves it
            // disabled by default — pause / menus rely on that). The player's
            // existence implies gameplay is active.
            EnableGameplayMap();
            CreateAdHocActions();
        }

        private void EnableGameplayMap()
        {
            var gp = InputService.Instance?.Gameplay?.Map;
            if (gp != null && !gp.enabled) gp.Enable();
        }

        private void CreateAdHocActions()
        {
            _middleClickAction = new InputAction("MiddleClick", InputActionType.Button, "<Mouse>/middleButton");
            _middleClickAction.Enable();

            // Python parity: full spell key bindings.
            // Number keys
            AddSpellBinding("<Keyboard>/1", "darkball");
            AddSpellBinding("<Keyboard>/2", "iceball");
            AddSpellBinding("<Keyboard>/3", "lightball");
            AddSpellBinding("<Keyboard>/4", "puddle_lava");
            AddSpellBinding("<Keyboard>/5", "mine_basic");
            AddSpellBinding("<Keyboard>/6", "boomerang");
            AddSpellBinding("<Keyboard>/7", "chain_lightning");
            AddSpellBinding("<Keyboard>/8", "vortex_pull");
            AddSpellBinding("<Keyboard>/9", "vortex_push");
            AddSpellBinding("<Keyboard>/0", "flame_breath");

            // Letter keys
            AddSpellBinding("<Keyboard>/q", "teleport");
            AddSpellBinding("<Keyboard>/e", "slash");
            AddSpellBinding("<Keyboard>/r", "lightning");
            AddSpellBinding("<Keyboard>/t", "sphere_magic_shield");
            AddSpellBinding("<Keyboard>/f", "smoke");
            AddSpellBinding("<Keyboard>/g", "smoke_emitter");
            AddSpellBinding("<Keyboard>/c", "arcane_flame");
            AddSpellBinding("<Keyboard>/v", "firework_launch");
            AddSpellBinding("<Keyboard>/x", "healing_aura");
            AddSpellBinding("<Keyboard>/p", "meteor_shower");
            AddSpellBinding("<Keyboard>/l", "healing_totem");
            AddSpellBinding("<Keyboard>/u", "summon_barbol");
            AddSpellBinding("<Keyboard>/m", "wall_ice");

            Debug.Log($"[PlayerController] Ad-hoc actions: middleClick + {_spellBindings.Count} spell bindings. " +
                      "Move/Look/PrimaryAttack/SecondaryAttack/Dash come from InputService.Gameplay.");
        }

        /// <summary>
        /// Detect post-hot-reload zombie state on the ad-hoc <see cref="InputAction"/>
        /// fields and rebuild them. The InputService.Gameplay actions never go zombie
        /// because they are resolved on every read (see the *Action properties above);
        /// only the locally-owned MiddleClick + spell bindings need this guard.
        /// </summary>
        private void EnsureInputActionsLive()
        {
            // Ensure the canonical map is enabled — pause / menu flows can disable it.
            EnableGameplayMap();

            if (_middleClickAction != null && _middleClickAction.bindings.Count > 0) return;

            // Dispose the zombies and rebuild from scratch.
            DisposeIfNotNull(ref _middleClickAction);
            for (int i = 0; i < _spellBindings.Count; i++)
            {
                var sb = _spellBindings[i];
                try { sb.action?.Disable(); sb.action?.Dispose(); } catch { }
            }
            _spellBindings.Clear();

            CreateAdHocActions();
        }

        private static void DisposeIfNotNull(ref InputAction action)
        {
            if (action == null) return;
            try { action.Disable(); action.Dispose(); } catch { }
            action = null;
        }

        private void AddSpellBinding(string binding, string spellKey)
        {
            var action = new InputAction($"Spell_{spellKey}", InputActionType.Button, binding);
            action.Enable();
            _spellBindings.Add((action, spellKey));
        }

        private void OnEnable()
        {
            EnableGameplayMap();
            if (_middleClickAction != null) _middleClickAction.Enable();
            foreach (var (action, _) in _spellBindings)
                action?.Enable();
        }

        private void OnDisable()
        {
            // Don't disable the canonical Gameplay map here — pause/menu flows own
            // its on/off cycle. Only disable our locally-owned actions.
            _middleClickAction?.Disable();
            foreach (var (action, _) in _spellBindings)
                action?.Disable();
        }

        private void OnDestroy()
        {
            _middleClickAction?.Dispose();
            foreach (var (action, _) in _spellBindings)
                action?.Dispose();
            _spellBindings.Clear();
        }
    }
}
