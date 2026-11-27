using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Zone markers: abstract spheres a game master sets down to say "in
    /// here", in their signature colour. Each is aimed with the pointer and
    /// set down on the stage, has a size from a dial in the menu, and is
    /// shown to everyone or to the people backstage alone.
    ///
    /// Every platform draws a marker as a ring of dashes on the ground and
    /// two dashed arches over it, drifting slowly round. That is plain
    /// geometry in the kit's one mesh, so it also survives a viewer who has
    /// the avatar's shaders blocked.
    ///
    /// PC adds what the request was for: a dashed line that lies on the
    /// meshes the sphere's edge crosses, and a faint tint on everything
    /// inside. Both are stencil volumes (Shaders/WirenookStageVolume): the
    /// tint a ball, the dashes a thin shell cut into slices of which every
    /// other one is kept. They need no depth texture and no light. Zones
    /// that overlap each other leave the overlap untinted; the shader's
    /// header says why.
    ///
    /// Synced, for each zone: on show (the stage's bit), backstage only (a
    /// bit) and the size (eight).
    /// </summary>
    public sealed class ZoneTool : IStageTool
    {
        /// <summary>How many zones the kit can have standing at once.</summary>
        public int Count = 3;

        /// <summary>Seconds a marker takes to drift once round.</summary>
        public const float TurnSeconds = 90f;

        /// <summary>How thick the shell the dashes are cut from is, as a share of the radius.</summary>
        public const float Shell = 0.03f;

        public static string Name(int zone) => "Zone " + zone;
        public static string BackstageOnly(string prefix, int zone) => prefix + "/Zone" + zone + "/Backstage";
        public static string Size(string prefix, int zone) => prefix + "/Zone" + zone + "/Size";

        private Transform[] _bones;
        private Transform[] _sizes;

        public void Things(StagePlan plan)
        {
            for (var zone = 1; zone <= Count; zone++) plan.Thing(Name(zone));
        }

        public void Build(GameMaster kit)
        {
            _bones = new Transform[Count];
            _sizes = new Transform[Count];
            for (var zone = 1; zone <= Count; zone++)
            {
                var bone = kit.Stage.Hold(Name(zone), kit.Stage[Name(zone)]);
                var size = GameMasterBuild.Bone(bone.gameObject, "Size", Vector3.zero);
                _bones[zone - 1] = bone;
                _sizes[zone - 1] = size;
                Marker(kit, size);
            }
        }

        /// <summary>
        /// Adds one marker, a metre in radius, to the kit's meshes, carried
        /// by <paramref name="size"/>: scale that bone to the radius wanted.
        /// For any tool that marks a sphere in the world.
        /// </summary>
        public static void Marker(GameMaster kit, Transform size, string swatch = "glow-cyan")
        {
            var solid = kit.Solid.On(size).Colour(swatch);
            const int dashes = 16;
            for (var dash = 0; dash < dashes; dash++)
                solid.Bend(1f - 0.014f, 1f, 0f, 0.014f, dash * 360f / dashes, 180f / dashes, 2);

            // Two arches over the top: the same dashes, stood on edge.
            foreach (var heading in new[] { 0f, 90f })
            {
                solid.At(Vector3.zero, Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(90f, 0f, 0f));
                const int arch = 9;
                for (var dash = 0; dash < arch; dash++)
                    solid.Bend(1f - 0.012f, 1f, -0.006f, 0.006f, 180f + (dash + 0.25f) * 180f / arch, 90f / arch, 2);
            }

            solid.At(Vector3.zero);

            var tint = kit.Effect(GameMasterBuild.Material(GameMasterBuild.TintMaterial));
            kit.Effects.On(size).Slot(tint).Slice(0f, 1f - Shell, 0f, 360f, 10, 20);
            var line = kit.Effect(GameMasterBuild.Material(GameMasterBuild.DashMaterial));
            const int slices = 24;
            kit.Effects.On(size).Slot(line);
            for (var slice = 0; slice < slices; slice += 2)
                kit.Effects.Slice(1f - Shell, 1f, slice * 360f / slices, 360f / slices, 10, 2);
        }

        public void Layers(GameMaster kit)
        {
            var fx = kit.Fx;
            for (var zone = 1; zone <= Count; zone++)
            {
                var thing = kit.Stage[Name(zone)];
                var backstage = BackstageOnly(kit.Prefix, zone);
                var sizePath = kit.Path(_sizes[zone - 1]);
                GameMasterBuild.Sized(fx, Name(zone) + " Size", sizePath, Size(kit.Prefix, zone));

                var audience = thing.Mine.Or(thing.Standing.And(Audience.WhenFalse(backstage).Or(GameMaster.Backstage)));
                kit.Stage.Show(fx, Name(zone), new[] { kit.Path(_bones[zone - 1]) }, audience, (clip, phase) =>
                {
                    if (phase != "on") return;
                    clip.Curve(sizePath, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, TurnSeconds, 360f)).Loop();
                });
            }
        }

        public void Menu(GameMaster kit)
        {
            var folder = kit.Folder("Zones", "zone");
            for (var zone = 1; zone <= Count; zone++)
            {
                var thing = kit.Stage[Name(zone)];
                kit.Parameters.Add(FeaturePrefab.SyncedBool(BackstageOnly(kit.Prefix, zone)));
                kit.Parameters.Add(FeaturePrefab.SyncedFloat(Size(kit.Prefix, zone), ZoneModel.RestingAmount));

                var own = FeaturePrefab.Child(folder, "Menu " + Name(zone));
                FeaturePrefab.MenuFolder(own, Name(zone), "zone");
                kit.Stage.MenuAim(FeaturePrefab.Child(own, "Menu Aim"), thing, "Aim");
                kit.Stage.MenuSet(FeaturePrefab.Child(own, "Menu Set"));
                kit.Stage.MenuShown(FeaturePrefab.Child(own, "Menu Shown"), thing, "Shown");
                FeaturePrefab.MenuToggle(FeaturePrefab.Child(own, "Menu Backstage"), "Backstage only", "presence", BackstageOnly(kit.Prefix, zone));
                FeaturePrefab.MenuRadial(FeaturePrefab.Child(own, "Menu Size"), "Size", "zone", Size(kit.Prefix, zone));
            }
        }
    }

    /// <summary>
    /// The pause: dropped where the game master stands, with a radius.
    ///
    /// Everyone, on every platform and with the avatar's shaders blocked,
    /// sees a ring on the ground and a pause sign turning over its middle:
    /// plain geometry. On PC the inside is dimmed as well, the game master
    /// and anything else that marks itself as a wirenook avatar excepted,
    /// and the people whose eyes are inside get a card on their screen that
    /// says so (Shaders/WirenookStageNotice). A sound plays from the spot
    /// when it begins and when it ends.
    ///
    /// Synced: on show (the stage's bit) and the size (eight bits).
    /// </summary>
    public sealed class PauseTool : IStageTool
    {
        public const string ThingName = "Pause";

        /// <summary>Metres above the ground the sign turns at.</summary>
        public const float SignHeight = 2.4f;

        /// <summary>Metres above the ground the point is that a viewer's eyes are measured from.</summary>
        public const float EyeLevel = 1.2f;

        /// <summary>How far the sounds carry, in metres.</summary>
        public const float Heard = 25f;

        public static string Size(string prefix) => prefix + "/Pause/Size";

        private Transform _bone;
        private Transform _size;
        private Transform _sign;

        public void Things(StagePlan plan)
        {
            plan.Thing(ThingName, atFeet: true);
        }

        public void Build(GameMaster kit)
        {
            _bone = kit.Stage.Hold(ThingName, kit.Stage[ThingName]);
            _size = GameMasterBuild.Bone(_bone.gameObject, "Size", Vector3.zero);
            _sign = GameMasterBuild.Bone(_bone.gameObject, "Sign", new Vector3(0f, SignHeight, 0f));

            kit.Solid.On(_size).Colour("glow-cyan").Bend(0.975f, 1f, 0f, 0.02f, 0f, 360f, 48)
                .Colour("white").Bend(0.93f, 0.942f, 0f, 0.012f, 0f, 360f, 48);
            kit.Solid.On(_sign).Colour("glow-cyan")
                .Box(new Vector3(-0.11f, 0f, 0f), new Vector3(0.12f, 0.42f, 0.07f))
                .Box(new Vector3(0.11f, 0f, 0f), new Vector3(0.12f, 0.42f, 0.07f));

            var dim = kit.Effect(GameMasterBuild.Material(GameMasterBuild.DimMaterial));
            kit.Effects.On(_size).Slot(dim).Slice(0f, 1f, 0f, 360f, 12, 24);
            var card = kit.Effect(GameMasterBuild.Material(GameMasterBuild.CardMaterial));
            kit.Effects.On(_bone).Slot(card).Corners(new Vector3(0f, EyeLevel, 0f));

            // Heard from the spot itself, and from further off than a cue: it is for the whole table.
            WirenookSounds.Source(_bone.gameObject, shared: false).GetComponent<VRC.SDK3.Avatars.Components.VRCSpatialAudioSource>().Far = Heard;
        }

        public void Layers(GameMaster kit)
        {
            var fx = kit.Fx;
            var thing = kit.Stage[ThingName];
            var effects = GameMaster.EffectsName;
            GameMasterBuild.Sized(fx, "Pause Size", kit.Path(_size), Size(kit.Prefix), (clip, radius) =>
                clip.Material<SkinnedMeshRenderer>(effects, "_Radius", radius));

            var signPath = kit.Path(_sign);
            var states = kit.Stage.Show(fx, "Pause", new[] { kit.Path(_bone) }, thing.Shown, (clip, phase) =>
            {
                var card = phase == "appear" ? AnimationCurve.EaseInOut(0f, 0f, PopToggle.AppearSeconds, 1f)
                    : phase == "disappear" ? AnimationCurve.EaseInOut(0f, 1f, PopToggle.DisappearSeconds, 0f)
                    : AnimationCurve.Constant(0f, 1f / 60f, phase == "on" ? 1f : 0f);
                clip.Material<SkinnedMeshRenderer>(effects, "_Show", card);
                if (phase != "on") return;
                clip.Curve(signPath, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 8f, 360f)).Loop();
            });
            var source = kit.Path(_bone) + "/" + WirenookSounds.SourceName;
            WirenookSounds.PlayOnEnter(states.Appear, source, "pause");
            WirenookSounds.PlayOnEnter(states.Disappear, source, "resume");
        }

        public void Menu(GameMaster kit)
        {
            var thing = kit.Stage[ThingName];
            kit.Parameters.Add(FeaturePrefab.SyncedFloat(Size(kit.Prefix), ZoneModel.RestingAmount));
            var folder = kit.Folder("Pause", "pause");
            kit.Stage.MenuNow(FeaturePrefab.Child(folder, "Menu Here"), thing, "Pause here", "pause");
            kit.Stage.MenuShown(FeaturePrefab.Child(folder, "Menu Paused"), thing, "Paused", "pause");
            FeaturePrefab.MenuRadial(FeaturePrefab.Child(folder, "Menu Size"), "Size", "zone", Size(kit.Prefix));
        }
    }

    /// <summary>
    /// The pointer's place in the kit's menu. The pointer itself is built
    /// with the kit, because the stage aims with it.
    /// </summary>
    public sealed class PointerTool : IStageTool
    {
        public void Things(StagePlan plan)
        {
        }

        public void Build(GameMaster kit)
        {
        }

        public void Layers(GameMaster kit)
        {
        }

        public void Menu(GameMaster kit)
        {
            kit.Pointer.AddMenu(kit.Folder("Pointer", "finger-light"));
        }
    }
}
