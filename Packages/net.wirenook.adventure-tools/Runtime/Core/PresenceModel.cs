using System;
using System.Collections.Generic;

namespace Wirenook.AdventureTools.Core
{
    /// <summary>
    /// Who one of the two settings reaches: who the wearer is shown to, or
    /// who hears them. The numbers are the menu's, and start at one: a menu
    /// switch that is turned off sets its number to nought, which is none
    /// of these, and the animator then puts the number back.
    /// </summary>
    public enum Reach
    {
        /// <summary>Anyone in the room.</summary>
        Everyone = 1,

        /// <summary>The people behind the scenes: everyone who carries the backstage circle.</summary>
        Backstage = 2,

        /// <summary>The people behind the scenes, and whoever the wearer has pointed at and marked.</summary>
        Chosen = 3,
    }

    /// <summary>What one viewer's client knows about itself, which is all a decision can rest on.</summary>
    public readonly struct Viewer
    {
        /// <summary>The wearer's own client.</summary>
        public bool Wearer { get; }

        /// <summary>Carries the backstage circle too.</summary>
        public bool Backstage { get; }

        /// <summary>Has been pointed at and marked.</summary>
        public bool Chosen { get; }

        public Viewer(bool wearer, bool backstage, bool chosen)
        {
            Wearer = wearer;
            Backstage = backstage;
            Chosen = chosen;
        }

        public static Viewer Self => new Viewer(true, false, false);
        public static Viewer Member => new Viewer(false, true, false);
        public static Viewer Marked => new Viewer(false, false, true);
        public static Viewer Stranger => new Viewer(false, false, false);

        public override string ToString() =>
            Wearer ? "the wearer" : Backstage ? (Chosen ? "a chosen member" : "a member") : Chosen ? "a chosen person" : "a stranger";
    }

    /// <summary>A named pair of settings, for one press in the menu.</summary>
    public readonly struct PresencePreset
    {
        /// <summary>What the menu calls it.</summary>
        public string Label { get; }
        public Reach Shown { get; }
        public Reach Heard { get; }

        public PresencePreset(string label, Reach shown, Reach heard)
        {
            Label = label;
            Shown = shown;
            Heard = heard;
        }
    }

    /// <summary>
    /// Presence: who sees the wearer and who hears them, decided on each
    /// viewer's own client from what that client knows. This is the whole
    /// rule, in plain code; the animator is generated to match it and
    /// checked against it.
    ///
    /// The wearer always sees and hears themself. The people behind the
    /// scenes always see and hear each other. Everyone else depends on the
    /// two settings.
    /// </summary>
    public static class PresenceModel
    {
        /// <summary>The wearer's choice of who they are shown to: 1, 2 or 3 (<see cref="Reach"/>). The menu's, never synced.</summary>
        public const string Shown = "Presence/Shown";

        /// <summary>The wearer's choice of who hears them, likewise.</summary>
        public const string Heard = "Presence/Heard";

        /// <summary>The preset being pressed in the menu, from 1; 0 when none is.</summary>
        public const string Preset = "Presence/Preset";

        /// <summary>Synced bool: not shown to everyone.</summary>
        public const string ShownFew = "Presence/ShownFew";

        /// <summary>Synced bool: and, when not, shown to the chosen as well as backstage.</summary>
        public const string ShownChosen = "Presence/ShownChosen";

        /// <summary>Synced bool: not heard by everyone.</summary>
        public const string HeardFew = "Presence/HeardFew";

        /// <summary>Synced bool: and, when not, heard by the chosen as well as backstage.</summary>
        public const string HeardChosen = "Presence/HeardChosen";

        /// <summary>The circle that game masters and spectators carry.</summary>
        public const string BackstageCircle = "Backstage";

        /// <summary>The five ways of being present that are one press each, in menu order.</summary>
        public static readonly IReadOnlyList<PresencePreset> Presets = new[]
        {
            new PresencePreset("Everyone", Reach.Everyone, Reach.Everyone),
            new PresencePreset("Ghost", Reach.Backstage, Reach.Backstage),
            new PresencePreset("Narrate", Reach.Backstage, Reach.Everyone),
            new PresencePreset("Whisper", Reach.Backstage, Reach.Chosen),
            new PresencePreset("Appear", Reach.Chosen, Reach.Chosen),
        };

        /// <summary>The first of the two synced bits a setting travels as: it reaches fewer than everyone.</summary>
        public static bool Few(Reach reach) => reach != Reach.Everyone;

        /// <summary>The second: the chosen are among the few.</summary>
        public static bool WithChosen(Reach reach) => reach == Reach.Chosen;

        /// <summary>The setting two synced bits stand for. The second means nothing without the first.</summary>
        public static Reach FromBits(bool few, bool withChosen) => !few ? Reach.Everyone : withChosen ? Reach.Chosen : Reach.Backstage;

        /// <summary>Whether a setting reaches a viewer.</summary>
        public static bool Reaches(Reach reach, Viewer viewer)
        {
            if (viewer.Wearer || viewer.Backstage) return true;
            switch (reach)
            {
                case Reach.Everyone: return true;
                case Reach.Backstage: return false;
                case Reach.Chosen: return viewer.Chosen;
                default: throw new ArgumentOutOfRangeException(nameof(reach));
            }
        }

        /// <summary>The viewer's client draws the wearer, in one way or the other.</summary>
        public static bool Drawn(Reach shown, Viewer viewer) => Reaches(shown, viewer);

        /// <summary>
        /// The viewer's client draws the wearer as a spectre: the wearer is
        /// not shown to everyone, and this viewer sees them only because
        /// they are the wearer or behind the scenes. Someone chosen who is
        /// not behind the scenes sees the wearer as they are; that is what
        /// being chosen is for.
        /// </summary>
        public static bool Spectral(Reach shown, Viewer viewer) => Few(shown) && (viewer.Wearer || viewer.Backstage);

        /// <summary>The viewer hears the wearer.</summary>
        public static bool Audible(Reach heard, Viewer viewer) => Reaches(heard, viewer);
    }
}
