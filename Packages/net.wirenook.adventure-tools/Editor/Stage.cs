using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.SelectiveAnimation.Editor;
using Wirenook.WorldSync.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>How a stage is built.</summary>
    public sealed class StageOptions
    {
        /// <summary>Prefix for the stage's parameters, unique on an avatar.</summary>
        public string Prefix = "GM/Stage";

        /// <summary>Metres from the world origin a thing can be set down at, on each axis.</summary>
        public float Range = 512f;

        /// <summary>Where a thing goes when the hand points at nothing, from the avatar's feet.</summary>
        public Vector3 InFront = new Vector3(0f, 0f, 1f);
    }

    /// <summary>One thing a stage can set down.</summary>
    public sealed class StageThing
    {
        public string Name { get; internal set; }

        /// <summary>Its slot on the bus, from 1.</summary>
        public int Slot { get; internal set; }

        /// <summary>Set down at the wearer's feet at one press, with no aiming.</summary>
        public bool AtFeet { get; internal set; }

        /// <summary>An object every client moves to where the thing stands.</summary>
        public Transform Pose { get; internal set; }

        /// <summary>Synced bool: the thing is on show.</summary>
        public string On { get; internal set; }

        /// <summary>
        /// The thing's value, 0 to 1: a float that every client has under
        /// this name, the wearer's from their menu and everyone else's
        /// from the bus (<see cref="AnchorBus.Value"/>). A size on a dial
        /// is the usual use. It costs no synced bits of its own.
        /// </summary>
        public string Value { get; internal set; }

        /// <summary>Part of a course that is laid out before it is shown.</summary>
        public bool Rehearsed { get; internal set; }

        /// <summary>On each client: it knows where the thing stands.</summary>
        public string Placed { get; internal set; }

        /// <summary>Whoever the thing stands for: it is on show, and their client knows where.</summary>
        public Audience Standing { get; internal set; }

        /// <summary>
        /// Whoever the thing is real for: it stands, and (for one that is
        /// rehearsed) the course is live. What a thing does to people, its
        /// contacts above all, belongs to this audience and no wider one.
        /// </summary>
        public Audience Real { get; internal set; }

        /// <summary>
        /// The people backstage, the wearer among them, while a rehearsed
        /// thing stands and the course is not live yet: they are to see it
        /// as a spectre. Nobody, for a thing that is not rehearsed.
        /// </summary>
        public Audience Rehearsal { get; internal set; }

        /// <summary>The wearer alone, while they are putting the thing down: it rides their aim until it stands.</summary>
        public Audience Mine { get; internal set; }

        /// <summary>The wearer alone, while the thing rides their aim.</summary>
        public Audience Carried { get; internal set; }

        /// <summary>Everyone who is to see the thing, before a tool narrows it further.</summary>
        public Audience Shown => Real.Or(Rehearsal).Or(Mine);
    }

    /// <summary>
    /// Setting things down in the world: the part every tool that leaves
    /// something standing shares. Any number of things stand on one
    /// <see cref="AnchorBus"/>, so all of them together sync what one
    /// position costs, and a late arrival has the lot within one round of
    /// the bus.
    ///
    /// A thing is put down in four steps, all on the wearer's client:
    ///
    ///   aim      the menu names the thing; the wearer alone sees it,
    ///            riding the spot their <see cref="Pointer"/> points at (or
    ///            a step in front, when it points at nothing)
    ///   click    a fist of the pointing hand, or "Set down" in the menu.
    ///            The thing goes off show for everyone and the bus measures
    ///            the spot
    ///   air      the bus reads the new spot out ahead of its round
    ///   show     only then is the thing switched on for everyone
    ///
    /// So nobody is ever shown a thing where it used to stand: about two
    /// seconds pass between the click and everyone seeing it. A thing marked
    /// "at feet" skips the aiming and is set down where the wearer stands.
    ///
    /// Each thing has one synced bit of its own, "on show", which the menu
    /// can also switch: off takes the thing away, and on brings it back
    /// where it last stood.
    ///
    /// Each thing also has a value, a float every client reads under one
    /// name (<see cref="StageThing.Value"/>), which rides the bus with the
    /// thing's place: the sizes of all the zones together sync eight bits.
    ///
    /// A thing can be marked as rehearsed. Such things make up a course:
    /// set down, they stand for the people backstage alone (who are to
    /// draw them as spectres, <see cref="StageThing.Rehearsal"/>) until the
    /// wearer raises one synced bit, <see cref="Live"/>, and the whole
    /// course is there for everyone at once.
    ///
    /// Nothing here draws anything but the mark the wearer aims with. A
    /// tool holds its own bones on a thing (<see cref="Hold"/>) and shows
    /// them to <see cref="StageThing.Shown"/>.
    /// </summary>
    public sealed class Stage
    {
        public StageOptions Options { get; }
        public Pointer Pointer { get; }
        public AnchorBus Bus { get; private set; }
        public IReadOnlyList<StageThing> Things => _things;

        /// <summary>The menu's: the number of the thing being aimed, 0 for none. The wearer's client only.</summary>
        public string Aim => Options.Prefix + "/Aim";

        /// <summary>The menu's: the number of a thing to set down at once. The wearer's client only.</summary>
        public string Now => Options.Prefix + "/Now";

        /// <summary>The menu's: set the thing being aimed down, for hands that never make a fist.</summary>
        public string Set => Options.Prefix + "/Set";

        /// <summary>The menu's: take everything away.</summary>
        public string Clear => Options.Prefix + "/Clear";

        /// <summary>Synced bool: the wearer is aiming, and their pointing hand is a button.</summary>
        public string Aiming => Options.Prefix + "/Aiming";

        /// <summary>
        /// Synced bool, on a stage with rehearsed things: the course is
        /// live, and what was laid out is there for everyone.
        /// </summary>
        public string Live => Options.Prefix + "/Live";

        /// <summary>Whether any thing on the stage is rehearsed, and so whether <see cref="Live"/> exists.</summary>
        public bool Rehearses => _things.Any(thing => thing.Rehearsed);

        private string Place => Options.Prefix + "/Place";
        private string Arming => Options.Prefix + "/Arming";
        private string Stored => Options.Prefix + "/Stored";
        private string Air => Options.Prefix + "/Air";

        /// <summary>Seconds the slot's number stays up before the wearer's client puts the thing on show.</summary>
        public const float ShowAfterAirSeconds = 0.4f;

        /// <summary>
        /// The size the bounds are held at while nothing is shown, as a
        /// share of their whole. Small enough to be a speck at the world's
        /// origin, and not nought: a mesh is skinned in the space of the
        /// object its bounds are measured from, and nothing can be measured
        /// from an object of no size at all.
        /// </summary>
        public const float AtRest = 0.0001f;

        private readonly GameObject _root;
        private readonly List<StageThing> _things = new List<StageThing>();
        private readonly List<(Transform bone, StageThing thing)> _held = new List<(Transform, StageThing)>();
        private Transform _carry;
        private Transform _extent;
        private Transform _mark;

        private string Gesture => Pointer.Options.Hand == HumanBodyBones.LeftHand ? "GestureLeft" : "GestureRight";

        private Stage(GameObject root, Pointer pointer, StageOptions options)
        {
            _root = root;
            Pointer = pointer;
            Options = options;
        }

        /// <summary>Call once before editing a prefab that carries a stage.</summary>
        public static void Prepare() => SyncedAnchor.Prepare();

        /// <param name="things">What can be set down, in the order of their slots.</param>
        public static Stage Configure(GameObject root, Pointer pointer, StageOptions options, params PlannedThing[] things)
        {
            if (things.Length == 0) throw new ArgumentException("A stage with nothing to set down.", nameof(things));
            var stage = new Stage(root, pointer, options ?? new StageOptions());
            stage.Build(things);
            return stage;
        }

        public StageThing this[string name] =>
            _things.FirstOrDefault(thing => thing.Name == name) ?? throw new KeyNotFoundException("The stage has nothing called \"" + name + "\".");

        private void Build(PlannedThing[] things)
        {
            var holder = FeaturePrefab.Child(_root, "Stage");
            var inFront = FeaturePrefab.Child(holder, "In Front");
            inFront.transform.localPosition = Options.InFront;
            var feet = FeaturePrefab.Child(holder, "Feet");
            feet.transform.localPosition = Vector3.zero;

            // On the pointer's dot while it has met something, a step in front while it has not.
            var carry = FeaturePrefab.Child(holder, "Carry");
            _carry = carry.transform;
            var follow = Constrain<VRCParentConstraint>(carry);
            follow.AddSource(new VRCConstraintSource { SourceTransform = Pointer.Dot, Weight = 0f });
            follow.AddSource(new VRCConstraintSource { SourceTransform = inFront.transform, Weight = 1f });

            Bus = AnchorBus.Configure(_root, Options.Prefix + "/Bus", things.Length, new[] { _carry, feet.transform }, Options.Range,
                things.Select(thing => thing.value).ToArray());
            var world = Bus.Pose(1).parent.parent;

            // What the stage draws is drawn wherever its bones are, which
            // can be anywhere in the world, so the bounds of its meshes are
            // the bus's whole range, measured from the world's origin. The
            // object they are measured from rests at next to no size, which
            // makes them a speck until there is something to show (KeepDrawn).
            _extent = FeaturePrefab.Child(world.gameObject, "Extent").transform;
            _extent.localScale = Vector3.one * AtRest;

            _mark = GameMasterBuild.Bone(holder, "Mark", Vector3.zero);
            var ride = Constrain<VRCParentConstraint>(_mark.gameObject);
            ride.AddSource(new VRCConstraintSource { SourceTransform = Bus.Cursor, Weight = 1f });

            for (var index = 0; index < things.Length; index++)
            {
                var slot = index + 1;
                var on = Options.Prefix + "/" + things[index].name.Replace(" ", "");
                var placed = Bus.Placed(slot);
                var carried = Audience.Wearer.And(Audience.WhenEqual(Aim, slot));
                var standing = Audience.WhenTrue(on).And(Audience.WhenTrue(placed));
                var rehearsed = things[index].rehearsed;
                _things.Add(new StageThing
                {
                    Name = things[index].name,
                    Slot = slot,
                    AtFeet = things[index].atFeet,
                    Pose = Bus.Pose(slot),
                    On = on,
                    Value = Bus.Value(slot),
                    Rehearsed = rehearsed,
                    Placed = placed,
                    Standing = standing,
                    Real = rehearsed ? standing.And(Audience.WhenTrue(Live)) : standing,
                    Rehearsal = rehearsed ? standing.And(Audience.WhenFalse(Live)).And(GameMaster.Backstage) : Audience.Nobody,
                    Carried = carried,
                    Mine = carried.Or(Audience.Wearer.And(Audience.WhenEqual(Arming, slot))),
                });
            }

            // While the wearer aims, a fist of the pointing hand is a button and not an expression.
            var claim = FeaturePrefab.Ensure<WirenookHandClaim>(holder);
            claim.hand = Pointer.Options.Hand == HumanBodyBones.LeftHand ? WirenookHand.Left : WirenookHand.Right;
            claim.parameter = Aiming;
        }

        private static T Constrain<T>(GameObject target) where T : VRCConstraintBase
        {
            var constraint = FeaturePrefab.Ensure<T>(target);
            constraint.ClearSources();
            constraint.IsActive = true;
            // Unlocked constraints re-measure their offset instead of moving the object.
            constraint.Locked = true;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            return constraint;
        }

        /// <summary>
        /// Holds a bone on a thing: where the thing stands, upright in the
        /// world's axes, and on the wearer's aim while they are putting it
        /// down. The bone is made under the feature's root if it is not
        /// there.
        /// </summary>
        public Transform Hold(string boneName, StageThing thing)
        {
            var bone = GameMasterBuild.Bone(FeaturePrefab.Child(_root, "Stage"), boneName, Vector3.zero);
            var constraint = Constrain<VRCParentConstraint>(bone.gameObject);
            constraint.AddSource(new VRCConstraintSource { SourceTransform = thing.Pose, Weight = 1f });
            constraint.AddSource(new VRCConstraintSource { SourceTransform = Bus.Cursor, Weight = 0f });
            _held.Add((bone, thing));
            return bone;
        }

        /// <summary>
        /// The bone a mesh that is drawn through the stage gives its
        /// renderer as its root, with <see cref="Bounds"/> as its bounds.
        /// </summary>
        public Transform Extent => _extent;

        /// <summary>Bounds that reach everywhere the stage can set a thing down, about <see cref="Extent"/>.</summary>
        public Bounds Bounds => new Bounds(Vector3.zero, Vector3.one * (2f * Bus.Grid.Range));

        /// <summary>Gives a renderer the stage's root and bounds, so that it is drawn wherever its bones have gone.</summary>
        public void Reach(SkinnedMeshRenderer renderer)
        {
            renderer.rootBone = _extent;
            renderer.localBounds = Bounds;
            renderer.updateWhenOffscreen = false;
        }

        /// <summary>
        /// Every clip that shows something of the stage's calls this: it
        /// keeps the bounds whole for as long as the clip plays. While no
        /// such clip plays, the bounds are a point and nothing is drawn or
        /// skinned.
        /// </summary>
        public void KeepDrawn(ClipBuilder clip)
        {
            PopToggle.Scale(clip, FeaturePrefab.PathTo(_root, _extent), AnimationCurve.Constant(0f, 1f / 60f, 1f));
        }

        /// <summary>Adds the mark the wearer aims with to a mesh.</summary>
        public void AddShapes(ShapeMesh mesh, string swatch = "glow-cyan", string pale = "white")
        {
            mesh.On(_mark).Colour(swatch)
                .Ring(0.16f, 0.2f, segments: 16, y: 0.01f)
                .Colour(pale)
                .Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.012f, 0.5f, 0.012f))
                .Colour(swatch)
                .Ball(new Vector3(0f, 0.5f, 0f), 0.03f, 3, 6);
        }

        /// <summary>
        /// Adds a layer that pops some bones in for an audience, with the
        /// stage's bounds kept whole while it does.
        /// </summary>
        /// <param name="extras">Further curves, as <see cref="PopToggle.Layer(ControllerBuilder,string,string,Action{TransitionBuilder},Action{TransitionBuilder},Action{ClipBuilder,string})"/> takes them.</param>
        public PopToggle.States Show(ControllerBuilder fx, string name, IReadOnlyList<string> paths, Audience audience,
            Action<ClipBuilder, string> extras = null)
        {
            fx.Declare(audience);
            return PopToggle.Layer(fx, name, paths[0], on => on.For(audience), off => off.For(audience.Not()), (clip, phase) =>
            {
                // The first bone's curve is the layer's own; the rest follow it.
                var seconds = phase == "appear" ? PopToggle.AppearSeconds : phase == "disappear" ? PopToggle.DisappearSeconds : 1f / 60f;
                var scale = phase == "appear" ? PopToggle.Overshoot(0f, 1f, seconds)
                    : phase == "disappear" ? PopToggle.Overshoot(1f, 0f, seconds)
                    : AnimationCurve.Constant(0f, seconds, phase == "on" ? 1f : 0f);
                foreach (var path in paths.Skip(1)) PopToggle.Scale(clip, path, scale);
                if (phase != "off") KeepDrawn(clip);
                extras?.Invoke(clip, phase);
            });
        }

        /// <summary>
        /// Adds the bus's layers and the stage's own. Call it after every
        /// <see cref="Hold"/>, and before the tools add theirs: the first
        /// layer here is the one that rests the bounds at a point.
        /// </summary>
        public void AddLayers(ControllerBuilder fx)
        {
            fx.Int(Aim).Int(Now).Bool(Set).Bool(Clear).Bool(Aiming).Int(Place).Int(Arming).Bool(Stored).Bool(Air)
                .Bool("IsLocal").Int(Gesture).Bool(Pointer.Hit).Float(Bus.OnAir).Int(Bus.Pending).Int(Bus.Fresh);
            foreach (var thing in _things) fx.Bool(thing.On).Bool(thing.Placed);
            if (Rehearses) fx.Bool(Live).Declare(GameMaster.Backstage);

            var extentPath = FeaturePrefab.PathTo(_root, _extent);
            fx.Layer("Stage Extent").State("None", fx.Clip("stage extent none", clip =>
                PopToggle.Scale(clip, extentPath, AnimationCurve.Constant(0f, 1f / 60f, AtRest))));

            Bus.AddLayers(fx, Place, Clear, Air, _things.Select(thing => thing.AtFeet ? 1 : 0).ToArray());

            // Says to everyone that the wearer is aiming: their copy of the hand is busy too.
            var aimingNow = Audience.Wearer.And(Audience.WhenNotEqual(Aim, 0));
            var says = fx.Layer("Stage Aiming");
            var resting = says.State("Resting");
            var aiming = says.State("Aiming");
            resting.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver, Change(Aiming, 0f)));
            aiming.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver, Change(Aiming, 1f)));
            resting.To(aiming).For(aimingNow);
            aiming.To(resting).For(aimingNow.Not());

            // Where a thing is carried: on the spot the pointer has met, or a step in front.
            var carryPath = FeaturePrefab.PathTo(_root, _carry);
            AnimationClip Carried(bool aimed) => fx.Clip(aimed ? "stage carry aimed" : "stage carry in front", clip => clip
                .Constant(carryPath, typeof(VRCParentConstraint), "Sources.source0.Weight", aimed ? 1f : 0f)
                .Constant(carryPath, typeof(VRCParentConstraint), "Sources.source1.Weight", aimed ? 0f : 1f));
            Audience.WhenTrue(Aiming).And(Audience.WhenTrue(Pointer.Hit)).Layer(fx, "Stage Spot", Carried(true), Carried(false));

            ClickLayer(fx);
            PublishLayer(fx);

            // The bus reads the layout out for as long as anything is on show or on its way there.
            var anything = _things.Aggregate(Audience.WhenNotEqual(Arming, 0), (any, thing) => any.Or(Audience.WhenTrue(thing.On)))
                .And(Audience.Wearer);
            var air = fx.Layer("Stage Air");
            var quiet = air.State("Quiet");
            var airing = air.State("Airing");
            quiet.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver, Change(Air, 0f)));
            airing.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver, Change(Air, 1f)));
            quiet.To(airing).For(anything);
            airing.To(quiet).For(anything.Not());

            // Clearing takes everything off show at once; the bus forgets where it stood.
            var clear = fx.Layer("Stage Clear");
            var keeping = clear.State("Keeping");
            var clearing = clear.State("Clearing");
            clearing.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver,
                _things.Select(thing => Change(thing.On, 0f))
                    .Concat(new[] { Change(Arming, 0f), Change(Stored, 0f), Change(Aim, 0f) })
                    .Concat(Rehearses ? new[] { Change(Live, 0f) } : new VRC_AvatarParameterDriver.Parameter[0]).ToArray()));
            keeping.To(clearing).IfTrue("IsLocal").IfTrue(Clear);
            clearing.To(keeping).IfFalse(Clear);

            // The mark: where the thing will stand, for the wearer alone.
            Show(fx, "Stage Mark", new[] { FeaturePrefab.PathTo(_root, _mark) }, aimingNow);

            // Each bone the stage holds: on its slot, or on the wearer's aim while they carry it.
            foreach (var (bone, thing) in _held)
            {
                var path = FeaturePrefab.PathTo(_root, bone);
                AnimationClip Where(bool carried) => fx.Clip("stage " + bone.name + (carried ? " carried" : " standing"), clip => clip
                    .Constant(path, typeof(VRCParentConstraint), "Sources.source0.Weight", carried ? 0f : 1f)
                    .Constant(path, typeof(VRCParentConstraint), "Sources.source1.Weight", carried ? 1f : 0f));
                thing.Carried.Layer(fx, "Stage " + bone.name + " Where", Where(true), Where(false));
            }
        }

        /// <summary>
        /// The click, on the wearer's client: a fist of the pointing hand
        /// while aiming (one that is already a fist when the aiming starts
        /// has to open first), "Set down" in the menu, or "at once" from
        /// the menu with no aiming at all. One thing at a time: nothing is
        /// taken up while the last one is still on its way to being shown.
        /// </summary>
        private void ClickLayer(ControllerBuilder fx)
        {
            var layer = fx.Layer("Stage Click");
            var idle = layer.State("Idle");
            var wait = layer.State("Wait");
            var ready = layer.State("Ready");
            var stored = layer.State("Stored");
            stored.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver, Change(Place, 0f), Change(Aim, 0f), Change(Stored, 1f)));

            idle.To(wait).IfTrue("IsLocal").NotEqual(Aim, 0).Equal(Arming, 0);
            wait.To(ready).NotEqual(Gesture, 1);
            foreach (var state in new[] { wait, ready }) state.To(idle).Equal(Aim, 0);
            // The buttons are let go of before the next thing is taken up.
            stored.To(idle).Equal(Now, 0).IfFalse(Set);

            // If the bus never takes the number up, let go after a while rather than hold the hand for good.
            var giveUp = fx.Clip("stage click wait", clip => clip.Hold(3f));
            foreach (var thing in _things)
            {
                var slot = thing.Slot;
                var click = layer.State("Click " + thing.Name, giveUp);
                click.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver,
                    Change(thing.On, 0f), Change(Place, slot), Change(Arming, slot), Change(Stored, 0f)));
                var measuring = layer.State("Measuring " + thing.Name, giveUp);

                ready.To(click).Equal(Gesture, 1).Equal(Aim, slot);
                foreach (var state in new[] { wait, ready }) state.To(click).IfTrue(Set).Equal(Aim, slot);
                idle.To(click).IfTrue("IsLocal").Equal(Now, slot).Equal(Arming, 0);
                foreach (var state in new[] { wait, ready }) state.To(click).Equal(Now, slot);

                click.To(measuring).Equal(Bus.Pending, slot);
                click.To(stored).AfterTime(1f);
                measuring.To(stored).Equal(Bus.Pending, 0);
                measuring.To(stored).AfterTime(1f);
            }
        }

        /// <summary>
        /// Putting a thing on show once everyone can know where it stands:
        /// the wearer's client waits until the bus has turned to the new
        /// spot (<see cref="AnchorBus.Fresh"/> back at 0) and the slot's
        /// number is up, then a little longer for the others to have copied
        /// it.
        /// </summary>
        private void PublishLayer(ControllerBuilder fx)
        {
            var layer = fx.Layer("Stage Publish");
            var idle = layer.State("Idle");
            var hold = fx.Clip("stage publish wait", clip => clip.Hold(ShowAfterAirSeconds));
            foreach (var thing in _things)
            {
                var slot = thing.Slot;
                var aired = layer.State("Aired " + thing.Name, hold);
                var show = layer.State("Show " + thing.Name);
                show.Behaviour<VRCAvatarParameterDriver>(driver => Drive(driver, Change(thing.On, 1f), Change(Arming, 0f), Change(Stored, 0f)));
                idle.To(aired).IfTrue("IsLocal").Equal(Arming, slot).IfTrue(Stored).Equal(Bus.Fresh, 0)
                    .Greater(Bus.OnAir, slot - 0.5f).Less(Bus.OnAir, slot + 0.5f);
                // Cleared, or overtaken, meanwhile.
                aired.To(idle).NotEqual(Arming, slot);
                aired.To(show).AfterTime(1f);
                show.To(idle).Equal(Arming, 0);
            }
        }

        private static VRC_AvatarParameterDriver.Parameter Change(string name, float value) =>
            new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = name, value = value };

        /// <summary>A driver that runs on the wearer's client only.</summary>
        private static void Drive(VRCAvatarParameterDriver driver, params VRC_AvatarParameterDriver.Parameter[] changes)
        {
            driver.localOnly = true;
            driver.parameters = changes.ToList();
        }

        /// <summary>
        /// The stage's expression parameters, the bus's included: one synced
        /// bit for each thing and one for "aiming", on top of what the bus
        /// syncs (<see cref="AnchorBus.SyncedBits"/>).
        /// </summary>
        public IEnumerable<ParameterConfig> Parameters()
        {
            foreach (var parameter in Bus.Parameters()) yield return parameter;
            foreach (var thing in _things) yield return FeaturePrefab.SyncedBool(thing.On);
            yield return FeaturePrefab.SyncedBool(Aiming);
            if (Rehearses) yield return FeaturePrefab.SyncedBool(Live);
            yield return Mine(Aim, ParameterSyncType.Int);
            yield return Mine(Now, ParameterSyncType.Int);
            yield return Mine(Set, ParameterSyncType.Bool);
            yield return Mine(Clear, ParameterSyncType.Bool);
            foreach (var name in new[] { Place, Arming, Stored, Air }) yield return GameMasterBuild.Own(name);
        }

        /// <summary>Synced bits the stage costs with this many things on it.</summary>
        public int SyncedBits => Bus.Bits + _things.Count + 1 + (Rehearses ? 1 : 0);

        /// <summary>A parameter the menu sets on the wearer's client and nobody else hears of.</summary>
        private static ParameterConfig Mine(string name, ParameterSyncType type)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name, syncType = type, localOnly = true, saved = false,
                defaultValue = 0f, hasExplicitDefaultValue = true,
            };
        }

        /// <summary>A menu switch that starts and stops aiming a thing.</summary>
        public void MenuAim(GameObject holder, StageThing thing, string label, string icon = "drop")
        {
            FeaturePrefab.MenuToggle(holder, label, icon, Aim, thing.Slot);
            Local(holder);
        }

        /// <summary>A menu button that sets a thing down at once: at the wearer's feet for one marked so, otherwise wherever the pointer's dot is.</summary>
        public void MenuNow(GameObject holder, StageThing thing, string label, string icon = "drop")
        {
            FeaturePrefab.MenuButton(holder, label, icon, Now, thing.Slot);
            Local(holder);
        }

        /// <summary>A menu switch for whether a thing is on show: off takes it away, on brings it back where it last stood.</summary>
        public void MenuShown(GameObject holder, StageThing thing, string label, string icon = "seen")
        {
            FeaturePrefab.MenuToggle(holder, label, icon, thing.On);
        }

        /// <summary>The menu button that sets down whatever is being aimed.</summary>
        public void MenuSet(GameObject holder, string label = "Set down", string icon = "drop")
        {
            FeaturePrefab.MenuButton(holder, label, icon, Set);
            Local(holder);
        }

        /// <summary>A dial in the menu for a thing's value. It is the wearer's own; the bus tells everyone else.</summary>
        public void MenuValue(GameObject holder, StageThing thing, string label, string icon)
        {
            FeaturePrefab.MenuRadial(holder, label, icon, thing.Value);
            holder.GetComponent<ModularAvatarMenuItem>().isSynced = false;
        }

        /// <summary>The menu switch that makes a rehearsed course live, and takes it back behind the scenes.</summary>
        public void MenuLive(GameObject holder, string label = "Live", string icon = "seen")
        {
            if (!Rehearses) throw new InvalidOperationException("The stage has nothing that is rehearsed, so nothing to make live.");
            FeaturePrefab.MenuToggle(holder, label, icon, Live);
        }

        /// <summary>The menu button that takes everything away.</summary>
        public void MenuClear(GameObject holder, string label = "Clear all", string icon = "pick-up")
        {
            FeaturePrefab.MenuButton(holder, label, icon, Clear);
            Local(holder);
        }

        private static void Local(GameObject holder)
        {
            var item = holder.GetComponent<ModularAvatarMenuItem>();
            item.isSynced = false;
            item.isSaved = false;
        }
    }
}
