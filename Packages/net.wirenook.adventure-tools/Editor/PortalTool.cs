using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.SelectiveAnimation.Editor;
using Station = VRC.SDKBase.VRCStation;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// A portal with two ends, both set down on the stage: a door that
    /// anyone can click, and where it leads. With no second end set down,
    /// it leads to the game master, wherever they have gone since: the
    /// catch-up portal of net.wirenook.avatar-stations, for the player who
    /// was left behind.
    ///
    /// It is one station split in two. The part that is clicked stays at
    /// the door; where the traveller arrives is a point that sits on the
    /// second end while that is on show, and just behind the wearer while
    /// it is not. The traveller arrives standing and lets go by walking.
    ///
    /// The stage keeps things upright in the world's axes and syncs no
    /// heading, so the door is a round gate that looks the same from every
    /// side.
    ///
    /// One station of the six an avatar may have. Synced: a bit for each
    /// end (the stage's).
    /// </summary>
    public sealed class PortalTool : IStageTool
    {
        public const string DoorName = "Door";
        public const string ExitName = "Exit";

        private Transform _door;
        private Transform _swirl;
        private Transform _exit;
        private Transform _arrival;
        private Transform _station;

        public void Things(StagePlan plan)
        {
            plan.Thing(DoorName);
            plan.Thing(ExitName);
        }

        public void Build(GameMaster kit)
        {
            var door = kit.Stage[DoorName];
            var exit = kit.Stage[ExitName];
            _door = kit.Stage.Hold(DoorName, door);
            _swirl = GameMasterBuild.Bone(_door.gameObject, "Swirl", new Vector3(0f, 1.05f, 0f));
            _exit = kit.Stage.Hold(ExitName, exit);

            // A gate of three hoops on four posts, and motes going round inside it.
            var solid = kit.Solid.On(_door);
            foreach (var height in new[] { 0.02f, 1.05f, 2.08f })
                solid.Colour("glow-cyan").Bend(0.5f, 0.54f, height - 0.02f, height + 0.02f, 0f, 360f, 20);
            for (var post = 0; post < 4; post++)
            {
                var around = (post * 90f + 45f) * Mathf.Deg2Rad;
                solid.Colour("night-2").Box(new Vector3(Mathf.Cos(around) * 0.52f, 1.05f, Mathf.Sin(around) * 0.52f), new Vector3(0.05f, 2.1f, 0.05f));
            }

            solid = kit.Solid.On(_swirl).Colour("glow-cyan");
            for (var mote = 0; mote < 6; mote++)
            {
                var around = mote * 60f * Mathf.Deg2Rad;
                solid.Ball(new Vector3(Mathf.Cos(around) * 0.3f, (mote % 3 - 1) * 0.45f, Mathf.Sin(around) * 0.3f), 0.05f, 3, 6);
            }

            // Where it leads: a pad on the ground.
            kit.Solid.On(_exit).Colour("glow-cyan").Bend(0.42f, 0.46f, 0f, 0.015f, 0f, 360f, 20)
                .Colour("white").Bend(0.1f, 0.16f, 0f, 0.012f, 0f, 360f, 10);

            // Just behind the wearer, for a portal with one end.
            var behind = GameMasterBuild.Bone(kit.Child("Portal"), "Behind", new Vector3(0f, 0f, -0.8f));
            _arrival = GameMasterBuild.Bone(kit.Child("Portal"), "Arrival", new Vector3(0f, 0f, -0.8f));
            var arrive = FeaturePrefab.Ensure<VRCParentConstraint>(_arrival.gameObject);
            arrive.ClearSources();
            arrive.AddSource(new VRCConstraintSource { SourceTransform = exit.Pose, Weight = 0f });
            arrive.AddSource(new VRCConstraintSource { SourceTransform = behind, Weight = 1f });
            arrive.IsActive = true;
            // Unlocked constraints re-measure their offset instead of moving the object.
            arrive.Locked = true;
            arrive.FreezeToWorld = false;
            arrive.RebakeOffsetsWhenUnfrozen = false;

            _station = GameMasterBuild.Bone(_door.gameObject, "Station", Vector3.zero);
            var station = FeaturePrefab.Ensure<VRCStation>(_station.gameObject);
            station.PlayerMobility = Station.Mobility.Immobilize;
            // Standing, not seated: the traveller arrives on their feet and walks off to let go.
            station.seated = false;
            station.canUseStationFromStation = true;
            station.disableStationExit = false;
            station.stationEnterPlayerLocation = _arrival;
            // VRChat rejects stations whose entry and exit are more than two
            // metres apart; using one transform for both cannot break that.
            station.stationExitPlayerLocation = _arrival;

            // The collider is what a player clicks. It starts disabled and
            // the animator enables it, while the station component stays
            // enabled throughout: one that is disabled at upload never works.
            var collider = FeaturePrefab.Ensure<BoxCollider>(_station.gameObject);
            collider.isTrigger = true;
            collider.size = new Vector3(1f, 2.1f, 1f);
            collider.center = new Vector3(0f, 1.05f, 0f);
            collider.enabled = false;

            WirenookSounds.Source(_door.gameObject, shared: false);
        }

        public void Layers(GameMaster kit)
        {
            var fx = kit.Fx;
            var door = kit.Stage[DoorName];
            var exit = kit.Stage[ExitName];
            var stationPath = kit.Path(_station);
            var swirlPath = kit.Path(_swirl);

            var states = kit.Stage.Show(fx, "Portal Door", new[] { kit.Path(_door) }, door.Shown, (clip, phase) =>
            {
                if (phase != "on") return;
                clip.Curve(swirlPath, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 4f, 360f)).Loop();
            });
            WirenookSounds.PlayOnEnter(states.Appear, kit.Path(_door) + "/" + WirenookSounds.SourceName, "portal");

            // It can be stepped through once it stands for everyone, not
            // while the wearer is still carrying it about. On Android and
            // iOS there is no collider to switch (VRChat gives each station
            // a trigger itself), but a station scaled to nothing has no
            // size to click.
            AnimationClip Gate(bool open) => fx.Clip(open ? "portal open" : "portal shut", clip =>
            {
                clip.Constant(stationPath, typeof(BoxCollider), "m_Enabled", open ? 1f : 0f);
                PopToggle.Scale(clip, stationPath, AnimationCurve.Constant(0f, 1f / 60f, open ? 1f : 0f));
            });
            door.Standing.Layer(fx, "Portal Gate", Gate(true), Gate(false));

            kit.Stage.Show(fx, "Portal Exit", new[] { kit.Path(_exit) }, exit.Shown);

            var arrivalPath = kit.Path(_arrival);
            AnimationClip Leads(bool toExit) => fx.Clip(toExit ? "portal leads to its exit" : "portal leads to the wearer", clip => clip
                .Constant(arrivalPath, typeof(VRCParentConstraint), "Sources.source0.Weight", toExit ? 1f : 0f)
                .Constant(arrivalPath, typeof(VRCParentConstraint), "Sources.source1.Weight", toExit ? 0f : 1f));
            exit.Standing.Layer(fx, "Portal Leads", Leads(true), Leads(false));
        }

        public void Menu(GameMaster kit)
        {
            var folder = kit.Folder("Portal", "teleport");
            kit.Stage.MenuAim(FeaturePrefab.Child(folder, "Menu Aim Door"), kit.Stage[DoorName], "Aim the door", "teleport");
            kit.Stage.MenuShown(FeaturePrefab.Child(folder, "Menu Door"), kit.Stage[DoorName], "Door open", "teleport");
            kit.Stage.MenuAim(FeaturePrefab.Child(folder, "Menu Aim Exit"), kit.Stage[ExitName], "Aim the way out", "teleport-pair");
            kit.Stage.MenuShown(FeaturePrefab.Child(folder, "Menu Exit"), kit.Stage[ExitName], "Leads there (off: to me)", "catch-up");
            kit.Stage.MenuSet(FeaturePrefab.Child(folder, "Menu Set"));
        }
    }

    /// <summary>
    /// The skeleton key: a plain hand tool that sends any one of VRChat's
    /// nineteen built-in trait tags by touch, chosen from the menu, and
    /// feels for all of them at once, lighting a pip on its dial for each
    /// trait something is giving off. For setting off, and finding out
    /// about, anything made with those tags: this package's sibling
    /// net.wirenook.contacts, other people's avatars, worlds.
    ///
    /// A sender's tags cannot be changed while it is worn, so there is one
    /// sender for each trait, switched on by the choice: nineteen contacts,
    /// and a twentieth to feel with. That is most of what an avatar may
    /// carry before it is ranked Poor on PC, and more than Android and iOS
    /// allow at Medium, so mobile builds leave the contacts and the menu
    /// entry out and the key is never taken out there.
    ///
    /// It sends whether or not the wearer is shown: a hidden game master
    /// can still light the fire.
    ///
    /// Synced: out (a bit) and the trait (eight).
    /// </summary>
    public sealed class KeyTool : IStageTool
    {
        public static string Out(string prefix) => prefix + "/Key";
        public static string Trait(string prefix) => prefix + "/Key/Trait";
        private static string Feel(string prefix) => prefix + "/Key/Feel";

        /// <summary>On each client: the key feels this trait. Written by VRChat for the receiver.</summary>
        public static string Feels(string prefix, string tag) => Feel(prefix) + "_CollisionTag_" + tag;

        /// <summary>The blendshape that takes a trait's pip off the dial.</summary>
        public static string Pip(string tag) => "key.feel." + tag;

        /// <summary>The blendshape that takes a family's stone out of the middle of the dial.</summary>
        public static string Stone(string family) => "key.send." + family;

        /// <summary>How far the key reaches, in metres, about the middle of its dial.</summary>
        public const float Reach = 0.08f;

        private static readonly Dictionary<string, string> Swatches = new Dictionary<string, string>
        {
            { "Heat", "orange" }, { "Cold", "ice" }, { "Water", "signal-blue" }, { "Wind", "mint" },
            { "Harm", "glow-red" }, { "Gear", "silver" }, { "Food", "banana" }, { "Craft", "lavender" },
        };

        private static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "Heat", "trait-fire" }, { "Cold", "trait-ice" }, { "Water", "trait-water" }, { "Wind", "trait-wind" },
            { "Harm", "trait-bonk" }, { "Gear", "shield" }, { "Food", "trait-snack" }, { "Craft", "trait-brush" },
        };

        private Transform _body;
        private Transform _dial;
        private readonly List<Transform> _senders = new List<Transform>();

        public void Things(StagePlan plan)
        {
        }

        public void Build(GameMaster kit)
        {
            var holder = kit.Child("Key");
            var mount = FeaturePrefab.Child(holder, "Mount");
            FeaturePrefab.AttachToBone(mount, HumanBodyBones.LeftHand);
            var grip = FeaturePrefab.Child(mount, "Grip");
            var fit = FeaturePrefab.Ensure<WirenookHandGrip>(grip);
            fit.hand = HumanBodyBones.LeftHand;
            fit.held = new List<WirenookHeldWhen> { WirenookHeldWhen.On(Out(kit.Prefix)) };
            fit.curl = 0.85f;
            fit.indexCurl = 0.85f;
            fit.thumbCurl = 0.8f;
            _body = GameMasterBuild.Bone(grip, "Body", Vector3.zero);

            // In the grip's own frame the fingers point along +Z and the
            // back of the hand is +Y, so in a left fist +X runs out past
            // the thumb: the shaft lies along it, and the dial at its end
            // faces the back of the hand, where the wearer can read it.
            const float length = 0.26f;
            var dialAt = new Vector3(length, 0.012f, 0f);
            _dial = GameMasterBuild.Bone(_body.gameObject, "Dial", dialAt);
            var solid = kit.Solid.On(_body);
            solid.Colour("grey-dark").Box(new Vector3(length * 0.5f - 0.03f, 0f, 0f), new Vector3(length + 0.06f, 0.018f, 0.018f));
            solid.Colour("brass").Box(new Vector3(length - 0.11f, -0.022f, 0f), new Vector3(0.016f, 0.028f, 0.012f))
                .Box(new Vector3(length - 0.14f, -0.018f, 0f), new Vector3(0.016f, 0.02f, 0.012f));
            solid.At(dialAt).Colour("grey-mid").Ring(0.046f, 0.064f, segments: 20).Colour("grey-dark").Ring(0f, 0.046f, segments: 12, y: -0.004f);

            // A pip for each trait round the dial, in its family's colour,
            // and a stone in the middle for the family being sent.
            var traits = KeyModel.Traits;
            for (var index = 0; index < traits.Count; index++)
            {
                var around = (index + 0.5f) / traits.Count * 2f * Mathf.PI;
                var at = new Vector3(Mathf.Cos(around) * 0.055f, 0.006f, Mathf.Sin(around) * 0.055f);
                solid.At(dialAt).Colour(Swatches[traits[index].Family]).Vanishing(Pip(traits[index].Tag), at)
                    .Box(at, new Vector3(0.011f, 0.012f, 0.011f));
            }

            foreach (var family in KeyModel.Families)
                solid.At(dialAt).Colour(Swatches[family]).Vanishing(Stone(family), Vector3.zero).Ball(new Vector3(0f, 0.006f, 0f), 0.024f, 3, 6);
            solid.Vanishing(null).At(Vector3.zero);

            // Android and iOS allow eight contacts at Medium, so the contacts
            // are left out there (and the menu with them, below). The key's
            // shapes are part of the kit's one mesh on every platform, and
            // its bones stay for them: nothing ever takes it out there.
            var contacts = GameMasterBuild.Bone(_dial.gameObject, "Contacts", Vector3.zero);
            FeaturePrefab.Ensure<WirenookPcOnly>(contacts.gameObject);
            _senders.Clear();
            for (var index = 0; index < traits.Count; index++)
            {
                var send = GameMasterBuild.Bone(contacts.gameObject, "Send " + traits[index].Tag, Vector3.zero);
                var sender = FeaturePrefab.Ensure<VRCContactSender>(send.gameObject);
                sender.shapeType = ContactBase.ShapeType.Sphere;
                sender.radius = Reach;
                sender.localOnly = false;
                sender.collisionTags = new List<string> { traits[index].Tag };
                sender.enabled = true;
                // Switched on by the choice of trait, by its object: a
                // contact on an object that is not active does not exist.
                send.gameObject.SetActive(false);
                _senders.Add(send);
            }

            var feel = GameMasterBuild.Bone(contacts.gameObject, "Feel", Vector3.zero);
            var receiver = FeaturePrefab.Ensure<VRCContactReceiver>(feel.gameObject);
            receiver.shapeType = ContactBase.ShapeType.Sphere;
            receiver.radius = Reach;
            receiver.localOnly = false;
            receiver.allowSelf = true;
            receiver.allowOthers = true;
            receiver.collisionTags = traits.Select(trait => trait.Tag).ToList();
            receiver.receiverType = ContactReceiver.ReceiverType.Constant;
            receiver.parameter = Feel(kit.Prefix);
            // A bool for each tag, beside the parameter: {parameter}_CollisionTag_{tag}.
            var serialized = new SerializedObject(receiver);
            var advanced = serialized.FindProperty("advancedParameters");
            advanced.enumValueIndex = System.Array.IndexOf(advanced.enumNames, "CollisionTags");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public void Layers(GameMaster kit)
        {
            var fx = kit.Fx;
            var prefix = kit.Prefix;
            var traits = KeyModel.Traits;
            var mesh = GameMaster.SolidName;
            fx.Bool(Out(prefix)).Int(Trait(prefix)).Float(GameMasterBuild.One, 1f);

            var held = Audience.WhenTrue(Out(prefix)).And(Presence.DrawnHere);
            kit.Stage.Show(fx, "Key", new[] { kit.Path(_body) }, held);

            // The one sender that is on, and the stone that says which family it is.
            var layer = fx.Layer("Key Send");
            AnimationClip Sending(int chosen) => fx.Clip("key sends " + (chosen == 0 ? "nothing" : traits[chosen - 1].Tag), clip =>
            {
                for (var index = 0; index < traits.Count; index++)
                    clip.Active(kit.Path(_senders[index]), index + 1 == chosen);
                foreach (var family in KeyModel.Families)
                    clip.Blendshape(mesh, Stone(family), chosen != 0 && traits[chosen - 1].Family == family ? 0f : 100f);
            });
            var nothing = layer.State("Nothing", Sending(0));
            layer.Any(nothing).IfFalse(Out(prefix)).Or().Equal(Trait(prefix), 0);
            for (var chosen = 1; chosen <= traits.Count; chosen++)
                layer.Any(layer.State(traits[chosen - 1].Tag, Sending(chosen))).IfTrue(Out(prefix)).Equal(Trait(prefix), chosen);

            // The pips: each is off the dial unless its trait is felt. One
            // state for all nineteen: a direct blend tree adds, for each
            // pip, "gone" at full weight and "back" by as much as VRChat
            // says the trait is felt.
            var children = new List<(string, Motion)>();
            foreach (var trait in traits)
            {
                var feels = Feels(prefix, trait.Tag);
                fx.Float(feels);
                children.Add((GameMasterBuild.One, fx.Clip("key pip " + trait.Tag + " gone", clip => clip.Blendshape(mesh, Pip(trait.Tag), 100f))));
                children.Add((feels, fx.Clip("key pip " + trait.Tag + " back", clip => clip.Blendshape(mesh, Pip(trait.Tag), -100f))));
            }

            fx.Layer("Key Feel").State("Feel", GameMasterBuild.Weighted(fx, "key feel", children.ToArray()));
        }

        public void Menu(GameMaster kit)
        {
            var prefix = kit.Prefix;
            kit.Parameters.Add(FeaturePrefab.SyncedBool(Out(prefix)));
            kit.Parameters.Add(FeaturePrefab.SyncedInt(Trait(prefix)));
            kit.Parameters.Add(GameMasterBuild.Own(Feel(prefix)));
            foreach (var trait in KeyModel.Traits) kit.Parameters.Add(GameMasterBuild.Own(Feels(prefix, trait.Tag)));

            var folder = kit.Folder("Key", "key");
            FeaturePrefab.Ensure<WirenookPcOnly>(folder);
            FeaturePrefab.MenuToggle(FeaturePrefab.Child(folder, "Menu Out"), "Skeleton key", "key", Out(prefix));
            var send = FeaturePrefab.Child(folder, "Menu Send");
            FeaturePrefab.MenuFolder(send, "Send", "contacts");
            foreach (var family in KeyModel.Families)
            {
                var own = FeaturePrefab.Child(send, "Menu " + family);
                var members = KeyModel.Traits.Where(trait => trait.Family == family).ToList();
                if (members.Count == 1)
                {
                    FeaturePrefab.MenuToggle(own, members[0].Tag, Icons[family], Trait(prefix), KeyModel.Number(members[0].Tag));
                    continue;
                }

                FeaturePrefab.MenuFolder(own, family, Icons[family]);
                foreach (var trait in members)
                    FeaturePrefab.MenuToggle(FeaturePrefab.Child(own, "Menu " + trait.Tag), trait.Tag, Icons[family], Trait(prefix), KeyModel.Number(trait.Tag));
            }
        }
    }
}
