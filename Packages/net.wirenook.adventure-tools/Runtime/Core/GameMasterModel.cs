using System;
using System.Collections.Generic;

namespace Wirenook.AdventureTools.Core
{
    /// <summary>What can be rolled. The numbers are the menu's and the synced kind's.</summary>
    public enum Roll
    {
        None = 0,
        Coin = 1,
        D6 = 2,
        TwoD6 = 3,
        D20 = 4,
        TwoD20 = 5,
    }

    /// <summary>
    /// The dice, in plain code: what each roll is made of, how a result
    /// travels, and which strokes of a seven-stroke numeral are drawn for
    /// it. The animator is generated from this and checked against it.
    ///
    /// A result is two whole numbers (the second is nought for one die) and
    /// which roll it was, as three bits. The numbers are drawn by the
    /// wearer's client and synced, so everyone shows the same ones.
    /// </summary>
    public static class DiceModel
    {
        /// <summary>The most one die shows, and the most two show together.</summary>
        public const int MaxFace = 20;
        public const int MaxSum = 2 * MaxFace;

        /// <summary>Bits the kind of roll travels as.</summary>
        public const int KindBits = 3;

        public static readonly IReadOnlyList<Roll> Rolls = new[] { Roll.Coin, Roll.D6, Roll.TwoD6, Roll.D20, Roll.TwoD20 };

        public static string Label(Roll roll)
        {
            switch (roll)
            {
                case Roll.Coin: return "Coin";
                case Roll.D6: return "1d6";
                case Roll.TwoD6: return "2d6";
                case Roll.D20: return "1d20";
                case Roll.TwoD20: return "2d20";
                default: return "None";
            }
        }

        /// <summary>How many faces each die of the roll has; a coin has two.</summary>
        public static int Faces(Roll roll)
        {
            switch (roll)
            {
                case Roll.Coin: return 2;
                case Roll.D6:
                case Roll.TwoD6: return 6;
                case Roll.D20:
                case Roll.TwoD20: return 20;
                default: return 0;
            }
        }

        /// <summary>How many dice the roll throws.</summary>
        public static int Dice(Roll roll)
        {
            return roll == Roll.TwoD6 || roll == Roll.TwoD20 ? 2 : roll == Roll.None ? 0 : 1;
        }

        /// <summary>One of the three bits a kind travels as, counted from 0.</summary>
        public static bool Bit(Roll roll, int bit) => (((int)roll >> bit) & 1) == 1;

        public static Roll FromBits(bool bit0, bool bit1, bool bit2)
        {
            var value = (bit0 ? 1 : 0) | (bit1 ? 2 : 0) | (bit2 ? 4 : 0);
            return value <= (int)Roll.TwoD20 ? (Roll)value : Roll.None;
        }

        /// <summary>A coin's first face is heads.</summary>
        public static bool Heads(int face) => face == 1;

        // The strokes of a numeral: a the top, b and c down the right, d the
        // bottom, e and f up the left, g across the middle.
        public const string Strokes = "abcdefg";

        private static readonly string[] Numerals =
        {
            "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg",
        };

        /// <summary>Whether a numeral (0 to 9) has a stroke.</summary>
        public static bool Drawn(int numeral, char stroke)
        {
            if (numeral < 0 || numeral > 9) throw new ArgumentOutOfRangeException(nameof(numeral));
            return Numerals[numeral].IndexOf(stroke) >= 0;
        }

        /// <summary>
        /// Whether a stroke is drawn in one place of a number of up to two
        /// places. Nought draws nothing at all, and a number under ten
        /// leaves its tens blank.
        /// </summary>
        /// <param name="tens">The left-hand place.</param>
        public static bool Drawn(int value, bool tens, char stroke)
        {
            if (value <= 0) return false;
            if (value > 99) throw new ArgumentOutOfRangeException(nameof(value));
            if (tens) return value >= 10 && Drawn(value / 10, stroke);
            return Drawn(value % 10, stroke);
        }
    }

    /// <summary>How big a zone or a pause is, from the dial that sets it.</summary>
    public static class ZoneModel
    {
        /// <summary>Metres across half of the smallest and the largest.</summary>
        public const float SmallestRadius = 1f;
        public const float LargestRadius = 16f;

        /// <summary>Where the dial rests: three metres.</summary>
        public static readonly float RestingAmount = Amount(3f);

        /// <summary>
        /// The radius a dial at <paramref name="amount"/> (0 to 1) gives.
        /// Each step of the dial multiplies the size, so the small sizes,
        /// where a metre matters, get as much of its travel as the large.
        /// </summary>
        public static float Radius(float amount)
        {
            if (amount < 0f) amount = 0f;
            if (amount > 1f) amount = 1f;
            return SmallestRadius * (float)Math.Pow(LargestRadius / SmallestRadius, amount);
        }

        /// <summary>The dial's amount for a radius: the inverse of <see cref="Radius"/>.</summary>
        public static float Amount(float radius)
        {
            if (radius <= SmallestRadius) return 0f;
            if (radius >= LargestRadius) return 1f;
            return (float)(Math.Log(radius / SmallestRadius) / Math.Log(LargestRadius / SmallestRadius));
        }
    }

    /// <summary>One trait the skeleton key can send or feel, and the family it is shown with.</summary>
    public readonly struct KeyTrait
    {
        /// <summary>VRChat's own spelling of the contact tag.</summary>
        public string Tag { get; }

        /// <summary>The family: its menu folder, and its colour on the key.</summary>
        public string Family { get; }

        public KeyTrait(string tag, string family)
        {
            Tag = tag;
            Family = family;
        }
    }

    /// <summary>
    /// The traits of the skeleton key: VRChat's nineteen built-in "object
    /// trait" contact tags, the standard by which one person's avatar or
    /// world acts on another's. Their place in this list is the synced
    /// number that chooses one, counted from 1, so append and never reorder.
    /// </summary>
    public static class KeyModel
    {
        public static readonly IReadOnlyList<KeyTrait> Traits = new[]
        {
            new KeyTrait("Fire", "Heat"), new KeyTrait("Hot", "Heat"),
            new KeyTrait("Cold", "Cold"), new KeyTrait("Freezer", "Cold"),
            new KeyTrait("Water", "Water"), new KeyTrait("Wet", "Water"),
            new KeyTrait("Wind", "Wind"),
            new KeyTrait("Damage", "Harm"), new KeyTrait("DamageBlunt", "Harm"), new KeyTrait("DamageSharp", "Harm"),
            new KeyTrait("Weapon", "Harm"), new KeyTrait("Projectile", "Harm"),
            new KeyTrait("Shield", "Gear"), new KeyTrait("Ammunition", "Gear"),
            new KeyTrait("Consumable", "Food"), new KeyTrait("ConsumableFood", "Food"), new KeyTrait("ConsumableDrink", "Food"),
            new KeyTrait("Brush", "Craft"), new KeyTrait("Dye", "Craft"),
        };

        /// <summary>The families in the order they first appear.</summary>
        public static IReadOnlyList<string> Families
        {
            get
            {
                var families = new List<string>();
                foreach (var trait in Traits)
                    if (!families.Contains(trait.Family))
                        families.Add(trait.Family);
                return families;
            }
        }

        /// <summary>The number that chooses a trait, from 1; 0 when the list has no such tag.</summary>
        public static int Number(string tag)
        {
            for (var index = 0; index < Traits.Count; index++)
                if (Traits[index].Tag == tag)
                    return index + 1;
            return 0;
        }
    }
}
