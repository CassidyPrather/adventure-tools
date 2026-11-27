using NUnit.Framework;
using Wirenook.AdventureTools.Core;

namespace Wirenook.AdventureTools.Tests
{
    public class PresenceModelTests
    {
        private static readonly Viewer[] Viewers =
        {
            Viewer.Self, Viewer.Member, Viewer.Marked, Viewer.Stranger, new Viewer(false, true, true),
        };

        private static PresencePreset Named(string label)
        {
            foreach (var preset in PresenceModel.Presets)
                if (preset.Label == label)
                    return preset;
            Assert.Fail("No preset is called " + label + ".");
            return default;
        }

        [Test]
        public void TheWearerAndTheBackstageAlwaysSeeAndHear()
        {
            foreach (var preset in PresenceModel.Presets)
            {
                foreach (var viewer in new[] { Viewer.Self, Viewer.Member, new Viewer(false, true, true) })
                {
                    Assert.IsTrue(PresenceModel.Drawn(preset.Shown, viewer), preset.Label + ": " + viewer + " sees");
                    Assert.IsTrue(PresenceModel.Audible(preset.Heard, viewer), preset.Label + ": " + viewer + " hears");
                }
            }
        }

        [Test]
        public void EveryoneIsThePlainAvatar()
        {
            var preset = Named("Everyone");
            foreach (var viewer in Viewers)
            {
                Assert.IsTrue(PresenceModel.Drawn(preset.Shown, viewer), viewer + " sees");
                Assert.IsFalse(PresenceModel.Spectral(preset.Shown, viewer), viewer + " sees no spectre");
                Assert.IsTrue(PresenceModel.Audible(preset.Heard, viewer), viewer + " hears");
            }
        }

        [Test]
        public void AGhostIsForTheBackstageAlone()
        {
            var preset = Named("Ghost");
            foreach (var outsider in new[] { Viewer.Stranger, Viewer.Marked })
            {
                Assert.IsFalse(PresenceModel.Drawn(preset.Shown, outsider), outsider + " sees nothing");
                Assert.IsFalse(PresenceModel.Audible(preset.Heard, outsider), outsider + " hears nothing");
            }

            Assert.IsTrue(PresenceModel.Spectral(preset.Shown, Viewer.Member));
            Assert.IsTrue(PresenceModel.Spectral(preset.Shown, Viewer.Self));
        }

        [Test]
        public void NarratingIsHeardByAllAndSeenByNone()
        {
            var preset = Named("Narrate");
            Assert.IsFalse(PresenceModel.Drawn(preset.Shown, Viewer.Stranger));
            Assert.IsTrue(PresenceModel.Audible(preset.Heard, Viewer.Stranger));
            Assert.IsFalse(PresenceModel.Drawn(preset.Shown, Viewer.Marked));
            Assert.IsTrue(PresenceModel.Audible(preset.Heard, Viewer.Marked));
        }

        [Test]
        public void AWhisperIsHeardByTheChosenAndSeenByNone()
        {
            var preset = Named("Whisper");
            Assert.IsFalse(PresenceModel.Drawn(preset.Shown, Viewer.Marked));
            Assert.IsTrue(PresenceModel.Audible(preset.Heard, Viewer.Marked));
            Assert.IsFalse(PresenceModel.Drawn(preset.Shown, Viewer.Stranger));
            Assert.IsFalse(PresenceModel.Audible(preset.Heard, Viewer.Stranger));
        }

        [Test]
        public void AppearingIsSeenAndHeardByTheChosenAsTheAvatarIs()
        {
            var preset = Named("Appear");
            Assert.IsTrue(PresenceModel.Drawn(preset.Shown, Viewer.Marked));
            Assert.IsFalse(PresenceModel.Spectral(preset.Shown, Viewer.Marked), "the chosen see the avatar, not a spectre");
            Assert.IsTrue(PresenceModel.Audible(preset.Heard, Viewer.Marked));
            Assert.IsFalse(PresenceModel.Drawn(preset.Shown, Viewer.Stranger));
            Assert.IsFalse(PresenceModel.Audible(preset.Heard, Viewer.Stranger));
            Assert.IsTrue(PresenceModel.Spectral(preset.Shown, Viewer.Member), "the backstage still see who is not there for everyone");
        }

        [Test]
        public void ASpectreIsOnlyEverSeenByThoseWhoSeeTheWearer()
        {
            foreach (Reach shown in System.Enum.GetValues(typeof(Reach)))
                foreach (var viewer in Viewers)
                    if (PresenceModel.Spectral(shown, viewer))
                        Assert.IsTrue(PresenceModel.Drawn(shown, viewer), shown + ", " + viewer);
        }

        [Test]
        public void ASettingSurvivesItsTwoBits()
        {
            foreach (Reach reach in System.Enum.GetValues(typeof(Reach)))
                Assert.AreEqual(reach, PresenceModel.FromBits(PresenceModel.Few(reach), PresenceModel.WithChosen(reach)));
            Assert.AreEqual(Reach.Everyone, PresenceModel.FromBits(false, true), "the second bit means nothing without the first");
        }

        [Test]
        public void TheFiveCasesAreAllThere()
        {
            Assert.AreEqual(5, PresenceModel.Presets.Count);
            Assert.AreEqual(Reach.Backstage, Named("Ghost").Heard);
            Assert.AreEqual(Reach.Everyone, Named("Narrate").Heard);
            Assert.AreEqual(Reach.Chosen, Named("Whisper").Heard);
            Assert.AreEqual(Reach.Chosen, Named("Appear").Shown);
        }
    }
}
