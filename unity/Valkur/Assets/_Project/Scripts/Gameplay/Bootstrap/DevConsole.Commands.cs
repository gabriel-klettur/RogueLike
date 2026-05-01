using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    public partial class DevConsole
    {
        private void CmdGodMode()
        {
            _godMode = !_godMode;
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var health = player.GetComponent<Health>();
            if (health == null) { Log("Player has no Health component."); return; }
            health.SetInvincible(_godMode);
            Log($"God mode: {(_godMode ? "ON" : "OFF")}");
        }

        private void CmdHeal()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var health = player.GetComponent<Health>();
            if (health != null) health.Heal(health.MaxHealth);
            var mana = player.GetComponent<Mana>();
            if (mana != null) mana.Restore(mana.MaxMana);
            Log("Player fully healed.");
        }

        private void CmdTeleport(string[] parts)
        {
            if (parts.Length < 3 || !float.TryParse(parts[1], out float x) || !float.TryParse(parts[2], out float y))
            { Log("Usage: tp <x> <y>"); return; }
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            player.position = new Vector3(x, y, 0f);
            Log($"Teleported to ({x}, {y}).");
        }

        private void CmdSetTime(string[] parts)
        {
            if (parts.Length < 2 || !float.TryParse(parts[1], out float t))
            { Log("Usage: time <0..1>  (e.g. time 0.5 = noon)"); return; }
            t = Mathf.Clamp01(t);
            if (DayNightCycle.Instance == null) { Log("DayNightCycle not found."); return; }
            DayNightCycle.Instance.SetTimeNormalized(t);
            Log($"Time set to {t:F2} ({Mathf.RoundToInt(t * 24f):D2}:00).");
        }

        private void CmdKillAll()
        {
            int count = 0;
            foreach (var health in FindObjectsOfType<Health>())
            {
                var player = EntityRegistry.PlayerTransform;
                if (player != null && health.gameObject == player.gameObject) continue;
                if (!health.IsDead) { health.TakeDamage(health.MaxHealth * 100); count++; }
            }
            Log($"Killed {count} entities.");
        }

        private void CmdGive(string[] parts)
        {
            if (parts.Length < 2) { Log("Usage: give <item_id> [qty]"); return; }
            string itemId = parts[1];
            int qty = parts.Length >= 3 && int.TryParse(parts[2], out int q) ? Mathf.Max(1, q) : 1;
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var inv = player.GetComponent<Inventory.Inventory>();
            if (inv == null) { Log("Player has no Inventory."); return; }
            var allDefs = Resources.FindObjectsOfTypeAll<Data.ItemDefinition>();
            Data.ItemDefinition def = null;
            foreach (var d in allDefs)
                if (d.itemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)) { def = d; break; }
            if (def == null) { Log($"Item '{itemId}' not found."); return; }
            int added = inv.AddItem(def, qty);
            Log($"Added {added}x {def.displayName} to inventory.");
        }

        private void CmdSpawn(string[] parts)
        {
            if (parts.Length < 2) { Log("Usage: spawn <monster_key>"); return; }
            string key = parts[1];
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var spawner = FindObjectOfType<MonsterSpawner>();
            if (spawner == null) { Log("No MonsterSpawner in scene."); return; }
            var allDefs = Resources.FindObjectsOfTypeAll<Data.MonsterDefinition>();
            Data.MonsterDefinition def = null;
            foreach (var d in allDefs)
                if (d.monsterKey.Equals(key, StringComparison.OrdinalIgnoreCase)) { def = d; break; }
            if (def == null) { Log($"Monster '{key}' not found."); return; }
            Vector2 spawnPos = (Vector2)player.position + UnityEngine.Random.insideUnitCircle.normalized * 3f;
            spawner.RequestSpawn(def, spawnPos);
            Log($"Spawned {def.displayName} @ ({spawnPos.x:F1}, {spawnPos.y:F1}).");
        }

        private void Log(string msg)
        {
            _log.Add(msg);
            while (_log.Count > LOG_MAX_LINES) _log.RemoveAt(0);
        }

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTex(new Color(0.05f, 0.05f, 0.08f, 0.95f));
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 11;
            _labelStyle.normal.textColor = new Color(0.85f, 0.95f, 0.85f);
            _labelStyle.wordWrap = false;
            _labelStyle.richText = false;
            _inputStyle = new GUIStyle(GUI.skin.textField);
            _inputStyle.fontSize = 12;
            _inputStyle.normal.textColor = Color.white;
        }

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(2, 2);
            tex.SetPixels(new[] { col, col, col, col });
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        // ── Spell Debug Commands ──

        private void CmdSpell(string[] parts)
        {
            if (parts.Length < 2) { Log("Usage: spell <spell_key>"); return; }
            string key = parts[1];
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var caster = player.GetComponent<SpellCaster>();
            if (caster == null) { Log("Player has no SpellCaster."); return; }

            // Get facing direction (toward mouse or default right). Routed
            // through MouseInputManager so the legacy backend supplies the
            // position when the new InputSystem package drops OS events.
            Vector2 dir = Vector2.right;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector2 mouseScreen = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
                if (mouseScreen.sqrMagnitude < 1f)
                    mouseScreen = new Vector2(Screen.width / 2f, Screen.height / 2f);
                Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
                dir = ((Vector2)mouseWorld - (Vector2)player.position).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector2.right;
            }

            bool success = caster.TryCastByKey(key, dir);
            if (success)
                Log($"Cast '{key}' → dir=({dir.x:F2},{dir.y:F2})");
            else
                Log($"Failed to cast '{key}' (not registered, on cooldown, or insufficient mana).");
        }

        private void CmdSpellList()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var caster = player.GetComponent<SpellCaster>();
            if (caster == null) { Log("Player has no SpellCaster."); return; }

            Log("--- Registered Spells ---");

            // Use reflection to access the spell book (private field)
            var bookField = typeof(SpellCaster).GetField("_spellBook",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (bookField == null) { Log("Cannot read spell book (reflection failed)."); return; }

            var book = bookField.GetValue(caster) as System.Collections.Generic.Dictionary<string, SpellDefinition>;
            if (book == null || book.Count == 0) { Log("Spell book is empty!"); return; }

            var sorted = book.OrderBy(kv => kv.Value.type.ToString()).ThenBy(kv => kv.Key);
            int count = 0;
            foreach (var kv in sorted)
            {
                var s = kv.Value;
                float cd = caster.GetBookCooldownRemaining(kv.Key);
                string cdStr = cd > 0 ? $" [CD:{cd:F1}s]" : "";
                Log($"  {s.spellKey} ({s.type}) dmg={s.damage} mana={s.manaCost} cd={s.cooldownDuration:F1}s{cdStr}");
                count++;
            }
            Log($"Total: {count} spells registered.");
        }

        private void CmdSpellInfo(string[] parts)
        {
            if (parts.Length < 2) { Log("Usage: spellinfo <spell_key>"); return; }
            string key = parts[1];
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No player found."); return; }
            var caster = player.GetComponent<SpellCaster>();
            if (caster == null) { Log("Player has no SpellCaster."); return; }

            var spell = caster.GetSpellByKey(key);
            if (spell == null)
            {
                Log($"Spell '{key}' not found in spell book.");
                // Try to find it in all loaded spell definitions
                var allDefs = Resources.FindObjectsOfTypeAll<SpellDefinition>();
                foreach (var d in allDefs)
                {
                    if (d.spellKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        spell = d;
                        Log($"  (Found as unregistered asset)");
                        break;
                    }
                }
                if (spell == null) return;
            }

            Log($"--- {spell.displayName} ({spell.spellKey}) ---");
            Log($"  Type:       {spell.type}");
            Log($"  Damage:     {spell.damage}");
            Log($"  Mana Cost:  {spell.manaCost}");
            Log($"  Cooldown:   {spell.cooldownDuration:F2}s");
            Log($"  Prepare:    {spell.prepareDuration:F2}s");
            Log($"  Channel:    {spell.channelDuration:F2}s");
            Log($"  Speed:      {spell.speed}");
            Log($"  Range:      {spell.range}");
            Log($"  Lifetime:   {spell.lifetime}");
            Log($"  Radius:     {spell.radius}");
            Log($"  Duration:   {spell.duration}");
            if (spell.damage > 0)    Log($"  DPS:        {spell.damagePerTick} / {spell.tickPeriod:F2}s");
            if (spell.healPerTick > 0) Log($"  HealTick:   {spell.healPerTick}");
            if (!string.IsNullOrEmpty(spell.element)) Log($"  Element:    {spell.element}");
            if (!string.IsNullOrEmpty(spell.vfxPreset)) Log($"  VFX Preset: {spell.vfxPreset}");
            Log($"  Interruptible: {spell.interruptible}");
            Log($"  Max Instances: {spell.maxInstances}");

            float currentCd = caster.GetBookCooldownRemaining(key);
            if (currentCd > 0) Log($"  Current CD: {currentCd:F1}s remaining");
        }
    }
}
