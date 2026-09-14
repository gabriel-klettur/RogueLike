using Valkur.Data.WorldGen;
using Valkur.Gameplay.World.Generation;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// "Partida con semilla": the main menu's door into a run that walks out of Pepitoria into a
    /// freshly generated world. Seed World lab only — with the lab off the row is not built at all
    /// (<see cref="SeedWorldLab"/>).
    ///
    /// <para><b>A new game in every other respect.</b> Same class selector, same
    /// <c>StartNewGame</c>. What the row adds is ONE request, armed at the moment the new game
    /// really starts (<see cref="SeedWorldNewGame.Request"/>), with a random seed: the menu kit has
    /// no text field, and <c>seedworld nueva &lt;semilla&gt;</c> is the door for a chosen one.</para>
    ///
    /// <para><b>Every other way into the game withdraws it.</b> A request outlives the menu — it
    /// waits for the next gameplay boot — so one left armed by a selector the player backed out of
    /// would turn the next Continue or Load into a trip to a world they never asked for.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private bool _seededNewGameChosen;

        private static bool SeededNewGameOffered => SeedWorldNewGame.Available;

        /// <summary>Both new-game rows open the class selector; only the seeded one remembers it.</summary>
        private void ChooseNewGame(bool seeded)
        {
            _seededNewGameChosen = seeded && SeededNewGameOffered;
            OpenClassSelector();
        }

        /// <summary>
        /// Called as a new game starts: arms a generated world when the seeded row chose it, and
        /// withdraws any stale request otherwise. The lab is asked again here — it can be switched
        /// off between opening the selector and confirming a class.
        /// </summary>
        private void ArmOrWithdrawSeededWorld()
        {
            bool seeded = _seededNewGameChosen && SeededNewGameOffered;
            _seededNewGameChosen = false;
            if (seeded) SeedWorldNewGame.Request(WorldSeed.NewRandom(new System.Random(System.Environment.TickCount)));
            else SeedWorldNewGame.Cancel();
        }

        /// <summary>Continue and Load resume a run in Pepitoria; neither may start a generated world.</summary>
        private void WithdrawSeededWorld()
        {
            _seededNewGameChosen = false;
            SeedWorldNewGame.Cancel();
        }
    }
}
