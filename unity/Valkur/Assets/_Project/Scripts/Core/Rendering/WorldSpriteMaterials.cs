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

        private static Material _lit;
        private static Material _unlit;
        private static bool     _ambientResolved;
        private static bool     _ambientAvailable;

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
