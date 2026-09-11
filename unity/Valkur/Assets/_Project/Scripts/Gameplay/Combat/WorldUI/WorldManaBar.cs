using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The mana driver. Reports a number to <see cref="WorldBarRig"/>, which draws it on the
    /// resource row beside the dash pip.
    ///
    /// <para>It used to be a full-width strip of its own, identical in shape to the dash bar
    /// below it and to the health bar below that — three rows of the same rectangle, separated
    /// only by hue, over a character 149 screen pixels tall. It is a thinner row now, and it
    /// shares that row with the dash charge, so the stack is two rows instead of three.</para>
    ///
    /// <para>A spend leaves the same delayed ghost a blow leaves on health. That is the only
    /// feedback the old bar had at all, and it had it in the one place it was useless: a
    /// sinusoidal flash once mana reached exactly zero, i.e. after every decision had been made.</para>
    /// </summary>
    public class WorldManaBar : MonoBehaviour
    {
        private Mana _mana;
        private WorldBarRig _rig;
        private int _lastMana = int.MinValue;

        private void Awake()
        {
            _mana = GetComponent<Mana>();
            if (_mana == null) return;   // no mana, no row. Monsters keep a health bar only.
            _rig = WorldBarRig.Ensure(gameObject);
            _rig.EnableMana(true);
        }

        private void OnEnable()
        {
            if (_mana == null || _rig == null) return;
            _rig.EnableMana(true);
            _mana.OnManaChanged += OnManaChanged;
            _lastMana = int.MinValue;
            OnManaChanged(_mana.CurrentMana, _mana.MaxMana);
        }

        private void OnDisable()
        {
            if (_mana != null) _mana.OnManaChanged -= OnManaChanged;
            // UnconsciousState disables this driver; the row goes with it rather than hanging
            // over a body that is no longer casting anything.
            _rig?.EnableMana(false);
        }

        private void OnManaChanged(int current, int max)
        {
            if (_rig == null) return;

            // A spend is the only mana movement worth marking. Regeneration arrives one point at
            // a time, several times a second, and a ghost behind each of them would be a
            // permanent bright smear where the bar happens to be.
            WorldBarChange change = _lastMana != int.MinValue && current < _lastMana
                ? WorldBarChange.Damage
                : WorldBarChange.Silent;

            _lastMana = current;
            _rig.SetMana(current, max, change);
        }
    }
}
