using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Wirenook.AdventureTools.Core;

namespace Wirenook.AdventureTools.Tests
{
    public class GameMasterModelTests
    {
        [Test]
        public void EveryRollSurvivesItsThreeBits()
        {
            foreach (var roll in DiceModel.Rolls.Concat(new[] { Roll.None }))
            {
                var back = DiceModel.FromBits(DiceModel.Bit(roll, 0), DiceModel.Bit(roll, 1), DiceModel.Bit(roll, 2));
                Assert.AreEqual(roll, back);
            }
        }

        [Test]
        public void BitsThatNameNoRollAreNoRoll()
        {
            Assert.AreEqual(Roll.None, DiceModel.FromBits(false, true, true));
            Assert.AreEqual(Roll.None, DiceModel.FromBits(true, true, true));
        }

        [Test]
        public void RollsAreMadeOfWhatTheirNamesSay()
        {
            Assert.AreEqual(2, DiceModel.Faces(Roll.Coin));
            Assert.AreEqual(1, DiceModel.Dice(Roll.Coin));
            Assert.AreEqual(6, DiceModel.Faces(Roll.TwoD6));
            Assert.AreEqual(2, DiceModel.Dice(Roll.TwoD6));
            Assert.AreEqual(20, DiceModel.Faces(Roll.D20));
            Assert.AreEqual(1, DiceModel.Dice(Roll.D20));
            Assert.AreEqual(2, DiceModel.Dice(Roll.TwoD20));
            Assert.AreEqual(0, DiceModel.Dice(Roll.None));
            Assert.IsTrue(DiceModel.Rolls.All(roll => DiceModel.Faces(roll) * DiceModel.Dice(roll) <= DiceModel.MaxSum));
        }

        private static string Strokes(int numeral)
        {
            return new string(DiceModel.Strokes.Where(stroke => DiceModel.Drawn(numeral, stroke)).ToArray());
        }

        [Test]
        public void NoTwoNumeralsLookAlike()
        {
            var seen = new HashSet<string>();
            for (var numeral = 0; numeral <= 9; numeral++) Assert.IsTrue(seen.Add(Strokes(numeral)), "numeral " + numeral);
            Assert.AreEqual("bc", Strokes(1));
            Assert.AreEqual("abcdefg", Strokes(8));
        }

        [Test]
        public void NoughtDrawsNothingAndSmallNumbersLeaveTheirTensBlank()
        {
            foreach (var stroke in DiceModel.Strokes)
            {
                Assert.IsFalse(DiceModel.Drawn(0, true, stroke));
                Assert.IsFalse(DiceModel.Drawn(0, false, stroke));
                Assert.IsFalse(DiceModel.Drawn(7, true, stroke));
                Assert.AreEqual(DiceModel.Drawn(7, stroke), DiceModel.Drawn(7, false, stroke));
                Assert.AreEqual(DiceModel.Drawn(2, stroke), DiceModel.Drawn(20, true, stroke));
                Assert.AreEqual(DiceModel.Drawn(0, stroke), DiceModel.Drawn(20, false, stroke));
                Assert.AreEqual(DiceModel.Drawn(4, stroke), DiceModel.Drawn(40, true, stroke));
            }
        }

        [Test]
        public void TheDialRunsFromTheSmallestZoneToTheLargest()
        {
            Assert.AreEqual(ZoneModel.SmallestRadius, ZoneModel.Radius(0f), 1e-4f);
            Assert.AreEqual(ZoneModel.LargestRadius, ZoneModel.Radius(1f), 1e-3f);
            Assert.AreEqual(ZoneModel.SmallestRadius, ZoneModel.Radius(-1f), 1e-4f);
            Assert.AreEqual(ZoneModel.LargestRadius, ZoneModel.Radius(2f), 1e-3f);
            var last = 0f;
            for (var step = 0; step <= 20; step++)
            {
                var radius = ZoneModel.Radius(step / 20f);
                Assert.Greater(radius, last);
                last = radius;
            }
        }

        [Test]
        public void TheDialAndTheRadiusUndoEachOther()
        {
            foreach (var radius in new[] { 1f, 1.5f, 3f, 8f, 16f })
                Assert.AreEqual(radius, ZoneModel.Radius(ZoneModel.Amount(radius)), 1e-3f);
            Assert.AreEqual(3f, ZoneModel.Radius(ZoneModel.RestingAmount), 1e-3f);
            Assert.AreEqual(0f, ZoneModel.Amount(0.2f));
            Assert.AreEqual(1f, ZoneModel.Amount(100f));
        }

        [Test]
        public void TheKeyHasVrchatsNineteenTraitsOnceEach()
        {
            var tags = KeyModel.Traits.Select(trait => trait.Tag).ToList();
            Assert.AreEqual(19, tags.Count);
            Assert.AreEqual(19, tags.Distinct().Count());
            CollectionAssert.Contains(tags, "Fire");
            CollectionAssert.Contains(tags, "ConsumableDrink");
            for (var index = 0; index < tags.Count; index++) Assert.AreEqual(index + 1, KeyModel.Number(tags[index]));
            Assert.AreEqual(0, KeyModel.Number("Nonsense"));
        }

        [Test]
        public void EveryTraitHasAFamilyAndNoFamilyIsEmpty()
        {
            var families = KeyModel.Families;
            Assert.AreEqual(8, families.Count);
            foreach (var family in families) Assert.IsTrue(KeyModel.Traits.Any(trait => trait.Family == family));
            Assert.IsTrue(KeyModel.Traits.All(trait => families.Contains(trait.Family)));
        }
    }
}
