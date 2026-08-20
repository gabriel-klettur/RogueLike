using Cinemachine;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data.Feel;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// Everything the camera does beyond existing: smooth follow, look-ahead, shake, kick.
    ///
    /// It writes exactly one Transform per frame — a proxy the Cinemachine vcam follows —
    /// and never touches <c>Camera.main</c>, never touches a lens, and never calls
    /// <c>DetachFollow</c>. That is possible because the shipped rig forces all three
    /// transposer dampings to zero, which makes the transposer an exact 1:1 copy of its
    /// follow target: verified in Play mode as <c>camera == follow + (0,0,-10)</c> to within
    /// a fifth of a screen pixel. Moving the proxy IS moving the camera.
    ///
    /// The alternative — writing the camera transform directly, which is what the old
    /// <c>CameraShake</c> did — has to guess whether it runs before or after the brain, and
    /// guessed wrong: its subtract-then-re-add restore removed an offset the brain had
    /// already erased, so the screen received the difference of two independent random
    /// vectors instead of the authored shake.
    ///
    /// Lens writes are deliberately absent, and that is a design constraint rather than an
    /// omission. <c>CameraPixelSnap</c> derives its lattice from the live orthographic size,
    /// which <c>CameraSetup</c> keeps on a ladder where one art texel is an integer number of
    /// screen pixels. A zoom punch of a few percent lands between rungs and makes every tile
    /// on screen crawl. Weight is carried by kick amplitude, shake frequency, trauma decay,
    /// hit-stop and lead freeze instead.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed partial class CameraFeelDirector : SingletonMonoBehaviour<CameraFeelDirector>
    {
        private const string PROXY_NAME = "[Camera Target]";

        /// <summary>Fallback used before the render camera resolves.</summary>
        private const float FALLBACK_WORLD_UNITS_PER_PIXEL = 0.01f;

        private CameraFeelProfile _profile;
        private CameraFeelState _state;

        private Transform _proxy;
        private Camera _renderCam;
        private CameraSetup _cameraSetup;
        private Transform _playerTransform;
        private GameObject _playerGo;
        private PlayerController _playerController;
        private Health _playerHealth;
        private ComboCounter _playerCombo;

        private Vector2 _appliedOffset;
        private Vector2 _appliedOffsetPreviousFrame;
        private Vector2 _lastPlayerPosition;
        private int _lastTickFrame = -1;
        private bool _warnedProxyStolen;
        // False until the proxy has been installed as the follow target once. The
        // first install is the normal boot handover (CameraSetup.Update lazily
        // assigns the player before this director exists), NOT a theft.
        private bool _proxyEverInstalled;
        private bool _warnedNoCameraSetup;
        private float _startedAt;

        /// <summary>The lead, shake and kick applied this frame, in world units.</summary>
        public Vector2 AppliedOffset => _appliedOffset;

        /// <summary>The transform the Cinemachine vcam follows.</summary>
        public Transform FollowProxy => _proxy;

        /// <summary>
        /// A read-only snapshot of what the solver is doing right now, for the Camera Editor.
        ///
        /// Tuning a camera blind is guesswork: a cue that fires and is rounded away by the
        /// pixel snap looks identical to one that never fired, and a rate limit swallowing
        /// repeats looks identical to a broken event. Seeing trauma, lead and kick as numbers
        /// is what separates tuning from poking.
        /// </summary>
        internal CameraFeelLive Live => new CameraFeelLive
        {
            Trauma = _state.Trauma,
            TraumaDecay = _state.TraumaDecay,
            ShakeFrequencyHz = _state.ShakeFrequencyHz,
            Lead = _state.Lead,
            Kick = _state.Kick,
            Applied = _appliedOffset,
            FollowLag = _playerTransform != null
                ? _state.Follow - (Vector2)_playerTransform.position
                : Vector2.zero,
            LeadFreezeRemaining = _state.LeadFreezeRemaining,
            TraumaSpentThisSecond = _state.TraumaSpentThisSecond,
            WorldUnitsPerPixel = WorldUnitsPerPixel(),
            ProxyIsFollowTarget = _cameraSetup != null && _cameraSetup.GetFollowTarget() == _proxy,
            Suppressed = Suppressed,
        };

        /// <summary>
        /// True while a runtime editor is open, the game is genuinely paused, or the rig is
        /// not ready. While suppressed the director writes nothing at all — not the proxy,
        /// not the follow target.
        /// </summary>
        public bool Suppressed { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _profile = Resources.Load<CameraFeelProfile>("CameraFeelProfile");
            if (_profile == null)
            {
                // A missing asset degrades to the tuned numbers rather than to a dead system.
                _profile = CameraFeelProfile.CreateDefault();
                Debug.LogWarning("[CameraFeel] No CameraFeelProfile in Resources — running on " +
                                 "the code defaults.");
            }

            _state = CameraFeelState.Create(Random.Range(0f, 1000f), Random.Range(2000f, 3000f),
                                            _profile.DefaultTraumaDecay);
            _startedAt = Time.realtimeSinceStartup;

            var proxyGo = new GameObject(PROXY_NAME);
            proxyGo.transform.SetParent(transform, worldPositionStays: false);
            _proxy = proxyGo.transform;
        }

        private void Start() => ConfigureBrain();

        /// <summary>
        /// Four brain fields the shipped scene gets wrong for this design.
        ///
        /// <c>SmartUpdate</c> picks the clock from how the follow target moves, over a
        /// thirty-frame heuristic; during hit-stop that can settle on the physics clock and
        /// evaluate the camera at three hertz. <c>m_IgnoreTimeScale</c> false does the same to
        /// any delta Cinemachine derives. And the scene ships a two-second EaseInOut default
        /// blend, which would be catastrophic if the Editor-only compatibility vcam ever won
        /// the priority election.
        /// </summary>
        private void ConfigureBrain()
        {
            Camera cam = ResolveRenderCamera();
            if (cam == null) return;

            var brain = cam.GetComponent<CinemachineBrain>();
            if (brain == null) return;

            brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.LateUpdate;
            brain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.LateUpdate;
            brain.m_IgnoreTimeScale = true;
            brain.m_DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Style.Cut, 0f);
        }

        private void LateUpdate()
        {
            if (Time.frameCount == _lastTickFrame) return;
            _lastTickFrame = Time.frameCount;

            if (!ResolveRig()) { EnterSuppressed(); return; }

            // The same predicate CameraSetup.Update early-returns on. Polled rather than
            // subscribed: GameEditorManager.Unregister clears the active editor without
            // firing its state-changed event, which would strand the gate open forever.
            bool editorOpen = GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive;
            bool paused = Time.timeScale <= 0.0001f;
            if (editorOpen || paused) { EnterSuppressed(); return; }

            Suppressed = false;

            // Never Time.deltaTime. Hit-stop drops the scaled clock to six percent, and the
            // shake continuing at full speed through the freeze is what makes the freeze read
            // as impact rather than as a dropped frame.
            float dt = Mathf.Min(Time.unscaledDeltaTime, _profile.MaxStepSeconds);
            Vector2 playerPos = _playerTransform.position;

            if (!_state.FollowInitialised)
            {
                _state.Follow = playerPos;
                _state.FollowInitialised = true;
                _lastPlayerPosition = playerPos;
            }
            else if (CameraFeelMath.IsTeleport(_lastPlayerPosition, playerPos, _profile.TeleportThresholdWu))
            {
                // A warp is not movement. Chasing one drags the whole transient layer across
                // the map, and at least one teleport path writes the transform and tells
                // nobody, so this detects it rather than trusting a notification.
                _state.ClearTransients(_profile.DefaultTraumaDecay);
                _state.Follow = playerPos;
                _state.FollowVelocity = Vector2.zero;
            }
            _lastPlayerPosition = playerPos;

            TickDeferredCues(dt);
            TickFollow(playerPos, dt);
            TickLead(playerPos, dt);
            Vector2 shake = TickNoise(dt);
            TickKick(dt);

            _appliedOffsetPreviousFrame = _appliedOffset;
            _appliedOffset = _state.Lead + shake + _state.Kick;

            _proxy.position = new Vector3(_state.Follow.x + _appliedOffset.x,
                                          _state.Follow.y + _appliedOffset.y,
                                          _playerTransform.position.z);

            EnsureProxyIsTheFollowTarget();
        }

        private void EnterSuppressed()
        {
            Suppressed = true;
            _state.ClearTransients(_profile != null ? _profile.DefaultTraumaDecay : 1.8f);
            _state.FollowInitialised = false;
            _appliedOffset = Vector2.zero;
            _appliedOffsetPreviousFrame = Vector2.zero;
        }

        /// <summary>
        /// Smooth follow. The camera springs after the player instead of being welded to
        /// them, with a hard leash so a fast player can never outrun the frame.
        /// </summary>
        private void TickFollow(Vector2 playerPos, float dt)
        {
            if (_profile.FollowOmega <= 0f)
            {
                _state.Follow = playerPos;
                _state.FollowVelocity = Vector2.zero;
                return;
            }

            CameraFeelMath.SpringStep(ref _state.Follow, ref _state.FollowVelocity, playerPos,
                                      _profile.FollowOmega, 1f, dt);

            Vector2 lag = _state.Follow - playerPos;
            float lagDistance = lag.magnitude;

            // A spring's tail is an infinite series of ever-smaller steps. CameraPixelSnap
            // rounds the result to the pixel lattice, so those last steps do not read as
            // arriving slowly — they read as the frame twitching between two rows. Landing
            // exactly is what makes a standing camera actually still.
            if (lagDistance < _profile.FollowSettlePixels * WorldUnitsPerPixel())
            {
                _state.Follow = playerPos;
                _state.FollowVelocity = Vector2.zero;
                return;
            }

            if (lagDistance > _profile.MaxFollowLagWu)
                _state.Follow = playerPos + lag / lagDistance * _profile.MaxFollowLagWu;
        }

        private void TickLead(Vector2 playerPos, float dt)
        {
            // The camera leads the character's movement. The cursor is not consulted at all
            // unless the profile explicitly asks for aim lead, which it ships not doing — a
            // frame that chases the pointer reads as arguing with the player rather than
            // anticipating them.
            Vector2 aim = Vector2.zero;
            bool wantsAimLead = _profile.AimLeadIdleWu > 0f || _profile.AimLeadMovingWu > 0f;
            if (wantsAimLead &&
                MouseInputManager.TryGetWorldMousePosition(out Vector2 mouseWorld, _renderCam,
                                                           requireInView: true,
                                                           requireApplicationFocus: false))
            {
                // De-leaded: the player's facing is read back off this same camera, so feeding
                // the raw cursor vector into the lead closes a loop whose fixed point is
                // whatever direction noise last pushed it to.
                aim = CameraFeelMath.ResolveAimVector(mouseWorld, playerPos,
                                                      _appliedOffsetPreviousFrame,
                                                      _profile.AimDeadzoneWu);
            }

            Vector2 moveInput = _playerController != null ? _playerController.MoveInput : Vector2.zero;
            Vector2 target = CameraFeelMath.ResolveLeadTarget(
                moveInput, aim, _profile.MoveLeadWu, _profile.AimLeadIdleWu,
                _profile.AimLeadMovingWu, _profile.MaxLeadWu) * _state.LeadScale;

            if (_state.LeadOverrideRemaining > 0f)
            {
                _state.LeadOverrideRemaining -= dt;
                target = _state.LeadOverride;
            }
            else if (_state.LeadFreezeRemaining > 0f)
            {
                _state.LeadFreezeRemaining -= dt;
                target = _state.Lead;   // hold where it is; do not snap to zero
            }

            target = CameraFeelMath.ApplyLeadDeadzone(_state.Lead, target,
                                                      _profile.LeadDeadzonePixels,
                                                      WorldUnitsPerPixel());

            float omega = _deathFlowActive ? _profile.LeadOmegaHeavy : _profile.LeadOmega;
            if (_state.LeadOverrideRemaining > 0f) omega = _profile.LeadOmega * 2.6f;

            CameraFeelMath.SpringStep(ref _state.Lead, ref _state.LeadVelocity, target,
                                      omega, 1f, dt);
        }

        private Vector2 TickNoise(float dt)
        {
            _state.NoiseTime += dt;
            _state.Trauma = CameraFeelMath.DecayTrauma(_state.Trauma, _state.TraumaDecay, dt);
            if (_state.Trauma <= 0f) _state.TraumaDecay = _profile.DefaultTraumaDecay;

            float amplitude = CameraFeelMath.TraumaToAmplitude(_state.Trauma, _profile.MaxShakeWu);
            if (amplitude <= 0f) return Vector2.zero;

            return CameraFeelMath.ShakeSample(_state.SeedX, _state.SeedY, _state.NoiseTime,
                                              _state.ShakeFrequencyHz,
                                              _profile.NoiseNormalisation) * amplitude;
        }

        private void TickKick(float dt)
            => CameraFeelMath.SpringStep(ref _state.Kick, ref _state.KickVelocity, Vector2.zero,
                                         _state.KickOmega, _state.KickZeta, dt);

        /// <summary>
        /// The proxy must remain the follow target every frame.
        ///
        /// <c>CameraSetup.SetTarget</c> writes the vcam's Follow but not its saved target, so
        /// an editor closing after the director installed the proxy restores the player — and
        /// because Follow is then non-null, the lazy re-acquire never fires and the whole
        /// feel layer is silently dead for the rest of the session.
        ///
        /// The very first call is always a mismatch: <c>CameraSetup.Update</c> lazily assigns
        /// <c>EntityRegistry.Player</c> to Follow long before this director's first
        /// <c>LateUpdate</c>, and this method is the only thing that ever installs the proxy.
        /// Warning on that handover fired "something reassigned the follow target" on every
        /// boot and trained the reader to ignore the one message that means a real conflict.
        /// </summary>
        private void EnsureProxyIsTheFollowTarget()
        {
            if (_cameraSetup.GetFollowTarget() == _proxy) return;

            _cameraSetup.SetTarget(_proxy);

            bool wasInitialInstall = !_proxyEverInstalled;
            _proxyEverInstalled = true;
            if (wasInitialInstall || _warnedProxyStolen) return;

            _warnedProxyStolen = true;
            Debug.LogWarning("[CameraFeel] Something reassigned the camera follow target; " +
                             "reinstalling the proxy. Investigate if this repeats.");
        }

        private bool ResolveRig()
        {
            if (_cameraSetup == null) _cameraSetup = CameraSetup.Instance;
            if (_cameraSetup == null)
            {
                if (!_warnedNoCameraSetup && Time.realtimeSinceStartup - _startedAt > 5f)
                {
                    _warnedNoCameraSetup = true;
                    Debug.LogWarning("[CameraFeel] No CameraSetup five seconds into play — the " +
                                     "camera feel layer is inert.");
                }
                return false;
            }

            if (_playerTransform == null)
            {
                _playerTransform = EntityRegistry.PlayerTransform;
                if (_playerTransform == null) return false;

                _playerGo = _playerTransform.gameObject;
                _playerController = _playerGo.GetComponent<PlayerController>();
                _playerHealth = _playerGo.GetComponent<Health>();
                _playerCombo = _playerGo.GetComponent<ComboCounter>();
            }

            return _proxy != null;
        }

        private Camera ResolveRenderCamera()
        {
            if (_renderCam != null) return _renderCam;

            _renderCam = Camera.main;
            if (_renderCam != null) return _renderCam;

            var tagged = GameObject.FindGameObjectWithTag("MainCamera");
            if (tagged != null) _renderCam = tagged.GetComponent<Camera>();
            return _renderCam;
        }

        /// <summary>
        /// The size of one screen pixel in world units — the quantum <c>CameraPixelSnap</c>
        /// rounds to, and therefore the smallest camera motion that can exist.
        /// </summary>
        private float WorldUnitsPerPixel()
        {
            Camera cam = ResolveRenderCamera();
            if (cam != null && cam.pixelHeight > 0)
                return (cam.orthographicSize * 2f) / cam.pixelHeight;

            if (_cameraSetup != null && Screen.height > 0)
                return (_cameraSetup.GetCurrentOrthographicSize() * 2f) / Screen.height;

            return FALLBACK_WORLD_UNITS_PER_PIXEL;
        }
    }
}
