using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.NPC
{
    public partial class VendorShopUI
    {
        // ------------------------------------------------------------------
        // Row Building
        // ------------------------------------------------------------------

        private partial void RefreshVendorRows()
        {
            foreach (var r in _vendorRows)
                if (r != null) Destroy(r);
            _vendorRows.Clear();

            if (_currentVendor == null || _vendorRowsParent == null) return;

            foreach (var entry in _currentVendor.ShopInventory)
            {
                if (entry.item == null) continue;
                int price = _currentVendor.GetBuyPrice(entry.item);
                int stock = entry.stock;
                var row = BuildRow(_vendorRowsParent, entry.item, price, stock, true);
                _vendorRows.Add(row);
            }

            LayoutRows(_vendorRowsParent, _vendorRows);
            if (_vendorEmptyText != null) _vendorEmptyText.gameObject.SetActive(_vendorRows.Count == 0);
        }

        private partial void RefreshPlayerRows()
        {
            foreach (var r in _playerRows)
                if (r != null) Destroy(r);
            _playerRows.Clear();

            if (_playerInventory == null || _playerRowsParent == null) return;

            foreach (var slot in _playerInventory.Slots)
            {
                if (slot.IsEmpty) continue;
                int price = _currentVendor != null ? _currentVendor.GetSellPrice(slot.Item) : 0;
                var row = BuildRow(_playerRowsParent, slot.Item, price, slot.Quantity, false);
                _playerRows.Add(row);
            }

            LayoutRows(_playerRowsParent, _playerRows);
            if (_playerEmptyText != null) _playerEmptyText.gameObject.SetActive(_playerRows.Count == 0);
        }

        private GameObject BuildRow(Transform parent, ItemDefinition item, int price, int qty, bool isBuy)
        {
            var row = new GameObject($"Row_{item.itemId}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(0f, rowHeight);

            // Background image, with a hover tint.
            //
            // rowHoverColor was a [SerializeField] with no reader — authored, exposed in the
            // inspector, and connected to nothing. A list of eight near-identical dark rows
            // gives the eye no way to tell which one the cursor is over, which matters here
            // because the Buy button is at the far end of the row from the item's name.
            var bg = row.AddComponent<Image>();
            bg.color = rowColor;
            AttachHoverTint(row, bg, rowColor, rowHoverColor);

            // Icon
            if (item.icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(row.transform, false);
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(4f, 0f);
                iconRect.sizeDelta = new Vector2(40f, 40f);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = item.icon;
                iconImg.preserveAspect = true;
            }

            // Item name label
            var nameLabel = CreateLabel(row.transform, item.displayName, 11, Color.white, false);
            var nlRect = nameLabel.GetComponent<RectTransform>();
            nlRect.anchorMin = new Vector2(0f, 0.5f);
            nlRect.anchorMax = new Vector2(0.5f, 0.5f);
            nlRect.pivot = new Vector2(0f, 0.5f);
            nlRect.anchoredPosition = new Vector2(50f, 6f);
            nlRect.sizeDelta = new Vector2(0f, 16f);

            // Stock / qty label
            var stockLabel = CreateLabel(row.transform, $"x{qty}", 10, new Color(0.7f, 0.7f, 0.7f), false);
            var slRect = stockLabel.GetComponent<RectTransform>();
            slRect.anchorMin = new Vector2(0f, 0.5f);
            slRect.anchorMax = new Vector2(0.5f, 0.5f);
            slRect.pivot = new Vector2(0f, 0.5f);
            slRect.anchoredPosition = new Vector2(50f, -8f);
            slRect.sizeDelta = new Vector2(0f, 14f);

            // Price label
            Color priceCol = isBuy ? new Color(0.9f, 0.75f, 0.2f) : new Color(0.7f, 0.9f, 0.3f);
            var priceLabel = CreateLabel(row.transform, $"{price}g", 11, priceCol, false);
            var prRect = priceLabel.GetComponent<RectTransform>();
            prRect.anchorMin = new Vector2(0.50f, 0.5f);
            prRect.anchorMax = new Vector2(0.64f, 0.5f);
            prRect.pivot = new Vector2(0.5f, 0.5f);
            prRect.anchoredPosition = new Vector2(0f, 0f);
            prRect.sizeDelta = new Vector2(0f, 16f);

            // Buy / Sell button
            var btnColor = isBuy ? buyButtonColor : sellButtonColor;
            string btnLabel = isBuy ? "Buy" : "Sell";
            var btn = CreateButton(row.transform, btnLabel, 10, btnColor,
                new Vector2(0.86f, 0.15f), new Vector2(0.98f, 0.85f));

            // Quantity stepper (-/+) and count label between the price and the action button.
            int maxQty = isBuy ? Mathf.Max(1, qty) : Mathf.Max(1, qty);
            int[] qtyBox = { 1 };
            var qtyLabel = CreateLabel(row.transform, "1", 11, Color.white, true);
            var qlRect = qtyLabel.GetComponent<RectTransform>();
            qlRect.anchorMin = new Vector2(0.72f, 0.15f);
            qlRect.anchorMax = new Vector2(0.78f, 0.85f);
            qlRect.offsetMin = qlRect.offsetMax = Vector2.zero;

            // The stepper reads - [n] + from left to right, each the same width. The '+' used
            // to be given 0.86..0.88 — two per cent of the row, about six pixels — so it was
            // effectively invisible and impossible to hit, while '-' had three times that.
            var minusBtn = CreateButton(row.transform, "-", 12, new Color(0.35f, 0.35f, 0.40f, 1f),
                new Vector2(0.66f, 0.15f), new Vector2(0.72f, 0.85f));
            minusBtn.onClick.AddListener(() =>
            {
                qtyBox[0] = Mathf.Max(1, qtyBox[0] - 1);
                qtyLabel.text = qtyBox[0].ToString();
                UpdatePriceLabel(priceLabel, priceCol, price, qtyBox[0]);
            });

            var plusBtn = CreateButton(row.transform, "+", 12, new Color(0.35f, 0.35f, 0.40f, 1f),
                new Vector2(0.78f, 0.15f), new Vector2(0.84f, 0.85f));
            plusBtn.onClick.AddListener(() =>
            {
                qtyBox[0] = Mathf.Min(maxQty, qtyBox[0] + 1);
                qtyLabel.text = qtyBox[0].ToString();
                UpdatePriceLabel(priceLabel, priceCol, price, qtyBox[0]);
            });

            // A Buy button the player cannot afford is a control that answers a click with
            // nothing, which reads as a broken window rather than as an empty purse. Buying
            // refreshes both columns, so this is re-evaluated on every purchase.
            if (isBuy)
            {
                int purse = _playerWallet != null ? _playerWallet.Coins : 0;
                bool affordable = price <= purse;
                btn.interactable = affordable;

                var btnImage = btn.GetComponent<Image>();
                if (btnImage != null && !affordable)
                    btnImage.color = new Color(btnColor.r * 0.45f, btnColor.g * 0.45f, btnColor.b * 0.45f, btnColor.a);

                if (!affordable) priceLabel.color = new Color(0.75f, 0.35f, 0.30f);
            }

            var capturedItem = item;
            btn.onClick.AddListener(() =>
            {
                int n = Mathf.Max(1, qtyBox[0]);
                for (int i = 0; i < n; i++)
                {
                    if (isBuy) HandleBuy(capturedItem);
                    else HandleSell(capturedItem);
                }
            });

            return row;
        }

        /// <summary>
        /// Tints <paramref name="background"/> while the cursor is over <paramref name="row"/>.
        ///
        /// An EventTrigger rather than a Selectable: a Button on the row would take the
        /// clicks meant for the Buy and stepper buttons inside it, and a Selectable would
        /// join the keyboard navigation order for something that is not a control.
        /// </summary>
        private static void AttachHoverTint(GameObject row, Image background, Color idle, Color hover)
        {
            var trigger = row.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
            };
            enter.callback.AddListener(_ => { if (background != null) background.color = hover; });
            trigger.triggers.Add(enter);

            var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit
            };
            exit.callback.AddListener(_ => { if (background != null) background.color = idle; });
            trigger.triggers.Add(exit);
        }

        private static void UpdatePriceLabel(TMPro.TextMeshProUGUI label, Color col, int unitPrice, int quantity)
        {
            if (label == null) return;
            label.text = $"{unitPrice * Mathf.Max(1, quantity)}g";
            label.color = col;
        }

        private static void LayoutRows(Transform parent, List<GameObject> rows)
        {
            var contentRect = parent.GetComponent<RectTransform>();
            if (contentRect == null) return;

            float totalH = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                var rRect = rows[i].GetComponent<RectTransform>();
                rRect.anchoredPosition = new Vector2(0f, -totalH);
                rRect.sizeDelta = new Vector2(contentRect.rect.width > 0 ? contentRect.rect.width : 300f, rRect.sizeDelta.y);
                totalH += rRect.sizeDelta.y + 2f;
            }
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, totalH);
        }
    }
}
