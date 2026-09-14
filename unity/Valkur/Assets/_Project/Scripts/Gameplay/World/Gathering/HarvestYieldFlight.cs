using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The moment a log is extracted: its icon springs out of the trunk, arcs over and lands on
    /// the worker, and only then is it announced.
    ///
    /// <para><b>WHY A FLIGHT AND NOT JUST TEXT.</b> Goods now go straight into the bag, so nothing
    /// lands on the ground to look at — and a number over the head says THAT something was gained
    /// without saying FROM WHERE or INTO WHOM. An object travelling from the tree to the player is
    /// the one image that says both, and it is the thing worth watching while the axe goes up and
    /// down. The icon is the item's own art, so the player learns to recognise a birch log before
    /// they read its name.</para>
    ///
    /// <para><b>RARITY IS A GLOW, NOT A SIZE.</b> A rare wood carries an additive halo in its
    /// rarity's colour and a sparkle on arrival. Scaling a rare icon up would make the common case
    /// look like a lesser version of the special one; lighting the special one leaves the common
    /// one exactly as it is.</para>
    ///
    /// <para><b>A FULL BAG FALLS.</b> When the bag could not take the item, the icon drops to the
    /// ground at the worker's feet instead of landing on them — the same path, a different ending,
    /// so the player sees immediately that this one did not go in.</para>
    ///
    /// <para>Self-destroying, and in Edit Mode it lands instantly: a fixture has no frames, and the
    /// announcement it gates must still happen.</para>
    /// </summary>
    public sealed class HarvestYieldFlight : MonoBehaviour
    {
        private const float FLIGHT_SECONDS = 0.55f;
        private const float ARC_HEIGHT = 0.9f;
        private const float ICON_WORLD_SIZE = 0.55f;

        private Transform _target;
        private Vector3 _from;
        private Vector3 _fallTo;
        private float _delay;
        private float _t;
        private bool _toBag;
        private SpriteRenderer _icon;
        private SpriteRenderer _glow;
        private ItemDefinition _item;
        private Action<ItemDefinition, bool> _onLanded;

        /// <summary>Launch one item from <paramref name="from"/> toward <paramref name="worker"/>.</summary>
        public static HarvestYieldFlight Launch(ItemDefinition item, Vector3 from, GameObject worker, bool toBag,
            float delay, Action<ItemDefinition, bool> onLanded)
        {
            if (item == null) return null;

            if (!Application.isPlaying || worker == null)
            {
                onLanded?.Invoke(item, toBag);
                return null;
            }

            var go = new GameObject("YieldFlight_" + item.itemId);
            var flight = go.AddComponent<HarvestYieldFlight>();
            flight.Build(item, from, worker, toBag, delay, onLanded);
            return flight;
        }

        private void Build(ItemDefinition item, Vector3 from, GameObject worker, bool toBag, float delay,
            Action<ItemDefinition, bool> onLanded)
        {
            _item = item;
            _target = worker.transform;
            _from = new Vector3(from.x, from.y, 0f);
            _fallTo = worker.transform.position + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.4f);
            _toBag = toBag;
            _delay = Mathf.Max(0f, delay);
            _onLanded = onLanded;
            transform.position = _from;

            bool special = item.rarity >= ItemRarity.Rare;
            if (special)
            {
                var glowGo = new GameObject("Glow");
                glowGo.transform.SetParent(transform, false);
                glowGo.transform.localScale = Vector3.one * ICON_WORLD_SIZE * 2.2f;
                _glow = glowGo.AddComponent<SpriteRenderer>();
                _glow.sprite = ElementalSprites.Glow;
                _glow.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                var c = RarityPalette.Color(item.rarity);
                _glow.color = new Color(c.r, c.g, c.b, 0.75f);
                _glow.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
                _glow.sortingOrder = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, from.y) + 30;
            }

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(transform, false);
            _icon = iconGo.AddComponent<SpriteRenderer>();
            _icon.sprite = item.icon;
            // UNLIT, explicitly. UI_World is one of the layers the ambient light deliberately does
            // not reach, and a SpriteRenderer left on the default Sprite-Lit material renders
            // black there — measured on the first live capture, every log flew as a dark blot.
            _icon.sharedMaterial = Valkur.Core.Rendering.WorldSpriteMaterials.Unlit;
            _icon.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
            _icon.sortingOrder = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, from.y) + 31;

            float native = item.icon != null ? Mathf.Max(item.icon.bounds.size.x, item.icon.bounds.size.y) : 1f;
            iconGo.transform.localScale = Vector3.one * (ICON_WORLD_SIZE / Mathf.Max(0.0001f, native));

            SetVisible(_delay <= 0f);
        }

        private void SetVisible(bool on)
        {
            if (_icon != null) _icon.enabled = on;
            if (_glow != null) _glow.enabled = on;
        }

        private void Update()
        {
            if (_delay > 0f)
            {
                _delay -= Time.deltaTime;
                if (_delay > 0f) return;
                SetVisible(true);
            }

            _t += Time.deltaTime / FLIGHT_SECONDS;
            float k = Mathf.Clamp01(_t);

            Vector3 to = _toBag && _target != null ? Head(_target) : _fallTo;

            // Out fast, settle in: an ease-out along the path with a parabola on top, so the icon
            // leaps from the trunk and drops onto the worker rather than sliding across.
            float e = 1f - (1f - k) * (1f - k);
            Vector3 p = Vector3.Lerp(_from, to, e);
            p.y += Mathf.Sin(k * Mathf.PI) * ARC_HEIGHT;
            transform.position = p;

            // A pop on launch and a squash on arrival.
            float pop = 1f + Mathf.Sin(Mathf.Clamp01(k * 3f) * Mathf.PI) * 0.35f;
            float land = k > 0.8f ? Mathf.Lerp(1f, 0.6f, (k - 0.8f) / 0.2f) : 1f;
            transform.localScale = Vector3.one * pop * land;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 2f) * 12f);

            if (k < 1f) return;

            if (_toBag)
            {
                var c = _item != null && _item.rarity >= ItemRarity.Rare
                    ? RarityPalette.Color(_item.rarity)
                    : new Color(1f, 0.93f, 0.72f, 0.9f);
                HarvestFx.Flash(to, c, _item != null && _item.rarity >= ItemRarity.Rare ? 0.9f : 0.45f);
            }

            _onLanded?.Invoke(_item, _toBag);
            Destroy(gameObject);
        }

        private static Vector3 Head(Transform worker)
        {
            var sr = worker.GetComponentInChildren<SpriteRenderer>();
            float top = sr != null && sr.sprite != null ? sr.bounds.max.y : worker.position.y + 1.8f;
            return new Vector3(worker.position.x, top - 0.1f, 0f);
        }
    }
}
