using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// What a phase change LOOKS like on the boss: a white flash across the body and a ring
    /// bursting out from its feet. Before this a phase change was a bar refresh at the top of
    /// the screen and nothing on the creature — the one moment the fight changes shape
    /// produced no pixel where the player was looking.
    ///
    /// The camera beat is NOT fired here. <c>CameraFeelDirector</c> registers every boss and
    /// owns the <c>BossPhase</c> cue; a second shake from this file would double it.
    /// </summary>
    public partial class BossPhaseController
    {
        /// <summary>How long the body flash holds before decaying.</summary>
        public const float FlashSeconds = 0.38f;

        /// <summary>The ring's reach from the feet, in world units.</summary>
        public const float RingReach = 3.4f;

        /// <summary>The ring's life.</summary>
        public const float RingSeconds = 0.55f;

        private static readonly Color FlashColour = new Color(1f, 0.96f, 0.85f, 1f);
        private static readonly Color RingColour  = new Color(1f, 0.55f, 0.25f, 0.9f);

        private bool _burstSubscribed;

        private void SubscribeBurst()
        {
            if (_burstSubscribed) return;
            OnPhaseChanged += PlayPhaseBurst;
            _burstSubscribed = true;
        }

        private void UnsubscribeBurst()
        {
            if (!_burstSubscribed) return;
            OnPhaseChanged -= PlayPhaseBurst;
            _burstSubscribed = false;
        }

        private void PlayPhaseBurst(int oldPhase, int newPhase) => PlayPhaseBurst();

        /// <summary>Flash the body and burst a ring from the feet. Public for the fixture.</summary>
        public void PlayPhaseBurst()
        {
            var ring = SpawnRing();
            if (Application.isPlaying)
            {
                var tint = SpriteTintStack.Attach(gameObject);
                if (tint != null) StartCoroutine(FlashRoutine(tint));
                if (ring != null) StartCoroutine(RingRoutine(ring));
            }
        }

        /// <summary>The ring sprite, so a test can find it. Sorted on VFX: it is light, not matter.</summary>
        private SpriteRenderer SpawnRing()
        {
            var body = SpriteTintStack.ResolveBodyRenderer(gameObject);
            var go = new GameObject("PhaseBurstRing");
            // Unparented on purpose: a boss root may be scaled, and a scaled ring is a wrong
            // radius; it follows nothing because it is born and gone in half a second.
            var feet = body != null ? new Vector3(body.bounds.center.x, body.bounds.min.y + 0.1f, 0f) : transform.position;
            go.transform.position = feet;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite           = ElementalSprites.Ring;
            sr.sharedMaterial   = ElementalSprites.SharedAdditiveMaterial;
            sr.color            = RingColour;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder     = 20;
            // Lying on the ground: the same squash every ground ring in the project uses.
            go.transform.localScale = new Vector3(0.2f, 0.2f * 0.42f, 1f);
            return sr;
        }

        private static IEnumerator FlashRoutine(SpriteTintStack tint)
        {
            float t = 0f;
            while (t < FlashSeconds)
            {
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / FlashSeconds);
                tint.SetFlash(FlashColour, a * a);
                yield return null;
            }
            tint.ClearFlash();
        }

        private static IEnumerator RingRoutine(SpriteRenderer ring)
        {
            float t = 0f;
            var c = ring.color;
            while (t < RingSeconds && ring != null)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / RingSeconds);
                float eased = 1f - (1f - u) * (1f - u);
                // Ring's bright band peaks at 0.78 of its normalized radius: span = reach / 0.39.
                float span = Mathf.Lerp(0.3f, RingReach / 0.39f, eased);
                ring.transform.localScale = new Vector3(span, span * 0.42f, 1f);
                c.a = RingColour.a * (1f - u * u);
                ring.color = c;
                yield return null;
            }
            if (ring != null) Destroy(ring.gameObject);
        }
    }
}
