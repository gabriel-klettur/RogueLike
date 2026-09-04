using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// What the WORLD is doing while a conversation happens, reduced to the one thing the
    /// chat panel can show: a face.
    ///
    /// <para>THE WEAKEST OF THE THREE LAYERS, AND THAT IS THE DESIGN. A face is answered by
    /// what the character SAID first (<see cref="ExpressionClassifier"/>), then by what the
    /// player DID to them (<see cref="DialogueIntent"/>), and only then by this. The order
    /// is not a preference: a line written to laugh must not be delivered with a tired face
    /// because it happens to be three in the morning, and a character being called a thief
    /// has something far more immediate to react to than the weather. So this layer speaks
    /// only into a silence — when the words gave nothing away and the player did nothing
    /// worth reacting to, which measured over Gatita's own repertoire is the majority of
    /// exchanges.</para>
    ///
    /// <para>Both signals are things the player can VERIFY by looking around, which is the
    /// bar a mood has to clear to be worth showing. A vendor yawning at dawn and a vendor
    /// uneasy in a thunderstorm are both statements about a world the player is standing
    /// in; a mood drawn from a hidden counter would just be the portrait changing for no
    /// reason. That is also why <c>friendshipScore</c> is NOT consulted — nothing in
    /// production writes it, so a face resting on it would never change.</para>
    ///
    /// <para>Read fresh per reply rather than captured when the conversation opened, for
    /// the same reason <c>BuildTradeContext</c> is: a storm can break, or dawn arrive,
    /// while the player is still typing.</para>
    /// </summary>
    public readonly struct ChatMoodContext
    {
        /// <summary>The phase of the day/night cycle, or Day when there is no cycle.</summary>
        public readonly DayNightCycle.DayPhase Phase;

        /// <summary>The heaviest weather running in the player's zone.</summary>
        public readonly WeatherIntensity Weather;

        /// <summary>True when a cycle was actually found, so Day is a reading and not a default.</summary>
        public readonly bool HasCycle;

        public ChatMoodContext(DayNightCycle.DayPhase phase, WeatherIntensity weather, bool hasCycle)
        {
            Phase = phase;
            Weather = weather;
            HasCycle = hasCycle;
        }

        /// <summary>
        /// The live world. Null-safe in every direction: a test scene, an EditMode fixture
        /// and a boot that has not built the weather manager yet all resolve to
        /// <see cref="None"/> rather than throwing inside a conversation.
        /// </summary>
        public static ChatMoodContext FromLive()
        {
            bool hasCycle = DayNightCycle.HasInstance;
            DayNightCycle.DayPhase phase = hasCycle
                ? DayNightCycle.Instance.CurrentPhase
                : DayNightCycle.DayPhase.Day;

            WeatherIntensity weather = WeatherIntensity.Off;
            WeatherManager manager = WeatherManager.Instance;
            if (manager != null)
            {
                // The heaviest of the three, because what a person reacts to is the worst of
                // what is falling on them, not the sum. Rain and wind together is a storm,
                // and a storm is exactly as unsettling as its heavier half.
                weather = Heaviest(manager.LevelOf(WeatherType.Rain), manager.LevelOf(WeatherType.Wind));
                weather = Heaviest(weather, manager.LevelOf(WeatherType.Snow));
            }

            return new ChatMoodContext(phase, weather, hasCycle);
        }

        /// <summary>A world that has nothing to say about anyone's mood.</summary>
        public static ChatMoodContext None =>
            new ChatMoodContext(DayNightCycle.DayPhase.Day, WeatherIntensity.Off, false);

        /// <summary>
        /// The face this world suggests, or <see cref="FacialExpression.Neutral"/> for the
        /// ordinary case of a clear afternoon.
        ///
        /// <para>Weather is tested before the hour because it is the more acute of the two:
        /// someone caught in a heavy storm at midnight is worried about the storm, and will
        /// be tired later. Only HEAVY weather counts — light rain is scenery, and a vendor
        /// visibly unsettled by drizzle is a vendor who looks unsettled most of the time,
        /// which spends the expression for nothing.</para>
        /// </summary>
        public FacialExpression SuggestedFace()
        {
            if (Weather == WeatherIntensity.Heavy) return FacialExpression.Worry;

            if (HasCycle && IsSmallHours(Phase)) return FacialExpression.Tired;

            return FacialExpression.Neutral;
        }

        /// <summary>
        /// The part of the cycle a market trader has no business being awake for.
        ///
        /// Dawn and BlueHour are deliberately included and Dusk is not: the first two are
        /// the far side of a night nobody slept through, while dusk is the end of an
        /// ordinary working day.
        /// </summary>
        private static bool IsSmallHours(DayNightCycle.DayPhase phase) =>
            phase == DayNightCycle.DayPhase.Night ||
            phase == DayNightCycle.DayPhase.Dawn ||
            phase == DayNightCycle.DayPhase.BlueHour;

        private static WeatherIntensity Heaviest(WeatherIntensity a, WeatherIntensity b) =>
            (int)a >= (int)b ? a : b;
    }
}
