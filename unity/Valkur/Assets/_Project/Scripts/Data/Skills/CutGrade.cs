namespace Valkur.Data
{
    /// <summary>
    /// How well a tapped cut landed against the beat: six grades, from the centre of the target
    /// outward. The first five land a blow worth less the further out they are; the sixth does not
    /// land at all.
    ///
    /// <para><b>A LADDER, NOT A SWITCH.</b> The rhythm gear used to be binary — inside the window a
    /// hit, outside it a miss — so a tap 1 ms outside cost as much as one half a beat off, and a tap
    /// 1 ms inside paid as much as a dead-centre one. A grade makes every millisecond of timing
    /// worth something, which is what a player can actually get better at.</para>
    ///
    /// <para><b>ORDERED BY DISTANCE, SO THE VALUE IS BEHAVIOUR.</b> Perfect is the centre and
    /// Soquete the outside; code compares grades by value (a Bad or worse breaks the combo). Never
    /// renumber — only append.</para>
    /// </summary>
    public enum CutGrade
    {
        /// <summary>Not a tapped cut: the automatic swing, or the tap that starts tapping.</summary>
        None = 0,

        /// <summary>The centre of the target. Worth more than the automatic swing.</summary>
        Perfect = 1,

        /// <summary>Inside the target's inner ring. A full blow.</summary>
        Good = 2,

        /// <summary>Inside the hit window. A three-quarter blow; the combo survives.</summary>
        Ok = 3,

        /// <summary>Just outside the window. A weak blow; the combo breaks.</summary>
        Bad = 4,

        /// <summary>On the target's outer ring. A glancing blow; the combo breaks.</summary>
        Awful = 5,

        /// <summary>Off the target. No blow, the beat is lost, and the player is told about it.</summary>
        Soquete = 6,
    }
}
