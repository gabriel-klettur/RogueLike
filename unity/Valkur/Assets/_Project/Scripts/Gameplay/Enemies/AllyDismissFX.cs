using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The end of a summon's service: it SINKS, it does not blink out.
    ///
    /// <para>WHY THIS IS NOT COSMETIC. A player must be able to tell an ally that TIMED OUT
    /// from one that was KILLED, because those two facts call for opposite responses — one
    /// means recast when the cooldown is up, the other means the fight is going badly. A
    /// summon that vanishes identically in both cases hides that, and the information is
    /// gone the instant it is needed. Killed is the entity's own death flow, which is fast
    /// and violent; this one is slow and quiet, and the CONTRAST is the whole point.</para>
    /// </summary>
    internal sealed class AllyDismissFX : MonoBehaviour
    {
        private const float DURATION = 0.9f;
        private const float SINK_DISTANCE = 0.55f;
        private const int MOTE_COUNT = 12;
        private const int ORDER_MOTE = 44;

        private static readonly Color MoteColor = new Color(0.55f, 0.95f, 0.62f, 1f);

        private GameObject _body;
        private SpriteTintStack _tint;
        private Vector3 _startPos;
        private float _age;
        private SpriteRenderer[] _motes;
        private Vector3[] _drift;

        /// <summary>
        /// Dissolve <paramref name="ally"/> and destroy it when the beat finishes. Refused
        /// outside Play Mode: the rig advances from Update, so building one in Edit Mode
        /// leaves a permanent cluster instead of a timed effect, and Object.Destroy is an
        /// outright error there. Same guard every other timed rig in this project uses.
        /// </summary>
        public static void Play(GameObject ally)
        {
            if (ally == null) return;
            if (!Application.isPlaying) { Object.DestroyImmediate(ally); return; }

            var go = new GameObject("AllyDismissFX");
            go.transform.position = ally.transform.position;

            var fx = go.AddComponent<AllyDismissFX>();
            fx._body = ally;
            fx._tint = SpriteTintStack.Attach(ally);
            fx._startPos = ally.transform.position;
            fx.BuildMotes();

            // Stop the creature acting during its own dismissal. Left running it would keep
            // chasing while sinking into the floor, which reads as a bug rather than an
            // ending.
            var brain = ally.GetComponent<FSM.FSMMonsterBrain>();
            if (brain != null) brain.enabled = false;
            var rb = ally.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
            foreach (var col in ally.GetComponentsInChildren<Collider2D>())
                col.enabled = false;
        }

        private void BuildMotes()
        {
            _motes = new SpriteRenderer[MOTE_COUNT];
            _drift = new Vector3[MOTE_COUNT];
            for (int i = 0; i < MOTE_COUNT; i++)
            {
                var go = new GameObject($"Mote{i}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(
                    Random.Range(-0.35f, 0.35f), Random.Range(0f, 0.9f), 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.Sparkle;
                sr.color = new Color(MoteColor.r, MoteColor.g, MoteColor.b, 0f);
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_MOTE;
                sr.transform.localScale = Vector3.one * Random.Range(0.14f, 0.26f);

                _motes[i] = sr;
                _drift[i] = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.5f, 1.1f), 0f);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / DURATION);

            if (_body != null)
            {
                // Sinking, not fading in place: the body goes DOWN, which is what makes it
                // read as returning to wherever it was called from.
                _body.transform.position = _startPos + new Vector3(0f, -SINK_DISTANCE * t * t, 0f);
                // The ally tint fades out through the same stack that set it, never by
                // writing the renderer -- SpriteRenderer.color has exactly one owner.
                _tint?.Set(TintLayer.Spirit, Color.Lerp(new Color(0.62f, 0.90f, 0.68f, 1f),
                                                        new Color(0.25f, 0.35f, 0.28f, 1f), t));
            }

            for (int i = 0; i < _motes.Length; i++)
            {
                if (_motes[i] == null) continue;
                _motes[i].transform.localPosition += _drift[i] * Time.deltaTime;
                _motes[i].color = new Color(MoteColor.r, MoteColor.g, MoteColor.b,
                                            Mathf.Sin(t * Mathf.PI) * 0.85f);
            }

            if (_age < DURATION) return;

            if (_body != null)
            {
                _tint?.Clear(TintLayer.Spirit);
                Destroy(_body);
            }
            Destroy(gameObject);
        }
    }
}
