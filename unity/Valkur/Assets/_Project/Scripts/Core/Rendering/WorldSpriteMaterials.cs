using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// Single owner of the "is this sprite lit or unlit?" decision for world content.
    ///
    /// URP 2D renders a <c>Sprite-Lit-Default</c> sprite BLACK when no Global Light2D
    /// covers its sorting layer, which is why half the project independently reached for
    /// <c>Sprite-Unlit-Default</c> — and why the day/night cycle ended up tinting nothing
    /// at all. The fix is not "always unlit"; it is one probe, made once, that every world
    /// renderer agrees on. See <c>.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md</c>.
    ///
    /// Anything the ambient light must NOT darken — HUD-ish world overlays, spell VFX,
    /// health bars — should keep asking for <see cref="Unlit"/> explicitly rather than
    /// going through <see cref="World"/>.
    /// </summary>
    public static class WorldSpriteMaterials
    {
        private const string LitShaderName   = "Universal Render Pipeline/2D/Sprite-Lit-Default";
        private const string UnlitShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        private const string SnowLitShaderName   = "Valkur/SpriteHDRTintLit";
        private const string SnowUnlitShaderName = "Valkur/SpriteHDRTint";

        private static readonly int SnowRoleId = Shader.PropertyToID("_SnowRole");

        private static Material _lit;
        private static Material _unlit;
        private static bool     _ambientResolved;
        private static bool     _ambientAvailable;

        // Indexed by (int)SnowRole. One shared material per role rather than a per-renderer
        // MaterialPropertyBlock: the role never changes for a given renderer, and an MPB on
        // every tilemap and every building would cost the batching that a shared material keeps.
        //
        // Deliberately NOT readonly. The Domain-Reload ratchet recognises a static as reset
        // when the hook writes the field (stsfld) or calls Clear()/Reset() on its own value —
        // and an array has neither, so `Array.Clear(_snowLit, ...)` passes the field as an
        // ARGUMENT and reads to the scanner as no reset at all. Reassigning a fresh array is
        // both the shape it recognises and the more obviously correct thing to do with a cache
        // whose contents were destroyed with the previous Play session.
        private static Material[] _snowLit   = new Material[3];
        private static Material[] _snowUnlit = new Material[3];

        /// <summary>
        /// How a surface collects snow. The distinction is not cosmetic: in a top-down
        /// projection the floor faces the sky across its whole area and collects evenly, while
        /// anything with a silhouette only collects where nothing is above it. See
        /// <c>Shaders/ValkurSnow.hlsl</c>.
        /// </summary>
        public enum SnowRole
        {
            /// <summary>Never collects. Entities and anything that moves — snow that stays
            /// painted on a walking monster reads as a texture bug, not as weather.</summary>
            None = 0,
            /// <summary>Collects on its upward-facing edges: walls, roofs, trees, props.</summary>
            Cap = 1,
            /// <summary>Collects evenly across the surface: ground and floor decals.</summary>
            Blanket = 2,
        }

        /// <summary>
        /// Domain Reload is OFF — these cached Materials belong to the previous Play session
        /// and are destroyed with it, so a stale handle would surface as a
        /// MissingReferenceException on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _lit              = null;
            _unlit            = null;
            _ambientResolved  = false;
            _ambientAvailable = false;
            _snowLit          = new Material[3];
            _snowUnlit        = new Material[3];
        }

        /// <summary>
        /// True when a Global Light2D exists, i.e. when lit world sprites will actually
        /// receive light instead of rendering black. Probed once per session.
        /// </summary>
        public static bool AmbientLightingAvailable
        {
            get
            {
                if (_ambientResolved) return _ambientAvailable;
                _ambientResolved = true;
                foreach (var l in Object.FindObjectsOfType<Light2D>())
                {
                    if (l.lightType != Light2D.LightType.Global) continue;
                    _ambientAvailable = true;
                    break;
                }
                return _ambientAvailable;
            }
        }

        /// <summary>
        /// Called by the bootstrap the moment the Global Light2D is created or repaired, so
        /// the probe never races a renderer that builds its material during the same frame.
        /// </summary>
        public static void NotifyAmbientLightReady()
        {
            _ambientResolved  = true;
            _ambientAvailable = true;
        }

        /// <summary>Shared material for world surfaces and their inhabitants.</summary>
        public static Material World => AmbientLightingAvailable ? Lit : Unlit;

        public static Material Lit   => _lit   != null ? _lit   : (_lit   = Resolve(LitShaderName));
        public static Material Unlit => _unlit != null ? _unlit : (_unlit = Resolve(UnlitShaderName));

        /// <summary>
        /// Shared world material that also collects snow in the given role.
        ///
        /// Separate from <see cref="World"/> — which stays on URP's stock sprite shaders — so
        /// the snow shader reaches exactly the two callers that want it (the tilemap layers
        /// and placed buildings) instead of every renderer in the project at once. Falls back
        /// to <see cref="World"/> if the Valkur shader is missing from the build, so a
        /// stripped shader costs the snow and nothing else.
        /// </summary>
        public static Material WorldWithSnow(SnowRole role)
        {
            bool lit    = AmbientLightingAvailable;
            var  cache  = lit ? _snowLit : _snowUnlit;
            int  index  = (int)role;

            if (cache[index] != null) return cache[index];

            var shader = Shader.Find(lit ? SnowLitShaderName : SnowUnlitShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[WorldSpriteMaterials] Snow shader '{(lit ? SnowLitShaderName : SnowUnlitShaderName)}' " +
                                  "not found — world surfaces will render correctly but will not collect snow.");
                return World;
            }

            var mat = new Material(shader)
            {
                name      = $"WorldSnow_{(lit ? "Lit" : "Unlit")}_{role}",
                hideFlags = HideFlags.HideAndDontSave,
            };
            mat.SetFloat(SnowRoleId, (float)index);

            cache[index] = mat;
            return mat;
        }

        private static Material Resolve(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[WorldSpriteMaterials] Shader '{shaderName}' not found — sprites may render black.");
                return null;
            }
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }
}
