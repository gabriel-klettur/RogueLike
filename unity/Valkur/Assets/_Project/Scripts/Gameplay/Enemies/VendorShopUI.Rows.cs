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
        }

        private GameObject BuildRow(Transform parent, ItemDefinition item, int price, int qty, bool isBuy)
        {
            var row = new GameObject($"Row_{item.itemId}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(0f, rowHeight);

            // Background image
            var bg = row.AddComponent<Image>();
            bg.color = rowColor;

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
            prRect.anchorMin = new Vector2(0.55f, 0.5f);
            prRect.anchorMax = new Vector2(0.75f, 0.5f);
            prRect.pivot = new Vector2(0.5f, 0.5f);
            prRect.anchoredPosition = new Vector2(0f, 0f);
            prRect.sizeDelta = new Vector2(0f, 16f);

            // Buy / Sell button
            var btnColor = isBuy ? buyButtonColor : sellButtonColor;
            string btnLabel = isBuy ? "Buy" : "Sell";
            var btn = CreateButton(row.transform, btnLabel, 10, btnColor,
                new Vector2(0.78f, 0.15f), new Vector2(0.98f, 0.85f));

            var capturedItem = item;
            btn.onClick.AddListener(() =>
            {
                if (isBuy) HandleBuy(capturedItem);
                else HandleSell(capturedItem);
            });

            return row;
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
