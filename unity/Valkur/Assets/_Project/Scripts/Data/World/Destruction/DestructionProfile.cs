using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Everything about how one kind of building can be broken. Referenced from
    /// <see cref="BuildingTemplateData.destruction"/>; a template with none is
    /// indestructible, which is the default for all 969 shipped templates.
    ///
    /// <para>WHY A PROFILE RATHER THAN FIELDS ON THE TEMPLATE. Durability is a dozen
    /// numbers, and the templates that share them share ALL of them — every common tree in
    /// the world wants the same 40 hit points, the same axe requirement and the same wood
    /// drops. Inlining that into <see cref="BuildingTemplateData"/> would copy twelve
    /// fields across hundreds of assets and make a balance pass a hundred-file edit.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "DP_NewProfile", menuName = "Valkur/World/Destruction Profile")]
    public class DestructionProfile : ScriptableObject
    {
        [Header("Material")]
        [Tooltip("What this is made of. Chooses the row of DestructionResistanceTable that " +
                 "decides which tools and elements get through.")]
        public MaterialClass material = MaterialClass.Wood;

        [Header("Durability")]
        [Tooltip("Hit points, in units of 'one unmodified blow'. With the shipped table an " +
                 "axe scores its full damage against wood, so this reads directly as " +
                 "axe-damage-to-fell. Sapling 15, common tree 40, ancient 120, barrel 8.")]
        public int durability = 40;

        [Tooltip("Minimum tool tier that counts as the right tool. 0 means bare hands are " +
                 "enough (bushes, crates). Below it, a PHYSICAL blow is scaled by " +
                 "chipDamageFraction — magic is judged by the table alone, since there is " +
                 "no such thing as the wrong tier of fireball.")]
        public int requiredToolTier;

        [Tooltip("What fraction of a physical blow lands when the tool tier is too low. " +
                 "0 makes the building immune without the right tool and is a valid, harsh " +
                 "choice; the default lets a determined player get there eventually.\n\n" +
                 "TUNING IT MAY NOT MOVE WHAT YOU EXPECT. HarvestBlowResolver.Scale never " +
                 "lets a real multiplier round away to nothing, so once a blow is already " +
                 "down to 1 point of work this field changes how many blows are needed by " +
                 "exactly zero: the shipped mine resolves to 0.003 bare-handed and lands 1 " +
                 "either way. The one thing it always controls is whether the blow lands AT " +
                 "ALL: 0 is a refusal, anything above it is slow.")]
        [Range(0f, 1f)] public float chipDamageFraction = 0.15f;

        [Header("Death")]
        [Tooltip("Which death sequence family this belongs to.")]
        public DestructionKind kind = DestructionKind.Fell;

        [Tooltip("Resources-relative sprite that replaces the building once it is destroyed, " +
                 "e.g. 'Buildings/nature/stump_oak'. Empty leaves nothing behind.")]
        public string remainsAssetPath = "";

        [Tooltip("What it drops. None = drops nothing.")]
        public HarvestDropTable drops;

        [Tooltip("Which animation the worker plays per blow, as an AttackVariant reservation " +
                 "key — 'harvest_chop' for a tree, 'harvest_mine' for a seam. Empty, or a key " +
                 "the character has no art for, falls back to the normal attack rotation.")]
        public string swingAnimationKey = "";

        [Header("World")]
        [Tooltip("Seconds until it comes back. 0 = never. A forest regrows; a house does not.")]
        public float regrowSeconds;

        [Tooltip("World-unit radius in which the destruction is heard. Monsters inside it are " +
                 "alerted. 0 = silent.")]
        public float noiseRadius = 12f;

        [Tooltip("If true, the building's collision cells are cleared when it dies, so what " +
                 "remains can be walked over. A stump is walkable; a collapsed wall of rubble " +
                 "is not.")]
        public bool remainsWalkable = true;

        // ── Harvesting ───────────────────────────────────────────────────────────
        // Working a building by hand — chopping, mining, picking — as opposed to hitting
        // it in combat. The two share the resistance matrix and the tool gate on purpose:
        // an axe against stone is the same statement whether it was a swing or a shift at
        // the rock face, and answering it twice is how the two halves drift.

        [Header("Harvesting")]
        [Tooltip("Whether the player can work this by holding the interact key. A profile " +
                 "can be destructible in combat without being harvestable (a barricade) and " +
                 "harvestable without being destructible (a mine).")]
        public bool harvestable;

        [Tooltip("Destroy = work consumes the building (trees, crates). " +
                 "Deplete = work consumes charges and the building survives, spent, until " +
                 "it refills (mines, ore seams).")]
        public HarvestMode harvestMode = HarvestMode.Destroy;

        [Tooltip("Verb shown in the interaction prompt, e.g. 'Chop' / 'Mine' / 'Gather'.")]
        public string harvestVerb = "Harvest";

        [Tooltip("World-unit radius the player must be inside for the prompt to appear. " +
                 "Measured from the FOOTPRINT, so a wide mine entrance is reachable along " +
                 "its whole face rather than only from its pivot.")]
        [Min(0.2f)] public float interactionRadius = 1.6f;

        [Tooltip("Seconds between blows while the session runs. This is the rhythm the " +
                 "whole activity is read through: every blow is an EVENT (a chip, a sound, " +
                 "a possible yield), and continuous motion with no events stops being read " +
                 "after about a second.")]
        [Min(0.05f)] public float secondsPerBlow = 0.65f;

        [Tooltip("Damage one blow is worth BEFORE the resistance matrix and the tool tier " +
                 "gate. In Destroy mode this divides `durability` into the number of blows; " +
                 "in Deplete mode a landed blow always costs exactly one charge, so this " +
                 "only feeds the matrix.\n\n" +
                 "It separates GOOD tools from mediocre ones and does nothing for bad ones: " +
                 "HarvestBlowResolver.Scale never lets a real multiplier round away to zero, " +
                 "so a bare-handed blow lands 1 damage whatever this says. Raising it to make " +
                 "hand-chopping bearable is the reach that does not work.")]
        [Min(1)] public int blowDamage = 10;

        [Tooltip("Deplete mode only: how many charges a full node holds. One charge is " +
                 "consumed per landed blow, so this is the length of a shift at the seam.")]
        [Min(1)] public int charges = 12;

        [Tooltip("Rolled INDEPENDENTLY on every landed blow. This is what a mine actually " +
                 "produces; `drops` stays what is left when a Destroy-mode building dies. " +
                 "A tree normally leaves this empty and pays out once, when it falls.")]
        public HarvestDropTable yieldPerBlow;

        [Tooltip("Weighted pool: ONE item per landed blow, chosen against the others. Takes " +
                 "precedence over yieldPerBlow when both are set.\n\n" +
                 "The two are different questions and a seam wants this one. A HarvestDropTable " +
                 "rolls every line independently, which is right for a tree -- felling an oak " +
                 "always gives wood and sometimes a sapling. A seam gives you ONE thing per " +
                 "swing and the interesting part is WHICH, so the lines have to compete. " +
                 "LootTable derives each line's weight from the item's own rarity when no " +
                 "weight is authored (Common 600, Uncommon 250, Rare 100, Epic 40, Legendary " +
                 "10 per mille), so a pool of sixty-four minerals balances itself and adding a " +
                 "sixty-fifth needs no re-tuning of the other sixty-four.")]
        public LootTable yieldPool;

        [Tooltip("Deplete mode only: colour multiplied over the node while it is spent, " +
                 "until it regrows. Desaturated and slightly darker reads as exhausted " +
                 "without the node vanishing, which is the whole point of the mode.")]
        public Color spentTint = new Color(0.52f, 0.52f, 0.58f, 1f);
    }
}
