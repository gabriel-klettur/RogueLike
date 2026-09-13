using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Off-screen live preview of ONE entity's animation, for the Entities editor's
    /// Animation panel.
    ///
    /// <para>Before this existed, the only way to watch a monster animate was to spawn it and
    /// play: the Spells editor's preview clones the LIVE PLAYER's animator, so it can show a
    /// player's cast pose and nothing else. Everything a `MonsterDefinition` says about its own
    /// animation — eight states, the attack and cast rotations, per-state pacing, loadouts —
    /// was invisible outside the Inspector's raw sprite lists.</para>
    ///
    /// <para>Three decisions are load-bearing and each has a reason recorded elsewhere in the
    /// project:</para>
    /// <list type="bullet">
    /// <item>The rig is built by <see cref="EntityAnimationBinder.ApplyLoadout"/>, the SAME
    /// path the game binds an entity with (a null loadout key is the base bind). A second
    /// implementation would answer differently the day a state has no art — and what a state
    /// without art shows is one of the questions this panel exists to answer.</item>
    /// <item>It reuses the <c>SpellPreview</c> Unity layer rather than claiming one. All 32
    /// physics layers are spent (see CLAUDE.md), and <see cref="GameEditorManager"/> opens
    /// editors exclusively, so the Spells editor's stage and this one can never render in the
    /// same frame. The two stages still sit at different Y so a stray object cannot be shared.</item>
    /// <item>Every renderer takes <see cref="ElementalSprites.SharedUnlitMaterial"/>: the
    /// preview camera is 30,000 units from the nearest <c>Light2D</c>, and a lit sprite with no
    /// light renders black — the trap <c>WorldGridBuilder.ApplyUnlitFallbackIfNeeded</c> exists
    /// for.</item>
    /// </list>
    /// </summary>
    public sealed partial class EntityAnimationPreviewService
    {
        // ── Constants ────────────────────────────────────────────────────────────

        internal const int   RT_SIZE     = 320;
        /// <summary>Clear of ParticlePreview (-10,000) and SpellPreview (-20,000).</summary>
        internal const float OFFSCREEN_Y = -30000f;
        internal const float CAMERA_Z    = -50f;

        private const float PADDING       = 0.55f;
        private const float ORTHO_MIN     = 0.60f;
        private const float ORTHO_MAX     = 60f;
        private const float ORTHO_DEFAULT = 3f;

        private const float ZOOM_MIN  = 0.25f;
        private const float ZOOM_MAX  = 6f;
        private const float ZOOM_STEP = 1.25f;

        /// <summary>Gap between the eight rigs of the grid view, as a multiple of the body's
        /// own width/height — so a dragon and a rat are both readable without a per-entity
        /// constant.</summary>
        private const float GRID_SPACING = 1.35f;

        /// <summary>The eight cells of the 3x3 grid, reading order, with the centre left out:
        /// the centre is where the body would have to be to face the camera, which no
        /// direction does. Mirrors the Python editor's <c>GRID_ORDER_3X3</c>.</summary>
        [Valkur.Core.SelfHealingStatic("Constant grid-order table; written once at class init and never mutated.")]
        internal static readonly DirectionalAnimator.Direction[] GridOrder =
        {
            DirectionalAnimator.Direction.NorthWest, DirectionalAnimator.Direction.North,
            DirectionalAnimator.Direction.NorthEast,
            DirectionalAnimator.Direction.West,      DirectionalAnimator.Direction.East,
            DirectionalAnimator.Direction.SouthWest, DirectionalAnimator.Direction.South,
            DirectionalAnimator.Direction.SouthEast
        };

        // ── State ────────────────────────────────────────────────────────────────

        private bool           _initialized;
        private bool           _open;
        private Camera         _camera;
        private RenderTexture  _rt;
        private GameObject     _stageRoot;

        private EntityAssetConfig _config;
        private string            _subjectLabel;
        private string            _loadoutKey;

        private DirectionalAnimator.AnimState _state     = DirectionalAnimator.AnimState.Idle;
        private DirectionalAnimator.Direction _direction = DirectionalAnimator.Direction.South;
        private int                           _variant   = -1;
        private bool                          _showAllDirections;

        private float  _userZoom = 1f;
        private Bounds _framing;
        private bool   _framingSeeded;
        private bool   _paused;
        private bool   _reversed;

        private readonly List<GameObject>         _rigs      = new List<GameObject>();
        private readonly List<DirectionalAnimator> _animators = new List<DirectionalAnimator>();
        private readonly List<SpriteRenderer>      _renderers = new List<SpriteRenderer>();

        /// <summary>Ghost of the previous frame, shown only while paused. A pivot that jumps
        /// between frames is what makes a walk cycle float, and it is invisible at speed and
        /// obvious against the frame before it.</summary>
        private SpriteRenderer _onionSkin;

        /// <summary>The line the feet are supposed to stand on: the rig's own y = 0, which is
        /// where every sprite's (0.5, 0) pivot puts the ground.</summary>
        private SpriteRenderer _groundLine;

        // ── Public surface ───────────────────────────────────────────────────────

        public bool IsOpen       => _open;
        public bool HasSubject   => _config != null && _animators.Count > 0;
        public string SubjectLabel => _subjectLabel;
        public float CurrentZoom => _userZoom;
        public bool ShowAllDirections => _showAllDirections;
        public DirectionalAnimator.AnimState CurrentState     => _state;
        public DirectionalAnimator.Direction CurrentDirection => _direction;
        public int CurrentVariant   => _variant;
        public string CurrentLoadout => _loadoutKey;

        /// <summary>The rig that answers questions about resolved art and pacing. In the grid
        /// view every rig carries the same sets and differs only in facing, so the first one
        /// speaks for all of them.</summary>
        public DirectionalAnimator Animator => _animators.Count > 0 ? _animators[0] : null;

        /// <summary>How many bodies are on the stage: one, or eight in the grid view. Public
        /// so a test can assert the grid without reaching into the stage hierarchy by name.</summary>
        public int RigCount => _rigs.Count;

        public RenderTexture GetPreviewTexture() => _initialized ? _rt : null;

        public void Initialize(Transform parent)
        {
            if (_initialized) return;

            int layer = ResolvePreviewLayer();

            _stageRoot = new GameObject("EntityAnimPreviewStage");
            _stageRoot.transform.SetParent(parent, false);
            _stageRoot.transform.position = new Vector3(0f, OFFSCREEN_Y, 0f);
            _stageRoot.layer = layer;

            var camGo = new GameObject("EntityAnimPreviewCamera");
            camGo.transform.SetParent(parent, false);
            camGo.transform.position = new Vector3(0f, OFFSCREEN_Y, CAMERA_Z);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic     = true;
            _camera.orthographicSize = ORTHO_DEFAULT;
            _camera.cullingMask      = 1 << layer;
            _camera.clearFlags       = CameraClearFlags.SolidColor;
            // Mid-tone, not the near-black every other editor panel uses: the six Dark twins
            // are tinted to a BLACK silhouette on purpose, and on a 0.07 ground they were a
            // shape you could only find by its outline. Measured on the first rendered frame —
            // no structural probe can see this, which is why the capture is the test.
            _camera.backgroundColor  = new Color(0.22f, 0.23f, 0.27f, 1f);
            _camera.nearClipPlane    = 0.1f;
            _camera.farClipPlane     = 200f;
            _camera.enabled          = false;

            var urp = camGo.GetComponent<UniversalAdditionalCameraData>()
                   ?? camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderType    = CameraRenderType.Base;
            urp.renderShadows = false;
            urp.antialiasing  = AntialiasingMode.None;

            _rt = new RenderTexture(RT_SIZE, RT_SIZE, 16, RenderTextureFormat.ARGB32)
            {
                name = "EntityAnimPreview_RT"
            };
            _rt.Create();
            _camera.targetTexture = _rt;

            _initialized = true;
        }

        public void Open()
        {
            if (!_initialized) return;
            _open = true;
            if (_camera != null) _camera.enabled = true;
            ResetFraming();
        }

        public void Close()
        {
            if (!_initialized) return;
            _open = false;
            if (_camera != null) _camera.enabled = false;
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            ClearRigs();
            // The camera lets go of the texture FIRST: releasing a RenderTexture a live camera
            // still names as its target logs "Releasing render texture that is set as
            // Camera.targetTexture!" into a console this project requires to be clean.
            if (_camera != null) _camera.targetTexture = null;
            if (_rt != null) { _rt.Release(); SafeDestroy.Of(_rt); _rt = null; }
            if (_camera != null) { SafeDestroy.Of(_camera.gameObject); _camera = null; }
            if (_stageRoot != null) { SafeDestroy.Of(_stageRoot); _stageRoot = null; }
            _config      = null;
            _subjectLabel = null;
            _open        = false;
            _initialized = false;
        }

        /// <summary>
        /// Points the stage at an entity. Passing null empties it, which is what the panel
        /// shows when the picker selection is cleared.
        ///
        /// <para>Re-staging the SAME config keeps the variant and the loadout. That is not a
        /// convenience: every pacing edit re-binds the rig so the change can be seen on the
        /// animation being watched, and a reset there meant the first edit silently dropped the
        /// selection — measured, the Hold toggle then wrote nothing at all, because by the time
        /// it ran the selected variant was -1 and its own guard refused. A DIFFERENT entity
        /// still resets both: a variant index belongs to one entity's table and means something
        /// else in the next one's.</para>
        /// </summary>
        public void SetSubject(EntityAssetConfig config, string label)
        {
            bool sameSubject = config != null && ReferenceEquals(config, _config);

            _config       = config;
            _subjectLabel = label;
            if (!sameSubject)
            {
                _loadoutKey = null;
                _variant    = -1;
            }
            RebuildRigs();
        }

        public void SetState(DirectionalAnimator.AnimState state)
        {
            if (_state == state) return;
            _state = state;
            // A variant index belongs to ONE state — attack #2 and cast #2 are unrelated
            // animations — so changing state drops the selection rather than carrying a number
            // across into a table where it means something else.
            _variant = -1;
            ApplyPose(restartFraming: true);
        }

        public void SetDirection(DirectionalAnimator.Direction direction)
        {
            _direction = direction;
            _showAllDirections = false;
            RebuildRigs();
        }

        public void SetShowAllDirections(bool show)
        {
            if (_showAllDirections == show) return;
            _showAllDirections = show;
            RebuildRigs();
        }

        /// <summary>-1 selects the state's base set; any other index selects that variant.</summary>
        public void SetVariant(int variant)
        {
            _variant = variant;
            ApplyPose(restartFraming: false);
        }

        /// <summary>A null or empty key re-binds the base art.</summary>
        public void SetLoadout(string loadoutKey)
        {
            _loadoutKey = string.IsNullOrEmpty(loadoutKey) ? null : loadoutKey;
            _variant    = -1;
            RebuildRigs();
        }

        public void ZoomIn()  => SetZoom(_userZoom * ZOOM_STEP);
        public void ZoomOut() => SetZoom(_userZoom / ZOOM_STEP);
        public void ZoomBy(float steps)
        {
            if (Mathf.Abs(steps) < 0.0001f) return;
            SetZoom(_userZoom * Mathf.Pow(ZOOM_STEP, steps));
        }

        public void SetZoom(float zoom) => _userZoom = Mathf.Clamp(zoom, ZOOM_MIN, ZOOM_MAX);

        /// <summary>How many alternative animations the CURRENT state carries; 0 means one.</summary>
        public int VariantCount(DirectionalAnimator.AnimState state)
            => Animator != null ? Animator.VariantCount(state) : 0;

        /// <summary>
        /// What to call variant <paramref name="index"/> of <paramref name="state"/>: its
        /// authored key, its reservation, or its index when the asset named neither.
        /// </summary>
        public string DescribeVariant(DirectionalAnimator.AnimState state, int index)
        {
            var animator = Animator;
            if (animator == null) return index.ToString();

            string label = animator.VariantLabel(state, index);
            if (string.IsNullOrEmpty(label)) label = $"#{index}";

            var reserved = animator.ReservedSpellKeys(state, index);
            if (reserved != null && reserved.Count > 0)
                label += $"  [{string.Join(", ", reserved)}]";
            return label;
        }

        // ── Transport ────────────────────────────────────────────────────────────

        public bool IsPaused   => _paused;
        public bool IsReversed => _reversed;

        /// <summary>Frame index on screen, and how many frames this pose has. Both come from
        /// the rig, so they describe what is being PLAYED rather than what the asset lists.</summary>
        public int DisplayedFrame => Animator != null ? Animator.DisplayedFrameIndex : 0;
        public int FrameCount     => Animator != null ? Animator.CurrentFrameCount : 0;

        /// <summary>The frames of the pose on screen, for the strip to draw. Null when the
        /// stage is empty.</summary>
        public Sprite[] CurrentFrames
        {
            get
            {
                var animator = Animator;
                if (animator == null) return null;
                var dir = _showAllDirections ? DirectionalAnimator.Direction.South : _direction;
                return animator.FramesFor(_state, dir, _variant);
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            for (int i = 0; i < _animators.Count; i++)
            {
                if (_animators[i] != null) _animators[i].Paused = paused;
            }
            UpdateGuides();
        }

        public void TogglePaused() => SetPaused(!_paused);

        /// <summary>
        /// Steps every rig by the same delta. Stepping pauses first: a "next frame" button that
        /// left the clock running would show the frame it asked for and then lose it on the next
        /// tick, which reads as the button doing nothing.
        /// </summary>
        public void StepFrame(int delta)
        {
            if (!_paused) SetPaused(true);
            for (int i = 0; i < _animators.Count; i++)
            {
                if (_animators[i] != null) _animators[i].StepFrame(delta);
            }
            UpdateGuides();
        }

        /// <summary>Jumps every rig to the same frame index of its own direction.</summary>
        public void ShowFrame(int index)
        {
            if (!_paused) SetPaused(true);
            for (int i = 0; i < _animators.Count; i++)
            {
                if (_animators[i] != null) _animators[i].ShowFrame(index);
            }
            UpdateGuides();
        }

        /// <summary>
        /// Plays the pose backwards. A real playback mode rather than a strip trick, because
        /// the game itself uses one — the dwarf's sheathe IS his draw reversed — so a preview
        /// that faked it would not be showing what the player sees.
        /// </summary>
        public void SetReversed(bool reversed)
        {
            if (_reversed == reversed) return;
            _reversed = reversed;
            ApplyPose(restartFraming: false);
        }

        // ── Timing readout ───────────────────────────────────────────────────────
        //
        // Every number here is asked of the RIG rather than recomputed from the asset. Three
        // multipliers decide a frame's duration — the entity's, the state's and the variant's
        // — and a reader that rebuilt the product would eventually forget one and disagree
        // with the clock it claims to describe.

        /// <summary>Seconds one frame is held, with all three multipliers applied.</summary>
        public float FrameSeconds
            => Animator != null ? Animator.FrameIntervalFor(_state, _variant) : 0f;

        /// <summary>Seconds the whole pose takes — what sizes an attack's damage window.</summary>
        public float StateSeconds
            => Animator != null ? Animator.GetStateLength(_state, _variant) : 0f;

        public float EntitySpeedMultiplier
            => Animator != null ? Animator.AnimationSpeedMultiplier : 1f;

        public float StateSpeedMultiplier
            => Animator != null ? Animator.StateSpeedOf(_state) : 1f;

        public float VariantSpeedMultiplier
            => Animator != null ? Animator.PacingOf(_state, _variant).SpeedMultiplier : 1f;

        public bool HoldsLastFrame
            => Animator != null && Animator.PacingOf(_state, _variant).HoldLastFrame;

        /// <summary>
        /// Which of <c>AdvanceFrame</c>'s rules governs the pose on screen. It is the half of
        /// the timing nobody can infer from the numbers: a walk of eight frames at 0.15 s does
        /// NOT take 1.2 s, because frame 0 is skipped.
        /// </summary>
        public string DescribePlaybackRule()
        {
            if (Animator == null) return "";
            if (FrameCount <= 1) return "single frame — a pose, not a cycle";

            switch (_state)
            {
                case DirectionalAnimator.AnimState.Idle:
                    return "holds frame 0 for 1 s, then loops 1..n";
                case DirectionalAnimator.AnimState.Walk:
                case DirectionalAnimator.AnimState.Chase:
                    return "skips frame 0 (the standing pose), then loops";
                case DirectionalAnimator.AnimState.Death:
                    return "plays once and holds the last frame";
            }
            return HoldsLastFrame ? "plays once and holds the last frame" : "loops every frame";
        }

        /// <summary>
        /// What the stage is REALLY showing when the selected state has no art of its own:
        /// <see cref="EntityAnimationBinder"/> falls walk back to idle, chase to walk, cast to
        /// walk, attack to cast, and damage and death to idle — so an entity missing a state
        /// shows another POSE rather than nothing, and without this line nobody can tell the
        /// difference between art that exists and art that fell back.
        /// Empty when the state carries its own frames.
        /// </summary>
        public string DescribeFallback()
        {
            var a = Animator;
            if (a == null || _variant >= 0) return "";   // a variant is art by definition

            switch (_state)
            {
                case DirectionalAnimator.AnimState.Walk:
                    return SameArt(a.WalkSprites, a.IdleSprites) ? "no walk art — showing idle" : "";
                case DirectionalAnimator.AnimState.Chase:
                    if (SameArt(a.ChaseSprites, a.WalkSprites))
                        return SameArt(a.WalkSprites, a.IdleSprites)
                            ? "no chase or walk art — showing idle"
                            : "no chase art — showing walk";
                    return "";
                case DirectionalAnimator.AnimState.Cast:
                    return SameArt(a.CastSprites, a.WalkSprites) ? "no cast art — showing walk" : "";
                case DirectionalAnimator.AnimState.Attack:
                    return SameArt(a.AttackSprites, a.CastSprites) ? "no attack art — showing cast" : "";
                case DirectionalAnimator.AnimState.Damage:
                    return SameArt(a.DamageSprites, a.IdleSprites) ? "no damage art — showing idle" : "";
                case DirectionalAnimator.AnimState.Death:
                    return SameArt(a.DeathSprites, a.IdleSprites) ? "no death art — showing idle" : "";
                case DirectionalAnimator.AnimState.Recover:
                    // Recover is the one state the binder leaves EMPTY rather than filling, so
                    // the animator itself falls it back to idle at read time.
                    return HasNoArt(a.RecoverSprites) ? "no recover art — showing idle" : "";
            }
            return "";
        }

        /// <summary>
        /// True when two sets are the SAME arrays — which is what the binder's fallback leaves
        /// behind, since it assigns one set to the other rather than copying frames.
        /// </summary>
        private static bool SameArt(DirectionalAnimator.DirectionalSpriteSet a,
                                    DirectionalAnimator.DirectionalSpriteSet b)
            => ReferenceEquals(a.south, b.south) && ReferenceEquals(a.east, b.east);

        private static bool HasNoArt(DirectionalAnimator.DirectionalSpriteSet set)
        {
            for (int i = 0; i < 8; i++)
            {
                var frames = set.GetFrames((DirectionalAnimator.Direction)i);
                if (frames != null && frames.Length > 0) return false;
            }
            return true;
        }

        /// <summary>Drive the camera. Call from the editor's Update while the panel is open.</summary>
        public void Tick()
        {
            if (!_initialized || !_open) return;
            UpdateFraming();
            UpdateGuides();
            // Re-placed per tick because the FRAME moves under it: watching the mark stay on
            // the mouth, or drift off it, as the animation plays is the whole reason the
            // muzzle is authored here rather than in the Inspector.
            UpdateMuzzleMarker();
        }

        // ── Rig construction ─────────────────────────────────────────────────────

        private void RebuildRigs()
        {
            ClearRigs();
            if (!_initialized || _config == null) return;

            if (_showAllDirections)
            {
                for (int i = 0; i < GridOrder.Length; i++) BuildRig(GridOrder[i]);
                LayoutGrid();
            }
            else
            {
                BuildRig(_direction);
            }

            BuildGuides();
            ApplyPose(restartFraming: true);
        }

        /// <summary>
        /// The two things a still frame cannot say on its own: where the ground is, and where
        /// the previous frame was. Both hang off the FIRST rig, so the grid view gets them on
        /// one body rather than eight — the question they answer ("does the pivot jump?") is
        /// about one animation, not about the facings.
        /// </summary>
        private void BuildGuides()
        {
            _onionSkin  = null;
            _groundLine = null;
            if (_rigs.Count == 0) return;

            var host  = _rigs[0].transform;
            int layer = host.gameObject.layer;

            var onionGo = new GameObject("OnionSkin");
            onionGo.transform.SetParent(host, false);
            onionGo.layer = layer;
            _onionSkin = onionGo.AddComponent<SpriteRenderer>();
            _onionSkin.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _onionSkin.sortingOrder     = -1;   // behind the body it is a ghost of
            _onionSkin.sharedMaterial   = ElementalSprites.SharedUnlitMaterial;
            _onionSkin.color            = new Color(1f, 1f, 1f, 0.30f);
            _onionSkin.enabled          = false;

            var lineGo = new GameObject("GroundLine");
            lineGo.transform.SetParent(host, false);
            lineGo.layer = layer;
            _groundLine = lineGo.AddComponent<SpriteRenderer>();
            _groundLine.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _groundLine.sortingOrder     = -2;
            _groundLine.sharedMaterial   = ElementalSprites.SharedUnlitMaterial;
            _groundLine.sprite           = GroundLineSprite();
            _groundLine.color            = new Color(0.45f, 0.75f, 1f, 0.35f);
            _groundLine.drawMode         = SpriteDrawMode.Sliced;
        }

        private static Sprite _groundLineSprite;

        /// <summary>
        /// Domain Reload is OFF in this project, so a static that survives a Play-mode restart
        /// holds a DESTROYED sprite and every later ground line renders nothing.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _groundLineSprite = null;
        }

        /// <summary>A one-texel white sprite, sized by the renderer rather than by the art.</summary>
        private static Sprite GroundLineSprite()
        {
            if (_groundLineSprite != null) return _groundLineSprite;
            var tex = new Texture2D(1, 1) { name = "EntityAnimGroundLine" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _groundLineSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f),
                                              new Vector2(0.5f, 0.5f), 1f, 0u,
                                              SpriteMeshType.FullRect, Vector4.zero);
            _groundLineSprite.name = "EntityAnimGroundLine";
            return _groundLineSprite;
        }

        /// <summary>
        /// Sizes the ground line to the body and shows the ghost only while paused: at speed
        /// the ghost is another moving body and reads as a rendering fault.
        /// </summary>
        private void UpdateGuides()
        {
            if (_groundLine != null)
            {
                Vector2 body = MeasureBody();
                _groundLine.size = new Vector2(Mathf.Max(0.5f, body.x * 1.6f), 0.03f);
                _groundLine.transform.localPosition = Vector3.zero;
            }

            if (_onionSkin == null) return;

            var animator = Animator;
            if (!_paused || animator == null)
            {
                _onionSkin.enabled = false;
                return;
            }

            var frames = CurrentFrames;
            if (frames == null || frames.Length < 2) { _onionSkin.enabled = false; return; }

            int previous = animator.DisplayedFrameIndex - 1;
            if (previous < 0) previous = frames.Length - 1;
            _onionSkin.sprite  = frames[previous];
            _onionSkin.enabled = frames[previous] != null;
        }

        private void BuildRig(DirectionalAnimator.Direction direction)
        {
            int layer = _stageRoot != null ? _stageRoot.layer : ResolvePreviewLayer();

            var go = new GameObject($"Rig_{direction}");
            go.transform.SetParent(_stageRoot.transform, false);
            go.layer = layer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder     = 0;
            ElementalSprites.EnsureAll();
            sr.sharedMaterial   = ElementalSprites.SharedUnlitMaterial;

            // The game's own bind. ApplyLoadout with a null key IS the base bind — it resolves
            // the fallback chain, the variants, the pacing and the entity scale exactly as
            // EntitySetup does at spawn.
            if (!EntityAnimationBinder.ApplyLoadout(go, _config, _loadoutKey))
            {
                // A config whose idle resolved to nothing binds no art at all, by design
                // (EntityAnimationBinder returns false on an empty idle). Keep the empty rig so
                // the panel can say so rather than silently showing the previous entity.
                SafeDestroy.Of(go);
                return;
            }

            var animator = go.GetComponent<DirectionalAnimator>();
            _rigs.Add(go);
            _animators.Add(animator);
            _renderers.Add(sr);

            if (animator != null) animator.SetState(_state, direction);
        }

        /// <summary>
        /// Spreads the eight rigs over a 3x3 grid, sized from the body itself so one constant
        /// serves a 1.2-unit dwarf and an 8-unit dragon.
        /// </summary>
        private void LayoutGrid()
        {
            Vector2 cell = MeasureBody();
            float dx = Mathf.Max(0.4f, cell.x * GRID_SPACING);
            float dy = Mathf.Max(0.4f, cell.y * GRID_SPACING);

            for (int i = 0; i < _rigs.Count && i < GridOrder.Length; i++)
            {
                // GridOrder is reading order with the centre skipped, so cell 4 is the hole.
                int slot = i < 4 ? i : i + 1;
                int col  = slot % 3;
                int row  = slot / 3;
                _rigs[i].transform.localPosition = new Vector3((col - 1) * dx, (1 - row) * dy, 0f);
            }
        }

        /// <summary>The drawn size of one rig, taken from its renderer. Falls back to a
        /// one-unit body before anything has been drawn.</summary>
        private Vector2 MeasureBody()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var sr = _renderers[i];
                if (sr == null || sr.sprite == null) continue;
                Vector3 size = sr.bounds.size;
                if (size.x > 0.0001f && size.y > 0.0001f) return new Vector2(size.x, size.y);
            }
            return Vector2.one;
        }

        /// <summary>
        /// Re-asserts the state, direction and variant on every rig. <c>RestartCurrentState</c>
        /// follows the <c>SetState</c> call for the reason the Spells preview records: SetState
        /// early-returns when nothing changed, and on a freshly built rig the state already
        /// reads Idle — so an Idle preview would change nothing, never advance a frame, and
        /// render whatever the bind happened to seed.
        /// </summary>
        private void ApplyPose(bool restartFraming)
        {
            for (int i = 0; i < _animators.Count; i++)
            {
                var animator = _animators[i];
                if (animator == null) continue;

                var dir = _showAllDirections && i < GridOrder.Length ? GridOrder[i] : _direction;
                animator.SetState(_state, dir, _variant, _reversed);
                animator.RestartCurrentState();
                animator.Paused = _paused;
            }
            if (restartFraming) ResetFraming();
        }

        private void ClearRigs()
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                if (_rigs[i] != null) SafeDestroy.Of(_rigs[i]);
            }
            _rigs.Clear();
            _animators.Clear();
            _renderers.Clear();
        }

        // ── Framing ──────────────────────────────────────────────────────────────

        private void ResetFraming()
        {
            _framingSeeded = false;
        }

        /// <summary>
        /// Frames whatever is on the stage. The bounds only ever GROW within one pose: each
        /// animation frame is trimmed to its own alpha, so re-fitting to the current frame
        /// makes the camera breathe with the walk cycle — the same reason
        /// <c>WorldBarRig</c> measures the body on demand rather than every frame.
        /// </summary>
        private void UpdateFraming()
        {
            if (_camera == null) return;

            bool any = false;
            Bounds b = default;
            for (int i = 0; i < _renderers.Count; i++)
            {
                var sr = _renderers[i];
                if (sr == null || sr.sprite == null) continue;
                if (!any) { b = sr.bounds; any = true; }
                else       b.Encapsulate(sr.bounds);
            }

            if (!any)
            {
                _camera.orthographicSize = ORTHO_DEFAULT / _userZoom;
                return;
            }

            if (!_framingSeeded) { _framing = b; _framingSeeded = true; }
            else                  _framing.Encapsulate(b);

            float aspect  = _camera.aspect > 0.0001f ? _camera.aspect : 1f;
            float halfH   = _framing.extents.y + PADDING;
            float halfW   = (_framing.extents.x + PADDING) / aspect;
            float ortho   = Mathf.Clamp(Mathf.Max(halfH, halfW) / _userZoom, ORTHO_MIN, ORTHO_MAX);

            // renderer.bounds is world space and the stage sits at OFFSCREEN_Y, so the centre
            // is already where the camera has to look.
            _camera.orthographicSize = ortho;
            _camera.transform.position = new Vector3(_framing.center.x, _framing.center.y, CAMERA_Z);
        }

        // ── Layer ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The <c>SpellPreview</c> layer, shared with the Spells editor on purpose — every one
        /// of Unity's 32 physics layers is spent, and two editors cannot be open at once.
        /// </summary>
        internal static int ResolvePreviewLayer()
        {
            int idx = LayerMask.NameToLayer("SpellPreview");
            if (idx < 0)
            {
                Debug.LogWarning("[EntityAnimationPreview] Layer 'SpellPreview' not found in " +
                                 "TagManager. Falling back to Default (layer 0) — the preview rig " +
                                 "may appear in the game world.");
                return 0;
            }
            return idx;
        }
    }
}
