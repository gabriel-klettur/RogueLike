namespace Valkur.Data
{
    /// <summary>Which group of the skills table a skill belongs to.</summary>
    public enum SkillCategory
    {
        /// <summary>Worked on nodes in the world: woodcutting, mining, fishing.</summary>
        Gathering = 0,

        /// <summary>Trained by recipes at a station or in the bag: cooking, blacksmithing.</summary>
        Crafting = 1,

        /// <summary>Trained by what the BODY does: running. Appended, never inserted — a skill
        /// asset serializes this as its integer.</summary>
        Physical = 2,
    }
}
