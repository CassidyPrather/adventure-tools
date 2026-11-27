using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.SelectiveAnimation.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>How a pointer is built.</summary>
    public sealed class PointerOptions
    {
        /// <summary>Prefix for the pointer's parameters, unique on an avatar.</summary>
        public string Prefix = "GM/Finger";

        /// <summary>The hand that points.</summary>
        public HumanBodyBones Hand = HumanBodyBones.RightHand;

        /// <summary>How far the laser reaches, in metres.</summary>
        public float Reach = 30f;

        /// <summary>Metres across the glow on the fingertip.</summary>
        public float Glow = 0.036f;

        /// <summary>The colour of the light, the beam and the dot: a swatch of the stage's material that follows the signature colour.</summary>
        public string Swatch = "glow-cyan";
    }

    /// <summary>
    /// A light on the tip of the index finger, for pointing things out
    /// while narrating, and a laser that grows out of it: a beam from the
    /// fingertip to a dot on whatever it meets. Both are in the wearer's
    /// signature colour and are drawn only where the wearer is.
    ///
    /// The light comes on with the pointing gesture, on with a switch in
    /// the menu, or both; a second switch turns the gesture off for people
    /// who would rather only use the menu. The laser is a switch of its own.
    ///
    ///   Hand/Grip       fitted to the hand like a held tool, so that its
    ///                   forward is the way the fingers point whatever the
    ///                   rig's own axes are
    ///   Hand/Grip/Ray   a VRCRaycast along the hand, against the world and
    ///                   other players, held on the fingertip by a
    ///                   constraint. Cast by every client for itself while
    ///                   the laser is on, so nothing of it is synced.
    ///   Finger/Tip      the last joint of the index finger: the glow, and
    ///                   on PC a trail behind it
    ///   Dot             where the ray ends; the raycast moves it
    ///
    /// The ray follows the hand and not the finger: a fist is how something
    /// is set down (see <see cref="Stage"/>), and a ray along a curled
    /// finger would point at the palm.
    ///
    /// The glow, the beam and the dot are shapes in a mesh the caller owns
    /// (<see cref="AddShapes"/>), so they cost no mesh or material slot of
    /// their own.
    /// </summary>
    public sealed class Pointer
    {
        public PointerOptions Options { get; }

        /// <summary>Synced: the light is on from the menu.</summary>
        public string Light => Options.Prefix;

        /// <summary>Synced and saved: the pointing gesture turns the light on.</summary>
        public string Gestures => Options.Prefix + "/Gestures";

        /// <summary>Synced: the laser is on.</summary>
        public string Laser => Options.Prefix + "/Laser";

        private string RayParameter => Options.Prefix + "/Ray";

        /// <summary>On each client: its own ray has met something.</summary>
        public string Hit => RayParameter + VRCRaycast.PARAM_HIT;

        /// <summary>On each client: how far along its reach the ray ends, 0 to 1.</summary>
        public string Ratio => RayParameter + VRCRaycast.PARAM_RATIO;

        /// <summary>Where the ray ends.</summary>
        public Transform Dot { get; private set; }

        /// <summary>The fingertip.</summary>
        public Transform Tip { get; private set; }

        /// <summary>The trail's object, for the caller to give a material; PC only.</summary>
        public GameObject Trail { get; private set; }

        private readonly GameObject _root;
        private Transform _ray;
        private Transform _beam;
        private Transform _dotMark;

        private bool Left => Options.Hand == HumanBodyBones.LeftHand;
        private string Gesture => Left ? "GestureLeft" : "GestureRight";

        /// <summary>VRChat's number for a pointing hand.</summary>
        private const int FingerPoint = 3;

        private Pointer(GameObject root, PointerOptions options)
        {
            _root = root;
            Options = options;
        }

        public static Pointer Configure(GameObject root, PointerOptions options = null)
        {
            var pointer = new Pointer(root, options ?? new PointerOptions());
            pointer.Build();
            return pointer;
        }

        private void Build()
        {
            var holder = FeaturePrefab.Child(_root, "Pointer");

            var finger = FeaturePrefab.Child(holder, "Finger");
            FeaturePrefab.AttachToBone(finger, Left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            Tip = GameMasterBuild.Bone(finger, "Tip", Vector3.zero);

            // The trail is a renderer of its own, and Android and iOS allow
            // an avatar none before they rank it Poor.
            Trail = FeaturePrefab.Child(Tip.gameObject, "Trail");
            FeaturePrefab.Ensure<WirenookPcOnly>(Trail);
            var trail = FeaturePrefab.Ensure<TrailRenderer>(Trail);
            trail.time = 0.45f;
            trail.minVertexDistance = 0.01f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = Options.Glow * 0.7f;
            trail.numCapVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = fade;

            var hand = FeaturePrefab.Child(holder, "Hand");
            FeaturePrefab.AttachToBone(hand, Options.Hand);
            var grip = FeaturePrefab.Child(hand, "Grip");
            var fit = FeaturePrefab.Ensure<WirenookHandGrip>(grip);
            fit.hand = Options.Hand;
            fit.alongFingers = 0.07f;
            fit.intoPalm = 0f;
            // Nothing is held: the hand keeps its own pose.
            fit.held = new List<WirenookHeldWhen>();

            var ray = FeaturePrefab.Child(grip, "Ray");
            _ray = ray.transform;
            // It leaves from the fingertip and runs the way the hand points.
            var ride = FeaturePrefab.Ensure<VRCPositionConstraint>(ray);
            ride.ClearSources();
            ride.AddSource(new VRCConstraintSource { SourceTransform = Tip, Weight = 1f });
            ride.PositionOffset = Vector3.zero;
            ride.IsActive = true;
            // Unlocked constraints re-measure their offset instead of moving the object.
            ride.Locked = true;
            ride.FreezeToWorld = false;
            ride.RebakeOffsetsWhenUnfrozen = false;

            var dot = FeaturePrefab.Child(_root, "Dot");
            Dot = dot.transform;
            _dotMark = GameMasterBuild.Bone(dot, "Mark", Vector3.zero);
            _beam = GameMasterBuild.Bone(ray, "Beam", Vector3.zero);

            var raycast = FeaturePrefab.Ensure<VRCRaycast>(ray);
            var serialized = new SerializedObject(raycast);
            // Version 1.1 is what allows hitting players.
            serialized.FindProperty("version").enumValueIndex = 1;
            serialized.FindProperty("raycastDirection").vector3Value = Vector3.forward;
            serialized.FindProperty("distance").floatValue = Options.Reach;
            serialized.FindProperty("collisionMode").enumValueIndex = 2; // worlds and players
            serialized.FindProperty("resultTransform").objectReferenceValue = Dot;
            serialized.FindProperty("applyRotation").boolValue = false;
            serialized.FindProperty("behaviorOnMiss").enumValueIndex = 2; // out at the ray's end
            serialized.FindProperty("parameter").stringValue = RayParameter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // Cast only while something needs it (AddLayers).
            raycast.enabled = false;
        }

        /// <summary>Adds the glow, the beam and the dot to a mesh.</summary>
        public void AddShapes(ShapeMesh mesh)
        {
            var radius = Options.Glow * 0.5f;
            mesh.On(Tip).Colour(Options.Swatch).Ball(Vector3.zero, radius, 3, 6);

            // A metre of beam along the ray; the animator stretches it to where the ray ends.
            var thick = radius * 0.3f;
            mesh.On(_beam).Colour(Options.Swatch).Box(new Vector3(0f, 0f, 0.5f), new Vector3(thick, thick, 1f));

            mesh.On(_dotMark).Colour(Options.Swatch).Ball(Vector3.zero, radius * 1.4f, 3, 6);
        }

        /// <summary>
        /// Adds the pointer's layers.
        /// </summary>
        /// <param name="alsoCast">
        /// Someone else who needs the ray, the light and the beam: the stage,
        /// while the wearer aims where to set something down. A parameter
        /// every client holds the same.
        /// </param>
        /// <param name="keepDrawn">Curves a clip must carry while it shows anything (see <see cref="Stage.KeepDrawn"/>).</param>
        public void AddLayers(ControllerBuilder fx, Audience alsoCast = null, System.Action<ClipBuilder> keepDrawn = null)
        {
            alsoCast = alsoCast ?? Audience.Nobody;
            fx.Bool(Light).Bool(Gestures, true).Bool(Laser).Bool(Hit).Float(Ratio).Int(Gesture);

            var pointing = Audience.WhenTrue(Gestures).And(Audience.WhenEqual(Gesture, FingerPoint));
            var beaming = Audience.WhenTrue(Laser).Or(alsoCast);
            var lit = Audience.WhenTrue(Light).Or(pointing).Or(beaming).And(Presence.DrawnHere);
            fx.Declare(lit).Declare(beaming);

            var tipPath = FeaturePrefab.PathTo(_root, Tip);
            var trailPath = FeaturePrefab.PathTo(_root, Trail.transform);
            PopToggle.Layer(fx, "Finger Light", tipPath, on => on.For(lit), off => off.For(lit.Not()), (clip, phase) =>
            {
                var on = phase == "on" || phase == "appear";
                clip.Constant(trailPath, typeof(TrailRenderer), "m_Emitting", on ? 1f : 0f);
                if (phase != "off") keepDrawn?.Invoke(clip);
                if (phase != "on") return;
                // A slow breath, so the light reads as alive.
                var breathe = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1.18f), new Keyframe(1.8f, 1f));
                for (var key = 0; key < breathe.length; key++) breathe.SmoothTangents(key, 0f);
                PopToggle.Scale(clip, tipPath, breathe);
                clip.Loop();
            });

            // The ray, cast on every client for itself.
            var rayPath = FeaturePrefab.PathTo(_root, _ray);
            AnimationClip Cast(bool casting) => fx.Clip(casting ? "pointer cast" : "pointer rest", clip =>
                clip.Constant(rayPath, typeof(VRCRaycast), "m_Enabled", casting ? 1f : 0f));
            beaming.Layer(fx, "Pointer Ray", Cast(true), Cast(false));

            // The beam: a metre long at rest, stretched along the ray to
            // where it ends. The clip sweeps the whole reach and the ray's
            // own ratio picks the place in it.
            var beamPath = FeaturePrefab.PathTo(_root, _beam);
            var shownBeam = beaming.And(Presence.DrawnHere);
            var stretch = fx.Clip("pointer beam", clip =>
            {
                clip.Constant(beamPath, typeof(Transform), "m_LocalScale.x", 1f, 1f)
                    .Constant(beamPath, typeof(Transform), "m_LocalScale.y", 1f, 1f)
                    .Curve(beamPath, typeof(Transform), "m_LocalScale.z", AnimationCurve.Linear(0f, 0f, 1f, Options.Reach));
                keepDrawn?.Invoke(clip);
            });
            var noBeam = fx.Clip("pointer no beam", clip => PopToggle.Scale(clip, beamPath, AnimationCurve.Constant(0f, 1f / 60f, 0f)));
            var beam = shownBeam.Layer(fx, "Pointer Beam", stretch, noBeam);
            beam.For.MotionTime(Ratio);

            var dotPath = FeaturePrefab.PathTo(_root, _dotMark);
            var onSomething = shownBeam.And(Audience.WhenTrue(Hit));
            PopToggle.Layer(fx, "Pointer Dot", dotPath, on => on.For(onSomething), off => off.For(onSomething.Not()), (clip, phase) =>
            {
                if (phase != "off") keepDrawn?.Invoke(clip);
            });
        }

        /// <summary>The pointer's expression parameters: three synced bits, and the ray's readings, which each client keeps for itself.</summary>
        public IEnumerable<ParameterConfig> Parameters()
        {
            yield return FeaturePrefab.SyncedBool(Light);
            yield return FeaturePrefab.SyncedBool(Gestures, defaultValue: true, saved: true);
            yield return FeaturePrefab.SyncedBool(Laser);
            foreach (var name in new[] { Hit, Ratio, RayParameter + VRCRaycast.PARAM_DISTANCE })
                yield return GameMasterBuild.Own(name);
        }

        /// <summary>Puts the three switches under <paramref name="folder"/>, which the caller makes a submenu of.</summary>
        public void AddMenu(GameObject folder)
        {
            FeaturePrefab.MenuToggle(FeaturePrefab.Child(folder, "Menu Light"), "Finger light", "finger-light", Light);
            FeaturePrefab.MenuToggle(FeaturePrefab.Child(folder, "Menu Laser"), "Laser", "laser-pointer", Laser);
            var gestures = FeaturePrefab.Child(folder, "Menu Gestures");
            FeaturePrefab.MenuToggle(gestures, "Light when I point", "gestures", Gestures);
            gestures.GetComponent<ModularAvatarMenuItem>().isSaved = true;
        }
    }
}
