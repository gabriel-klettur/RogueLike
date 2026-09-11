using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// What a character currently has for the player. Drives the badge's glyph.
    ///
    /// <para><b>The numbers are an URGENCY ORDER and the publisher compares them.</b> A
    /// character is routinely several of these at once — Gatita offers the banquet, is
    /// owed the pantry, and gave you a third that is half done — and
    /// <c>QuestMarkerPublisher.RaiseBadge</c> keeps the HIGHEST. So ordering them wrongly
    /// does not fail, it shows the wrong mark: the first cut had InProgress above TurnIn,
    /// which would have hidden a finished quest's reward behind the dim "you are working
    /// on something" glyph — the one state the player can do nothing with.</para>
    ///
    /// <para>Safe to renumber, unlike most enums in this project: it is runtime-only, and
    /// is serialized into no asset and no save.</para>
    /// </summary>
    public enum QuestBadgeState
    {
        /// <summary>Nothing — the badge hides itself.</summary>
        None = 0,

        /// <summary>A quest of theirs is under way. A dimmed, quiet mark.</summary>
        InProgress = 1,

        /// <summary>Work on offer. The classic exclamation mark.</summary>
        Offer = 2,

        /// <summary>Something finished, waiting to be reported. A question mark.</summary>
        TurnIn = 3,
    }

    /// <summary>
    /// The mark over a character's head that says they have something for you.
    ///
    /// <para><b>Why it earns its place.</b> Discovery scored 3.5 because the only way to
    /// learn a character had work was to walk up and read the line they say on open — so
    /// finding the ten shipped quests meant opening a conversation with all seven personas
    /// and remembering which ones answered. A mark visible from across the street is the
    /// difference between a quest layer you stumble into and one you can go looking for.</para>
    ///
    /// <para><b>It is DRIVEN, never self-polling.</b> The badge holds no reference to the
    /// quest layer and asks it nothing: <c>QuestMarkerPublisher</c> already scans every
    /// persona on its own timer and pushes the answer in. A badge that polled would run
    /// that same scan once per character per frame, and its state could disagree with the
    /// minimap marker computed from the same facts a moment earlier.</para>
    ///
    /// <para><b>The glyph is generated, not authored.</b> Three tiny textures with no art
    /// dependency — the alternative is a sprite import, an atlas entry and a lookup for
    /// nine pixels of exclamation mark.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestGiverBadge : MonoBehaviour
    {
        /// <summary>How far above the character's own bounds the mark floats.</summary>
        private const float CLEARANCE = 0.35f;

        /// <summary>World height of the mark.</summary>
        private const float GLYPH_HEIGHT = 0.62f;

        /// <summary>Amplitude of the idle bob, in world units.</summary>
        private const float BOB = 0.07f;

        /// <summary>Seconds for one bob cycle.</summary>
        private const float BOB_PERIOD = 1.6f;

        private static readonly Color OfferColor      = new Color(0.98f, 0.82f, 0.24f, 1f);
        private static readonly Color TurnInColor     = new Color(0.46f, 0.90f, 0.42f, 1f);
        private static readonly Color InProgressColor = new Color(0.62f, 0.66f, 0.72f, 0.55f);

        private SpriteRenderer _renderer;
        private Transform _pivot;
        private QuestBadgeState _state = QuestBadgeState.None;
        private float _baseY;

        /// <summary>The mark this character is showing.</summary>
        public QuestBadgeState State => _state;

        /// <summary>
        /// Sets what the character is advertising. Cheap and idempotent — the publisher
        /// calls it twice a second for every persona in the world.
        /// </summary>
        public void SetState(QuestBadgeState state)
        {
            if (_state == state && _renderer != null) return;
            _state = state;
            EnsureBuilt();

            if (_renderer == null) return;
            _renderer.gameObject.SetActive(state != QuestBadgeState.None);
            if (state == QuestBadgeState.None) return;

            _renderer.sprite = QuestBadgeSprites.For(state);
            _renderer.color = state == QuestBadgeState.Offer      ? OfferColor
                            : state == QuestBadgeState.TurnIn     ? TurnInColor
                                                                  : InProgressColor;
        }

        private void EnsureBuilt()
        {
            if (_pivot != null) return;

            var go = new GameObject("QuestBadge");
            _pivot = go.transform;
            _pivot.SetParent(transform, false);

            _renderer = go.AddComponent<SpriteRenderer>();

            // Overhead, so it clears the character's own body and every prop layer. It is
            // a readout the player must be able to find, not a thing in the world — the
            // opposite of FacingIndicator, which is deliberately depth-sorted with the body.
            _renderer.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
            _renderer.sortingOrder = 0;

            _baseY = ResolveHeight();
            _pivot.localPosition = new Vector3(0f, _baseY, 0f);
        }

        /// <summary>
        /// How high the mark sits: the top of the character's own collider, never the
        /// sprite.
        ///
        /// <para>Same rule <c>InteractionBounds</c> records for a villager's badge — a
        /// character is drawn upward from their feet and Gatita is 2.4 units tall, so
        /// measuring against the sprite floats the mark far above the head of anything
        /// large and buries it in the chest of anything small.</para>
        /// </summary>
        private float ResolveHeight()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) return col.bounds.size.y + CLEARANCE;

            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr != _renderer) return sr.bounds.size.y + CLEARANCE;

            return 1.6f;
        }

        private void LateUpdate()
        {
            if (_pivot == null || _state == QuestBadgeState.None) return;

            // A slow bob is the whole animation. It exists because a static mark over a
            // static villager reads as part of the art; anything faster would compete with
            // the combat feedback for the player's eye.
            float bob = Mathf.Sin(Time.time * (Mathf.PI * 2f / BOB_PERIOD)) * BOB;
            _pivot.localPosition = new Vector3(0f, _baseY + bob, 0f);

            // Sized in WORLD units rather than by scaling the parent, because these
            // characters ship at three different pixel-per-unit pairs and a scale factor
            // would make the vampire's mark half again the dwarf's.
            var sprite = _renderer.sprite;
            if (sprite != null)
            {
                float h = sprite.bounds.size.y;
                if (h > 0.0001f)
                {
                    float k = GLYPH_HEIGHT / h;
                    _pivot.localScale = new Vector3(k, k, 1f);
                }
            }
        }
    }
}
