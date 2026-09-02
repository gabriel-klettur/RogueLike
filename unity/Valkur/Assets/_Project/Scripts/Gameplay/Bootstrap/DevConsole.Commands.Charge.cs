using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>charge</c> command family: light one of the seven energy-charge auras on the
    /// player without binding seven keys to spells that have no gameplay yet.
    ///
    /// <para>These exist because the charges are VISUAL ONLY for now. A spell with no effect
    /// is not worth a hotkey, and the alternative — opening the Spells Editor and casting
    /// through its preview — makes it awkward to compare two tiers back to back, which is the
    /// one thing anyone tuning them actually wants to do.</para>
    ///
    /// <para>Registered from <c>DevConsole.cs::RegisterDefaults()</c> under the "charge"
    /// category, and reachable programmatically through <see cref="DevConsole.Execute"/>.</para>
    /// </summary>
    public partial class DevConsole
    {
        /// <summary>
        /// The ladder, weakest first. Named rather than numbered in the catalog so the console
        /// can accept either: <c>charge 4</c> and <c>charge solar</c> are the same spell.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of string literals. Holds no Unity " +
            "objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly string[] ChargeKeys =
        {
            "charge_ki_spirit",
            "charge_ki_azure",
            "charge_ki_verdant",
            "charge_ki_solar",
            "charge_ki_crimson",
            "charge_ki_violet",
            "charge_ki_void",
        };

        private void RegisterChargeCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "charge",
                Usage = "charge [1-7 | spirit|azure|verdant|solar|crimson|violet|void | off]",
                Help = "light an energy-charge aura on the player (visual only), or put it out",
                Category = "charge",
                Handler = args => CmdCharge(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "charges",
                Usage = "charges",
                Help = "list the seven charge tiers with their colour and intensity",
                Category = "charge",
                Handler = _ => CmdChargeList()
            });
        }

        private void CmdCharge(string[] args)
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { Log("[charge] No player in the scene."); return; }

            if (args == null || args.Length == 0)
            {
                Log("[charge] usage: charge <1-7 | name | off>. 'charges' lists them.");
                return;
            }

            string argument = args[0].Trim().ToLowerInvariant();
            if (argument == "off" || argument == "none" || argument == "stop")
            {
                // The registry owns every tracked effect; asking it to clear the player's is
                // the same path a zone change takes, so a charge cannot be left burning.
                int cleared = SpellEffectRegistry.ClearOwnedBy(player);
                Log($"[charge] put out {cleared} charge(s).");
                return;
            }

            SpellDefinition spell = ResolveChargeSpell(argument);
            if (spell == null)
            {
                Log($"[charge] no charge named '{argument}'. Try 'charges'.");
                return;
            }

            var executor = SpellCaster.GetExecutor(SpellType.EnergyCharge);
            if (executor == null) { Log("[charge] no EnergyCharge executor registered."); return; }

            // Straight to the executor rather than through SpellCaster: these have no mana
            // cost and no gameplay, and going through the cast path would start a cooldown on
            // a spell nobody has learned.
            executor.Execute(new SpellContext
            {
                Spell = spell,
                Caster = player.transform,
                Direction = Vector2.right,
            });

            Log($"[charge] {spell.displayName} — intensity {spell.scale:F2}, " +
                $"{spell.duration:F1}s, radius {spell.radius:F1}");
        }

        private SpellDefinition ResolveChargeSpell(string argument)
        {
            var catalog = Core.ServiceLocator.Get<SpellCatalog>();
            if (catalog == null) return null;

            if (int.TryParse(argument, out int tier) && tier >= 1 && tier <= ChargeKeys.Length)
                return catalog.GetByKey(ChargeKeys[tier - 1]);

            // Accept the bare colour name as well as the full key, so 'charge void' works.
            for (int i = 0; i < ChargeKeys.Length; i++)
                if (ChargeKeys[i] == argument || ChargeKeys[i].EndsWith("_" + argument))
                    return catalog.GetByKey(ChargeKeys[i]);

            return null;
        }

        private void CmdChargeList()
        {
            var catalog = Core.ServiceLocator.Get<SpellCatalog>();
            if (catalog == null) { Log("[charge] No SpellCatalog registered."); return; }

            for (int i = 0; i < ChargeKeys.Length; i++)
            {
                var spell = catalog.GetByKey(ChargeKeys[i]);
                if (spell == null) { Log($"[charge] {i + 1}. {ChargeKeys[i]} — MISSING from the catalog"); continue; }

                Color c = spell.particleColor;
                string lightning = spell.scale >= KiPalette.LightningThreshold ? "  +lightning" : "";
                Log($"[charge] {i + 1}. {spell.displayName,-20} intensity {spell.scale:F2}  " +
                    $"rgb({c.r:F2},{c.g:F2},{c.b:F2})  {spell.duration:F1}s{lightning}");
            }
        }
    }
}
