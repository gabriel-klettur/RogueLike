using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Where, in the world, snow has actually settled — a single-channel world-space buffer
    /// that individual flakes stamp as they land, and that <c>Shaders/ValkurSnow.hlsl</c>
    /// samples by world position.
    ///
    /// This is the half that makes accumulation physical rather than graded. With one global
    /// scalar, every pixel in the world whitens by the same amount at the same moment, which
    /// no amount of tuning can make read as snow settling — there is no history in it, so
    /// there are no drifts, the wind cannot pile anything anywhere, a spot the fall has not
    /// reached yet is as white as one that has been under it for a minute, and thawing is a
    /// slider going down rather than patches shrinking. Stamping real landings gives all of
    /// that away for free, because it IS all the same fact: how much has landed HERE.
    ///
    /// The buffer follows the camera. It is not the whole world — that would be a texture per
    /// map slot and a persistence format — so it scrolls, resampled at an offset each time the
    /// camera has moved far enough, and ground scrolled in from outside comes in bare. Outside
    /// it the shader falls back to the global amount, so distant geometry is snowed rather
    /// than showing the buffer's rectangular edge.
    ///
    /// Owned by <see cref="WeatherManager"/>, which creates it as a child and ticks it.
    /// </summary>
    public sealed class SnowSplatMap : MonoBehaviour
    {
        public static SnowSplatMap Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // ── sizing ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Buffer resolution. 384 over <see cref="WorldSize"/> is 5.33 texels per world unit —
        /// one texel per three art pixels at the world's 16 PPU. Deliberately coarse: the map
        /// carries WHERE the snow is, and all the fine detail comes from the per-sprite alpha
        /// cap that reads it. A higher resolution would buy nothing visible and cost bandwidth
        /// on every scroll.
        /// </summary>
        private const int Resolution = 384;

        /// <summary>
        /// World units the buffer spans. Comfortably more than three shipped viewports wide
        /// (the game plays at 20 x 10), so ordinary walking never reaches an edge and the
        /// scroll runs rarely.
        /// </summary>
        private const float WorldSize = 72f;

        /// <summary>How far the camera may drift from the buffer's centre before it scrolls.</summary>
        private const float RecenterDistance = WorldSize * 0.16f;

        /// <summary>
        /// Radius of one landed flake's stamp, in world units. Roughly a third of a tile: big
        /// enough that a few hundred landings read as a continuous drift rather than as
        /// measles, small enough that the drift has a shape.
        /// </summary>
        private const float SplatRadius = 0.55f;

        /// <summary>
        /// Depth one landing contributes. Sized against the fall: at Heavy the snow layers land
        /// roughly 60 flakes/second, one full pass over a 20 x 10 viewport takes about 210
        /// stamps, and 0.04 puts full local cover about 90 seconds out — the same order as the
        /// global clock in <see cref="SnowAccumulation"/>, so the two ramp together instead of
        /// one gating the other.
        /// </summary>
        private const float SplatStrength = 0.04f;

        /// <summary>Seconds between scroll/melt passes. The melt is measured in minutes; this is plenty.</summary>
        private const float MaintenanceInterval = 0.12f;

        private static readonly int MapId       = Shader.PropertyToID("_ValkurSnowMap");
        private static readonly int MapRectId   = Shader.PropertyToID("_ValkurSnowMapRect");
        private static readonly int ScrollFadeId = Shader.PropertyToID("_SnowScrollFade");

        // ── state ────────────────────────────────────────────────────────────────────

        private RenderTexture _map;
        private RenderTexture _scratch;
        private Material      _material;
        private Mesh          _mesh;
        private CommandBuffer _commands;
        private Camera        _trackedCamera;

        private Vector2 _origin;            // world position of the buffer's bottom-left corner
        private float   _maintenanceTimer;
        private bool    _ready;
        private bool    _built;

        // Landing points waiting to be stamped, as (worldX, worldY, strength).
        private readonly List<Vector3> _pending = new List<Vector3>(512);

        // Hoisted mesh buffers — rebuilt in place every flush rather than reallocated. The
        // flush runs every frame it has anything to draw.
        private Vector3[] _verts   = new Vector3[0];
        private Vector2[] _uvs     = new Vector2[0];
        private Color[]   _colors  = new Color[0];
        private int[]     _indices = new int[0];

        /// <summary>True when the buffer exists and is being published to the shaders.</summary>
        public bool IsReady => _ready;

        /// <summary>The buffer's world-space rect, for tests and diagnostics.</summary>
        public Rect WorldRect => new Rect(_origin, new Vector2(WorldSize, WorldSize));

        /// <summary>Landings stamped since the last flush. Diagnostics only.</summary>
        public int PendingCount => _pending.Count;

        // ── lifecycle ────────────────────────────────────────────────────────────────

        private void Awake() => EnsureBuilt();

        /// <summary>
        /// Allocate the buffer, once.
        ///
        /// Public and idempotent for the same reason <c>WeatherEffect.EnsureBuilt</c> is: Unity
        /// does not call Awake on a component added in Edit Mode, and marking this
        /// <c>[ExecuteAlways]</c> to satisfy a test would put a RenderTexture and a per-frame
        /// blit into every editor session whether or not the game is running.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_ready || _built) return;
            _built = true;

            if (Instance != null && Instance != this)
            {
                // Stay inert rather than destroying itself. Self-destruction inside a build
                // step is a surprise for whoever added the component, it is DEFERRED in Play
                // Mode (so the duplicate is still around for the rest of the frame anyway),
                // and in Edit Mode Destroy is an outright error.
                Debug.LogWarning("[SnowSplatMap] A second buffer was requested; ignoring it. " +
                                 "WeatherManager owns the only one.");
                return;
            }
            Instance = this;

            var shader = Shader.Find("Valkur/SnowSplat");
            if (shader == null)
            {
                // Degrade to exactly what shipped before the buffer existed: the global amount
                // alone, applied uniformly. Everything downstream already handles a missing map.
                Debug.LogWarning("[SnowSplatMap] Shader 'Valkur/SnowSplat' not found — snow will " +
                                 "accumulate uniformly instead of where it lands.");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            var descriptor = new RenderTextureDescriptor(Resolution, Resolution, RenderTextureFormat.R8, 0)
            {
                sRGB           = false,       // this is a depth of snow, not a colour
                useMipMap      = false,
                autoGenerateMips = false,
            };
            _map     = CreateBuffer(descriptor, "ValkurSnowMap");
            _scratch = CreateBuffer(descriptor, "ValkurSnowMapScratch");

            _mesh = new Mesh { name = "SnowSplatQuads", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();

            _commands = new CommandBuffer { name = "SnowSplat" };

            _origin = -Vector2.one * (WorldSize * 0.5f);
            _ready  = true;
            Publish();
        }

        private static RenderTexture CreateBuffer(RenderTextureDescriptor descriptor, string name)
        {
            var rt = new RenderTexture(descriptor)
            {
                name       = name,
                filterMode = FilterMode.Bilinear,
                // Clamp, so a sample that lands a hair outside reads the edge rather than
                // wrapping the drift round to the far side of the world.
                wrapMode   = TextureWrapMode.Clamp,
                hideFlags  = HideFlags.HideAndDontSave,
            };
            rt.Create();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;

            return rt;
        }

        private void OnDestroy() => ReleaseBuffer();

        /// <summary>
        /// Hand back the shader globals and free the GPU resources, once.
        ///
        /// Public and idempotent, and paired with <see cref="EnsureBuilt"/> for the same
        /// reason: in Edit Mode a component whose <c>Awake</c> never ran does not receive
        /// <c>OnDestroy</c> either, so a test that destroys the object gets no teardown at all
        /// and would be asserting against state Unity never gave the object a chance to clean
        /// up. Play Mode reaches this through OnDestroy exactly as before.
        /// </summary>
        public void ReleaseBuffer()
        {
            bool wasPublisher = Instance == this;
            if (wasPublisher) Instance = null;
            _built = false;

            // A shader global outlives the object that set it. Point it back at a white 1x1 so
            // anything still drawing falls back to the uniform path rather than sampling a
            // destroyed RenderTexture. Keyed on having been the PUBLISHER rather than on
            // having allocated: an instance that published and then failed somewhere later
            // still has to take its globals back with it.
            if (wasPublisher)
            {
                Shader.SetGlobalTexture(MapId, Texture2D.whiteTexture);
                Shader.SetGlobalVector(MapRectId, new Vector4(0f, 0f, 1f, 1f));
            }

            if (!_ready) return;
            _ready = false;

            if (_map     != null) _map.Release();
            if (_scratch != null) _scratch.Release();
            DestroyImmediate(_map);
            DestroyImmediate(_scratch);
            DestroyImmediate(_material);
            DestroyImmediate(_mesh);
            _map = null; _scratch = null; _material = null; _mesh = null;
            _commands?.Release();
            _commands = null;
        }

        // ── public API ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Record that a flake landed here. Queued rather than drawn: a whole frame's landings
        /// go into one mesh and one draw, because sixty individual render-target switches per
        /// frame would cost far more than the snow is worth.
        /// </summary>
        public void Stamp(Vector2 worldPosition, float strength = SplatStrength)
        {
            if (!_ready) return;
            _pending.Add(new Vector3(worldPosition.x, worldPosition.y, strength));
        }

        /// <summary>
        /// Fill or clear the whole buffer.
        ///
        /// Exists for the <c>snow</c> console command and for the world being torn down.
        /// Without it, <c>snow 1</c> would raise the global amount over a buffer that is still
        /// empty and nothing would appear — the map is a multiplier, so "pretend it has been
        /// snowing" has to say so in both places.
        /// </summary>
        public void Fill(float value)
        {
            if (!_ready) return;
            _pending.Clear();
            var previous = RenderTexture.active;
            RenderTexture.active = _map;
            GL.Clear(false, true, new Color(Mathf.Clamp01(value), 0f, 0f, 1f));
            RenderTexture.active = previous;
        }

        /// <summary>
        /// Advance the buffer: follow the camera, melt, and stamp everything that landed.
        /// Called once per frame by <see cref="WeatherManager"/>.
        /// </summary>
        public void Tick(float deltaTime, float meltPerSecond)
        {
            if (!_ready) return;

            _maintenanceTimer += deltaTime;
            if (_maintenanceTimer >= MaintenanceInterval)
            {
                Maintain(_maintenanceTimer, meltPerSecond);
                _maintenanceTimer = 0f;
            }

            FlushSplats();
            Publish();
        }

        // ── scroll + melt ────────────────────────────────────────────────────────────

        /// <summary>
        /// One pass that does both jobs the buffer needs between stamps: re-anchor it under the
        /// camera, and take a step of melt off everything in it.
        ///
        /// They are folded together because a second full-buffer blit at the same cadence
        /// would be the same bandwidth for nothing — and because doing them in two passes
        /// would need the scroll's ping-pong twice.
        /// </summary>
        private void Maintain(float elapsed, float meltPerSecond)
        {
            Vector2 offset = Vector2.zero;

            if (_trackedCamera == null) _trackedCamera = Camera.main;
            if (_trackedCamera != null)
            {
                Vector2 centre = _trackedCamera.transform.position;
                Vector2 target = centre - Vector2.one * (WorldSize * 0.5f);

                if (Mathf.Abs(target.x - _origin.x) > RecenterDistance ||
                    Mathf.Abs(target.y - _origin.y) > RecenterDistance)
                {
                    // Snap the new origin to the texel grid. An unsnapped scroll resamples the
                    // drift at a fractional offset every time, and repeated bilinear resampling
                    // of the same data is a blur — the drifts would dissolve as the player walks.
                    float texel = WorldSize / Resolution;
                    target.x = Mathf.Round(target.x / texel) * texel;
                    target.y = Mathf.Round(target.y / texel) * texel;

                    offset  = (target - _origin) / WorldSize;
                    _origin = target;
                }
            }

            float fade = Mathf.Clamp01(1f - meltPerSecond * elapsed);
            if (offset == Vector2.zero && fade >= 0.9999f) return;   // nothing to do

            _material.SetVector(ScrollFadeId, new Vector4(offset.x, offset.y, fade, 0f));
            Graphics.Blit(_map, _scratch, _material, 1);

            // Ping-pong rather than blitting in place: a shader that reads and writes the same
            // RenderTexture has undefined results on every backend that does not silently
            // resolve it for you.
            (_map, _scratch) = (_scratch, _map);
        }

        // ── stamping ─────────────────────────────────────────────────────────────────

        private void FlushSplats()
        {
            int count = _pending.Count;
            if (count == 0) return;

            EnsureMeshCapacity(count);

            float halfW = SplatRadius;
            for (int i = 0; i < count; i++)
            {
                var p = _pending[i];
                int v = i * 4;

                _verts[v + 0] = new Vector3(p.x - halfW, p.y - halfW, 0f);
                _verts[v + 1] = new Vector3(p.x + halfW, p.y - halfW, 0f);
                _verts[v + 2] = new Vector3(p.x + halfW, p.y + halfW, 0f);
                _verts[v + 3] = new Vector3(p.x - halfW, p.y + halfW, 0f);

                var c = new Color(p.z, 0f, 0f, 1f);
                _colors[v + 0] = c; _colors[v + 1] = c; _colors[v + 2] = c; _colors[v + 3] = c;
            }
            _pending.Clear();

            // Degenerate the tail of the buffer rather than resizing the mesh every frame: the
            // vertex arrays are hoisted at the high-water mark and only the live quads are drawn.
            for (int i = count; i * 4 + 3 < _verts.Length; i++)
            {
                int v = i * 4;
                _verts[v + 0] = _verts[v + 1] = _verts[v + 2] = _verts[v + 3] = Vector3.zero;
            }

            _mesh.vertices  = _verts;
            _mesh.uv        = _uvs;
            _mesh.colors    = _colors;
            _mesh.triangles = _indices;
            _mesh.RecalculateBounds();

            // An orthographic projection over the buffer's world rect, so the quads' world
            // coordinates land exactly on the texels that world position maps to.
            var view = Matrix4x4.identity;
            var proj = Matrix4x4.Ortho(_origin.x, _origin.x + WorldSize,
                                       _origin.y, _origin.y + WorldSize, -1f, 1f);

            _commands.Clear();
            _commands.SetRenderTarget(_map);
            _commands.SetViewProjectionMatrices(view, proj);
            _commands.DrawMesh(_mesh, Matrix4x4.identity, _material, 0, 0);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        private void EnsureMeshCapacity(int quads)
        {
            if (_verts.Length >= quads * 4) return;

            // Grow in steps, not to the exact size: landings arrive in bursts and a per-frame
            // reallocation would be garbage on every gust.
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(quads, 64));

            _verts  = new Vector3[capacity * 4];
            _colors = new Color[capacity * 4];
            _uvs    = new Vector2[capacity * 4];
            _indices = new int[capacity * 6];

            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4, t = i * 6;
                _uvs[v + 0] = new Vector2(0f, 0f);
                _uvs[v + 1] = new Vector2(1f, 0f);
                _uvs[v + 2] = new Vector2(1f, 1f);
                _uvs[v + 3] = new Vector2(0f, 1f);

                _indices[t + 0] = v + 0; _indices[t + 1] = v + 2; _indices[t + 2] = v + 1;
                _indices[t + 3] = v + 0; _indices[t + 4] = v + 3; _indices[t + 5] = v + 2;
            }

            _mesh.Clear();
        }

        // ── publication ──────────────────────────────────────────────────────────────

        private void Publish()
        {
            Shader.SetGlobalTexture(MapId, _map);
            Shader.SetGlobalVector(MapRectId, new Vector4(_origin.x, _origin.y, WorldSize, WorldSize));
        }
    }
}
