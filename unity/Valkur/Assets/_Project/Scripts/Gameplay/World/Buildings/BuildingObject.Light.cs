using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Makes a light fixture actually light the world.
    ///
    /// The <c>Buildings/lights/</c> family — lamp posts, braziers, sconces, lanterns — used to
    /// be pure decoration: a lamp post drawn at full brightness at midnight, emitting nothing.
    /// A template that names a <c>lightPresetKey</c> now spawns its own Light2D through
    /// <see cref="WorldLightLoader.RegisterDerivedLight"/>, so it inherits the day/night gate,
    /// the flicker and the viewport culling that authored lights already get, and costs the
    /// author nothing beyond placing the prop.
    ///
    /// The art ships in lit/unlit pairs (<c>lamp_post_ornate</c> ↔ <c>lamp_post_ornate_lit</c>).
    /// When the template names a <c>litAssetPath</c>, the sprite follows the light: dark by day,
    /// burning at night.
    /// </summary>
    public partial class BuildingObject : MonoBehaviour
    {
        private GameObject _derivedLight;
        private Coroutine  _lightAttachRoutine;
        private bool       _lightSubscribed;
        private bool       _showingLitSprite;

        private bool WantsLight  => _template != null && !string.IsNullOrEmpty(_template.lightPresetKey);
        private bool HasLitSwap  => _template != null && !string.IsNullOrEmpty(_template.litAssetPath);

        /// <summary>
        /// Re-evaluate the fixture's light after the template changed. Called at the end of
        /// <c>Apply</c>, so retargeting a building in the F10 editor swaps its light too.
        /// </summary>
        private void RefreshLightFromTemplate()
        {
            if (!WantsLight)
            {
                DetachLight();
                return;
            }

            if (_derivedLight != null)
            {
                // Already attached — Apply may have rewritten localScale, so the counter-scale
                // and the offset have to be recomputed against the new bounds.
                PlaceDerivedLight();
                return;
            }

            if (_lightAttachRoutine == null && isActiveAndEnabled)
                _lightAttachRoutine = StartCoroutine(AttachLightWhenLoaderReady());
        }

        /// <summary>
        /// Buildings are spawned during world load, and <see cref="WorldLightLoader"/> may not
        /// have its catalog yet — so wait for it rather than dropping the light silently.
        /// Bounded: a world with no light loader at all should not leave a coroutine spinning.
        /// </summary>
        private IEnumerator AttachLightWhenLoaderReady()
        {
            const int maxFrames = 300;   // ~5 s at 60 fps
            for (int i = 0; i < maxFrames && WorldLightLoader.Instance == null; i++)
                yield return null;

            _lightAttachRoutine = null;
            if (!WantsLight) yield break;

            var loader = WorldLightLoader.Instance;
            if (loader == null)
            {
                Debug.LogWarning($"[BuildingObject] '{name}' wants a '{_template.lightPresetKey}' light " +
                                  "but no WorldLightLoader appeared — the fixture stays dark.", this);
                yield break;
            }

            _derivedLight = loader.RegisterDerivedLight(_template.lightPresetKey, LightWorldPosition(), transform);
            SubscribeToLightsGate();
            ApplyLitSprite(DayNightCycle.HasInstance && DayNightCycle.Instance.LightsEnabledNow);
        }

        /// <summary>
        /// Give a solid building a shadow caster, so a torch behind a house stops lighting the
        /// street in front of it.
        ///
        /// Attached to the FOOTPRINT half — the part standing on the ground — not the canopy,
        /// which is roof and overhang the light should pass under. URP's
        /// <c>ShadowCaster2D.Awake</c> builds its shape from the renderer's bounds when the
        /// shape path is empty, so no reflection into URP internals is needed.
        ///
        /// Skipped entirely unless some light preset actually casts: the caster's public
        /// <c>Update()</c> would otherwise run every frame on every building for nothing.
        /// </summary>
        internal void EnsureShadowCaster()
        {
            if (_template == null || !_template.solid) return;
            if (_bottomRenderer == null) return;

            // A fixture emits; it does not occlude. Its own light sits above its footprint, and
            // a caster that close to the light origin produces shadow artefacts rather than a
            // shadow — quite apart from a lamp post being too thin to block anything.
            if (WantsLight) return;

            var loader = WorldLightLoader.Instance;
            if (loader == null || !loader.ShadowsInUse) return;
            if (_bottomRenderer.GetComponent<ShadowCaster2D>() != null) return;

            var caster = _bottomRenderer.gameObject.AddComponent<ShadowCaster2D>();
            caster.castsShadows = true;
            // The building must not shade its own sprite — it is drawn from above, not lit
            // from the side like a wall in a platformer.
            caster.selfShadows  = false;
        }

        private void PlaceDerivedLight()
        {
            if (_derivedLight == null) return;
            _derivedLight.transform.position = LightWorldPosition();

            // The owner's localScale maps sprite pixels to display size and is routinely
            // non-uniform; without the inverse the Light2D radius would come out elliptical.
            var owned = transform.lossyScale;
            _derivedLight.transform.localScale = new Vector3(
                Mathf.Approximately(owned.x, 0f) ? 1f : 1f / owned.x,
                Mathf.Approximately(owned.y, 0f) ? 1f : 1f / owned.y,
                1f);
        }

        /// <summary>
        /// The flame's position: a fraction across the fixture's own rendered bounds. Falls
        /// back to the transform when the renderers are not built yet.
        /// </summary>
        private Vector3 LightWorldPosition()
        {
            var offset = _template != null ? _template.lightOffsetNormalized : new Vector2(0.5f, 0.75f);

            Bounds b;
            if (_bottomRenderer != null)
            {
                b = _bottomRenderer.bounds;
                if (_topRenderer != null) b.Encapsulate(_topRenderer.bounds);
            }
            else if (_topRenderer != null) b = _topRenderer.bounds;
            else return transform.position;

            return new Vector3(
                b.min.x + b.size.x * offset.x,
                b.min.y + b.size.y * offset.y,
                0f);
        }

        private void SubscribeToLightsGate()
        {
            if (_lightSubscribed) return;
            DayNightCycle.OnLightsEnabledChanged += OnLightsGateChanged;
            _lightSubscribed = true;
        }

        private void OnLightsGateChanged(bool lightsOn) => ApplyLitSprite(lightsOn);

        /// <summary>
        /// Swap between the fixture's dark and burning artwork. Re-runs <c>Apply</c> because the
        /// sprite has to be re-sliced into the footprint/canopy pair at the template's split
        /// ratio — the halves are <c>Sprite.Create</c> sub-rects, not whole assets. This costs
        /// two rebuilds per in-game day for a handful of props.
        /// </summary>
        private void ApplyLitSprite(bool lit)
        {
            if (!HasLitSwap || lit == _showingLitSprite) return;
            _showingLitSprite = lit;
            Apply(_template, _scaleOverride, _splitRatioOverride,
                  assetPathOverride: lit ? _template.litAssetPath : null);
        }

        private void DetachLight()
        {
            if (_lightSubscribed)
            {
                DayNightCycle.OnLightsEnabledChanged -= OnLightsGateChanged;
                _lightSubscribed = false;
            }
            if (_derivedLight != null)
            {
                // RemoveLight destroys the GameObject itself; destroying it here as well would
                // be a second Destroy on the same object.
                var loader = WorldLightLoader.Instance;
                if (loader != null) loader.RemoveLight(_derivedLight);
                else                Destroy(_derivedLight);
                _derivedLight = null;
            }
            _showingLitSprite = false;
        }

        private void OnDestroy() => DetachLight();
    }
}
