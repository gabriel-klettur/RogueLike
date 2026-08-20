using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The one material every procedural VFX mesh in the spell layer renders with.
    ///
    /// Ribbons, lances and streaks carry all of their colour in vertex colours, so they
    /// need nothing from a material except an unlit sprite shader and a white texture to
    /// multiply against. Building one per effect — which the slash used to do, once per
    /// cast — is a steady drip of garbage for no visual difference, and it breaks batching
    /// between effects that are otherwise identical.
    /// </summary>
    internal static class UnlitMeshMaterial
    {
        private static Material _shared;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _shared = null;
        }

        public static Material Shared
        {
            get
            {
                if (_shared != null) return _shared;

                _shared = new Material(ElementalSprites.SharedUnlitMaterial)
                {
                    name = "VfxUnlitMeshMaterial",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (_shared.HasProperty("_MainTex"))
                    _shared.mainTexture = Texture2D.whiteTexture;
                return _shared;
            }
        }
    }
}
