using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay
{
    /// <summary>
    /// New console commands added as part of the DevConsole registry refactor.
    /// Mirrors the Python slash-command set from roguelike_engine/console/.
    /// Handlers are registered in DevConsole.cs::RegisterDefaults().
    /// </summary>
    public partial class DevConsole
    {
        // ── mana ──────────────────────────────────────────────────────────────

        private void CmdMana()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var mana = player.GetComponent<Mana>();
            if (mana == null) { Log("Player has no Mana component."); return; }
            mana.Restore(mana.MaxMana);
            Log($"Mana fully restored ({mana.MaxMana}).");
        }

        // ── resurrect ─────────────────────────────────────────────────────────

        private void CmdResurrect()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }

            // Preferred path: route through the death-sequence controller so
            // every revive (altar OR cheat) goes through the same teardown
            // (spirit visuals → off, grayscale → 0, corpse → despawn, HP/MP → full).
            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller != null && controller.IsDeathFlowActive)
            {
                controller.ForceRevive();
                Log("Player resurrected via DeathSequenceController.");
                return;
            }

            // Fallback (no controller, or player isn't actually dying): manually
            // refill stats so the cheat still works in EditMode tests / detached
            // scenes that don't bootstrap the controller.
            var health = player.GetComponent<Health>();
            if (health != null) health.Initialize(health.MaxHealth);

            var mana = player.GetComponent<Mana>();
            if (mana != null) mana.Restore(mana.MaxMana);

            var pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;

            GameEvents.FirePlayerResurrected();
            GameEvents.FirePlayerRevived();

            if (Time.timeScale < 0.01f) Time.timeScale = 1f;

            Log("Player resurrected (fallback path — no DeathSequenceController found).");
        }

        // ── givememoney ───────────────────────────────────────────────────────

        private void CmdGiveMeMoney(string[] parts)
        {
            int amount = 1000;
            if (parts.Length >= 2 && int.TryParse(parts[1], out int parsed) && parsed > 0)
                amount = parsed;

            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var wallet = player.GetComponent<CurrencyWallet>();
            if (wallet == null) { Log("Player has no CurrencyWallet."); return; }
            wallet.Add(amount);
            Log($"Added {amount} coins. New balance: {wallet.Coins}.");
        }

        // ── kill ──────────────────────────────────────────────────────────────

        private void CmdKill(string[] parts)
        {
            if (parts.Length >= 2 &&
                string.Equals(parts[1], "all", StringComparison.OrdinalIgnoreCase))
            {
                CmdKillAll();
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var health = player.GetComponent<Health>();
            if (health == null) { Log("Player has no Health component."); return; }
            health.TakeDamage(health.MaxHealth * 100);
            Log("Player killed.");
        }

        // ── noclip ────────────────────────────────────────────────────────────

        private void CmdNoclip(string[] parts)
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb == null) { Log("Player has no Rigidbody2D."); return; }

            // Parse optional on/off argument.
            bool? forceState = null;
            if (parts.Length >= 2)
            {
                if (string.Equals(parts[1], "on", StringComparison.OrdinalIgnoreCase)) forceState = true;
                else if (string.Equals(parts[1], "off", StringComparison.OrdinalIgnoreCase)) forceState = false;
            }

            bool activate = forceState ?? !_noclipActive;

            if (activate && !_noclipActive)
            {
                _noclipOriginalLayer = player.gameObject.layer;
                rb.simulated = false;
                Log("Noclip ON — physics disabled. Move via teleport or drag.");
            }
            else if (!activate && _noclipActive)
            {
                rb.simulated = true;
                player.gameObject.layer = _noclipOriginalLayer;
                Log("Noclip OFF — physics restored.");
            }
            else
            {
                Log($"Noclip is already {(_noclipActive ? "ON" : "OFF")}.");
                return;
            }

            _noclipActive = activate;
        }

        // ── restockvendorfood ─────────────────────────────────────────────────

        private void CmdRestockVendorFood(string[] parts)
        {
            if (parts.Length < 2)
            {
                Log("Usage: restockvendorfood <vendor_name|current> [qty]");
                return;
            }

            string vendorName = parts[1];
            int qty = 100;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedQty) && parsedQty > 0)
                qty = parsedQty;

            VendorNPC vendor = null;

            if (string.Equals(vendorName, "current", StringComparison.OrdinalIgnoreCase))
            {
                // Find the nearest vendor to the player.
                var player = EntityRegistry.PlayerTransform;
                if (player != null)
                {
                    float closest = float.MaxValue;
                    foreach (var v in FindObjectsOfType<VendorNPC>())
                    {
                        float dist = Vector2.Distance(player.position, v.transform.position);
                        if (dist < closest) { closest = dist; vendor = v; }
                    }
                }
                if (vendor == null) { Log("No vendor found near player."); return; }
            }
            else
            {
                foreach (var v in FindObjectsOfType<VendorNPC>())
                {
                    var interactable = v.GetComponent<NPCInteractable>();
                    if (interactable != null &&
                        string.Equals(interactable.NPCName, vendorName, StringComparison.OrdinalIgnoreCase))
                    {
                        vendor = v;
                        break;
                    }
                }
                if (vendor == null)
                {
                    Log($"Vendor '{vendorName}' not found. Name must match NPCInteractable.NPCName exactly (case-insensitive).");
                    return;
                }
            }

            int restocked = 0;
            foreach (var entry in vendor.ShopInventory)
            {
                if (entry.item == null) continue;
                var category = ItemCategoryUtil.GetCategory(entry.item);
                if (category == ItemCategory.Consumable && entry.item.healing > 0)
                {
                    vendor.RestockItem(entry.item, qty);
                    restocked++;
                }
            }
            Log($"Restocked {restocked} food item type(s) on '{vendor.gameObject.name}' by +{qty} each.");
        }

        // ── remove ────────────────────────────────────────────────────────────

        private void CmdRemove(string[] parts)
        {
            if (parts.Length < 2) { Log("Usage: remove <item_id> [qty]"); return; }
            string itemId = parts[1];
            int qty = parts.Length >= 3 && int.TryParse(parts[2], out int q) ? Mathf.Max(1, q) : 1;

            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var inv = player.GetComponent<Inventory.Inventory>();
            if (inv == null) { Log("Player has no Inventory."); return; }

            var def = FindItemDef(itemId);
            if (def == null) { Log($"Item '{itemId}' not found."); return; }

            int removed = inv.RemoveItem(def, qty);
            if (removed == 0)
                Log($"Item '{def.displayName}' not in inventory (or insufficient quantity).");
            else
                Log($"Removed {removed}x {def.displayName} from inventory.");
        }

        // ── edit (stub) ───────────────────────────────────────────────────────

        private void CmdEditItem(string[] parts)
        {
            Log("[stub] ItemDefinition fields are immutable at runtime (ScriptableObject). " +
                "A slot-level override system will be added in a future update.");
            Log("Usage: edit <item_id> <prop> <value>");
        }

        // ── list inventory ────────────────────────────────────────────────────

        private void CmdListInventory()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var inv = player.GetComponent<Inventory.Inventory>();
            if (inv == null) { Log("Player has no Inventory."); return; }

            if (inv.UsedSlots == 0) { Log("Inventory is empty."); return; }

            Log($"Inventory ({inv.UsedSlots}/{inv.Capacity} slots):");
            foreach (var slot in inv.Slots)
            {
                if (!slot.IsEmpty)
                    Log($"  {slot.Item.itemId,-24} x{slot.Quantity,4}   [{slot.Item.displayName}]");
            }
        }

        // ── pause / resume ────────────────────────────────────────────────────

        private void CmdPause()
        {
            Time.timeScale = 0f;
            Log("Game paused (Time.timeScale = 0).");
        }

        private void CmdResume()
        {
            Time.timeScale = 1f;
            Log("Game resumed (Time.timeScale = 1).");
        }

        // ── save / load ───────────────────────────────────────────────────────

        private void CmdSave(string[] parts)
        {
            string slotName = parts.Length >= 2 ? parts[1] : null;
            var saveService = SaveService.Instance;
            if (saveService == null)
            {
                Log("SaveService not found — use F5 quicksave instead.");
                return;
            }
            bool ok = saveService.Save(slotName);
            Log(ok ? $"Game saved to slot '{slotName ?? "timestamp"}'." : "Save failed — check Unity log for details.");
        }

        private void CmdLoad(string[] parts)
        {
            // Runtime load requires the full path; the Load Game UI handles slot listing.
            // This stub points the user to the correct workflow.
            Log("[stub] Runtime 'load' by slot name is not yet wired to SaveService.Load(path).");
            Log("Use the Load Game UI (Main Menu → Cargar Partida) to load a named save.");
        }

        // ── Shared lookup helpers ─────────────────────────────────────────────

        private static ItemDefinition FindItemDef(string itemId)
        {
            var allDefs = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var d in allDefs)
                if (d.itemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        // ── Tab Completers ────────────────────────────────────────────────────

        private static string[] ItemIdCompleter(string[] tokens)
        {
            // tokens[0] = command name, tokens[last] = partial item id being typed.
            string prefix = tokens.Length >= 2 ? tokens[tokens.Length - 1] : "";
            var results = new List<string>();
            var allDefs = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var d in allDefs)
                if (d.itemId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    results.Add(d.itemId);
            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results.ToArray();
        }

        private static string[] WorldSlugCompleter(string[] tokens)
        {
            string prefix = tokens.Length >= 2 ? tokens[tokens.Length - 1] : "";
            var results = new List<string>();
            var allDescs = Resources.FindObjectsOfTypeAll<WorldDescriptor>();
            foreach (var d in allDescs)
                if (d.Slug.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    results.Add(d.Slug);
            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results.ToArray();
        }

        // ── layer (M1.7: Player layer mutation MVP) ───────────────────────────

        /// <summary>
        /// Console handler for <c>layer &lt;0..8&gt;</c> — sets the player's
        /// <see cref="VisualLayerOccupant.CurrentVisualLayer"/> for testing the
        /// per-visual-layer collision pipeline (M1.5 foundation, M2 runtime). The
        /// occupant's <c>SetVisualLayer</c> path clamps + fires <c>OnLayerChanged</c>
        /// so the COLLIDERS LAYER diagnostic panel reacts the same way as it will
        /// when M2's gameplay triggers eventually drive the same setter.
        /// </summary>
        private void CmdLayer(string[] parts)
        {
            if (parts == null || parts.Length != 2)
            {
                Log("Usage: layer <0..8>");
                return;
            }
            if (!int.TryParse(parts[1], out int target) || target < VisualLayerOccupant.MinLayer
                || target > VisualLayerOccupant.MaxLayer)
            {
                Log($"Invalid layer '{parts[1]}'. Expected 0..8.");
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var occupant = player.GetComponent<VisualLayerOccupant>();
            if (occupant == null) { Log("Player has no VisualLayerOccupant."); return; }

            int prev = occupant.CurrentVisualLayer;
            occupant.SetVisualLayer(target);
            Log($"Player layer: {prev} → {occupant.CurrentVisualLayer} ({occupant.LayerName}).");
        }

        private static string[] MonsterKeyCompleter(string[] tokens)
        {
            string prefix = tokens.Length >= 2 ? tokens[tokens.Length - 1] : "";
            var results = new List<string>();
            var allDefs = Resources.FindObjectsOfTypeAll<MonsterDefinition>();
            foreach (var d in allDefs)
                if (d.monsterKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    results.Add(d.monsterKey);
            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results.ToArray();
        }
    }
}
