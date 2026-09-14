namespace Valkur.Data
{
    /// <summary>
    /// Whether the game can actually TRAIN a skill yet — the single answer to "is this row of the
    /// skills table live or 'próximamente'".
    ///
    /// <para><b>DERIVED FROM CONTENT, NEVER AUTHORED.</b> A "coming soon" checkbox is a flag that
    /// outlives the day fishing ships and keeps a working skill greyed out, or is cleared early
    /// and shows a live row nothing can raise — both silently. What makes a skill trainable is
    /// already in the data: a gathering skill has nodes that teach it, a crafting skill has at
    /// least one recipe that could be made in a trade that points at it.</para>
    /// </summary>
    public static class SkillAvailability
    {
        public static bool IsTrainable(SkillDefinition skill, RecipeCatalog recipes)
        {
            if (skill == null) return false;

            // A physical skill is trained by the body, and every character has one: running needs
            // no node and no recipe, so it is live the moment it exists.
            if (skill.category == SkillCategory.Physical) return true;

            if (skill.trainingNodes != null)
                for (int i = 0; i < skill.trainingNodes.Count; i++)
                    if (skill.trainingNodes[i] != null) return true;

            return CountRecipes(skill, recipes) > 0;
        }

        /// <summary>Well-formed recipes whose trade trains <paramref name="skill"/>.</summary>
        public static int CountRecipes(SkillDefinition skill, RecipeCatalog recipes)
        {
            if (skill == null || recipes == null) return 0;
            int count = 0;
            var list = recipes.Recipes;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r != null && r.IsWellFormed && r.profession.skill == skill) count++;
            }
            return count;
        }
    }
}
