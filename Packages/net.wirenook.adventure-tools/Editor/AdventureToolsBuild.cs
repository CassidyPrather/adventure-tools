using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.WorldSync.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Generates the package's prefabs and animators from the exported models.
    /// Everything under Generated/ and Prefabs/ is rebuilt in place by
    /// <see cref="Rebuild"/>; the sources are the models, the notice list and
    /// this file.
    /// </summary>
    public static class AdventureToolsBuild
    {
        public const string Package = "Packages/net.wirenook.adventure-tools";
        private const string Models = Package + "/Models";
        private const string Generated = Package + "/Generated";
        private const string Prefabs = Package + "/Prefabs";

        public const string FlashlightPrefab = Prefabs + "/Wirenook Flashlight.prefab";
        public const string FlashlightRealPrefab = Prefabs + "/Wirenook Flashlight (Real Light).prefab";
        public const string LanternPrefab = Prefabs + "/Wirenook Lantern.prefab";
        public const string LanternRealPrefab = Prefabs + "/Wirenook Lantern (Real Light).prefab";
        public const string MegaphonePrefab = Prefabs + "/Wirenook Megaphone.prefab";
        public const string LaserPrefab = Prefabs + "/Wirenook Laser Pointer.prefab";
        public const string LaserPingPrefab = Prefabs + "/Wirenook Laser Pointer (Ping).prefab";
        public const string NoticePrefab = Prefabs + "/Wirenook Notice.prefab";
        public const string BeaconPrefab = Prefabs + "/Wirenook Beacon.prefab";
        public const string BundlePrefab = Prefabs + "/Wirenook Adventure Tools.prefab";
        public const string BundleFlashlightPrefab = Prefabs + "/Wirenook Adventure Tools (Real Flashlight).prefab";
        public const string BundleLanternPrefab = Prefabs + "/Wirenook Adventure Tools (Real Lantern).prefab";
        public const string BundleEventPrefab = Prefabs + "/Wirenook Adventure Tools (Event).prefab";

        /// <summary>
        /// Which light is out: 0 none, 1 the flashlight, 2 the lantern. Both
        /// are held in the left hand, so the two prefabs share this one
        /// parameter and taking one out puts the other away.
        /// </summary>
        private const string LightParameter = "WN/Light";

        /// <summary>One of the two hand lights, and what its real light is like.</summary>
        private sealed class HandLightSpec
        {
            public string Id;
            public string Label;
            public int Choice;
            public string Model;
            public string RootBone;

            /// <summary>The bone carrying the geometry that stands in for light, and the light itself.</summary>
            public string GlowBone;

            /// <summary>The bone that hangs and swings, if any.</summary>
            public string SwingBone;
            public LightType Type;
            public float Range;
            public float Intensity;
            public float SpotAngle;

            public string Prefab(bool real) => Prefabs + "/Wirenook " + Label + (real ? " (Real Light).prefab" : ".prefab");
            public string Controller(bool real) => Generated + "/" + Id + (real ? "-real-fx.controller" : "-fx.controller");

            /// <summary>Whether the real one has been left standing in the world.</summary>
            public string Placed => "WN/" + Label + "Placed";
        }

        private static readonly HandLightSpec FlashlightSpec = new HandLightSpec
        {
            Id = "flashlight", Label = "Flashlight", Choice = 1, Model = "flashlight.fbx",
            RootBone = "Flashlight", GlowBone = "Flashlight.Beam",
            Type = LightType.Spot, Range = 14f, Intensity = 1.6f, SpotAngle = 55f,
        };

        private static readonly HandLightSpec LanternSpec = new HandLightSpec
        {
            Id = "lantern", Label = "Lantern", Choice = 2, Model = "lantern.fbx",
            RootBone = "Lantern", GlowBone = "Lantern.Glow", SwingBone = "Lantern.Body",
            Type = LightType.Point, Range = 7f, Intensity = 1.3f,
        };

        private const string Mount = "Mount";
        private const string Grip = "Grip";

        /// <summary>How far the laser reaches, in metres.</summary>
        private const float LaserRange = 30f;

        /// <summary>How long a ping stays up, in turns of its one-second loop.</summary>
        private const int PingLoops = 4;

        private const string PingParameter = "WN/Ping";

        /// <summary>How long a notice stays up between fading in and out, in seconds.</summary>
        private const float NoticeSeconds = 3.85f;
        private const float NoticeFadeIn = 0.25f;
        private const float NoticeFadeOut = 0.4f;

        /// <summary>Checks the placed lights' synced anchors as far as the editor can.</summary>
        [MenuItem("Tools/Wirenook/Adventure Tools/Verify")]
        public static string Verify()
        {
            var report = string.Join(System.Environment.NewLine, new[] { FlashlightSpec, LanternSpec }.Select(light =>
                light.Label + ": " + SyncedAnchorCheck.Run(light.Prefab(true), light.Controller(true), light.Placed)));
            Debug.Log("[adventure-tools] " + report);
            return report;
        }

        [MenuItem("Tools/Wirenook/Adventure Tools/Rebuild")]
        public static void Rebuild()
        {
            foreach (var light in new[] { FlashlightSpec, LanternSpec })
            {
                HandLight(light, real: false);
                HandLight(light, real: true);
            }

            Megaphone();
            LaserPointer(ping: false);
            LaserPointer(ping: true);
            Notice();
            Beacon();
            Bundle(BundlePrefab, LaserPrefab, MegaphonePrefab, FlashlightPrefab, LanternPrefab, NoticePrefab);
            Bundle(BundleFlashlightPrefab, LaserPrefab, MegaphonePrefab, FlashlightRealPrefab, NoticePrefab);
            Bundle(BundleLanternPrefab, LaserPrefab, MegaphonePrefab, LanternRealPrefab, NoticePrefab);
            Bundle(BundleEventPrefab, LaserPingPrefab, MegaphonePrefab, FlashlightPrefab, LanternPrefab, NoticePrefab, BeaconPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[adventure-tools] Rebuilt prefabs and animators.");
        }

        /// <summary>
        /// The shared shape of a hand-held tool: a mount that Modular Avatar
        /// moves onto the hand, a grip that fits the tool into that hand, and
        /// the model below it.
        /// </summary>
        /// <param name="parameter">The bool that is on while the tool is out; the hand closes on it for as long.</param>
        /// <param name="curl">How far the fingers close round it, and <paramref name="indexCurl"/> the index finger; see <see cref="WirenookHandGrip"/>.</param>
        private static GameObject HeldModel(GameObject root, string modelFile, HumanBodyBones hand, string parameter,
            float curl, float indexCurl)
        {
            var mount = FeaturePrefab.Child(root, Mount);
            FeaturePrefab.AttachToBone(mount, hand);
            var grip = FeaturePrefab.Child(mount, Grip);
            var fit = FeaturePrefab.Ensure<WirenookHandGrip>(grip);
            fit.hand = hand;
            fit.held = new List<WirenookHeldWhen> { WirenookHeldWhen.On(parameter) };
            fit.curl = curl;
            fit.indexCurl = indexCurl;
            return FeaturePrefab.Model(grip, Models + "/" + modelFile);
        }

        /// <summary>
        /// Builds one of the hand lights, in one of two builds.
        ///
        /// The plain one shows light as glowing geometry (a beam cone, a
        /// halo), joins the avatar's mesh and costs nothing in performance
        /// rank. The real one leaves that geometry out for an actual light
        /// that lights the world for everyone: a spotlight in the flashlight,
        /// a point light in the lantern. VRChat ranks any avatar with a light
        /// "Poor" and with two "Very Poor", and a light's type cannot be
        /// animated to share one between them, so an avatar should carry at
        /// most one real one. The real one can also be left standing in the
        /// world: it has to be a separate mesh anyway, and a lamp is worth
        /// putting down.
        /// </summary>
        private static void HandLight(HandLightSpec light, bool real)
        {
            if (real) SyncedAnchor.Prepare();
            FeaturePrefab.Edit(light.Prefab(real), root =>
            {
                var mount = FeaturePrefab.Child(root, Mount);
                FeaturePrefab.AttachToBone(mount, HumanBodyBones.LeftHand);
                var grip = FeaturePrefab.Child(mount, Grip);
                var fit = FeaturePrefab.Ensure<WirenookHandGrip>(grip);
                fit.hand = HumanBodyBones.LeftHand;
                // In the hand while it is the light chosen, and, for one that
                // can be left standing in the world, not put down.
                fit.held = new List<WirenookHeldWhen> { WirenookHeldWhen.Is(LightParameter, light.Choice) };
                if (real) fit.held.Add(WirenookHeldWhen.Off(light.Placed));
                // A torch is a thick barrel; a lantern hangs from a thin bail the fingers hook under.
                fit.curl = light.SwingBone != null ? 0.75f : 0.5f;
                fit.indexCurl = fit.curl;
                fit.thumbCurl = light.SwingBone != null ? 0.85f : 0.7f;

                SyncedAnchor rig = null;
                GameObject model;
                System.Func<string, Transform> bone;
                if (real)
                {
                    // Not parented to the hand but following it, so that it
                    // can stop following and stay where it is.
                    var placed = FeaturePrefab.Child(root, "Placed");
                    rig = SyncedAnchor.Configure(root, placed, grip.transform, light.Placed);
                    model = RigidProp.Build(placed, Models + "/" + light.Model, Generated);
                    bone = name => FeaturePrefab.Find(model, name);
                }
                else
                {
                    model = FeaturePrefab.Model(grip, Models + "/" + light.Model);
                    bone = name => FeaturePrefab.Bone(model, name);
                }

                var body = bone(light.RootBone);
                var glow = bone(light.GlowBone);
                var glowPath = FeaturePrefab.PathTo(root, glow);
                string lampPath = null;

                if (light.SwingBone != null)
                {
                    // It hangs from the handle and swings with the hand.
                    var hanging = bone(light.SwingBone);
                    var swing = FeaturePrefab.Ensure<VRCPhysBone>(hanging.gameObject);
                    swing.rootTransform = hanging;
                    swing.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
                    swing.pull = 0.35f;
                    swing.spring = 0.5f;
                    swing.gravity = 0.45f;
                    swing.immobile = 0f;
                    swing.limitType = VRCPhysBoneBase.LimitType.Angle;
                    swing.maxAngleX = 55f;
                    swing.allowCollision = VRCPhysBoneBase.AdvancedBool.False;
                    swing.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
                    swing.allowPosing = VRCPhysBoneBase.AdvancedBool.True;
                }

                if (real)
                {
                    // The light itself replaces the geometry that stood in for it.
                    foreach (var renderer in glow.GetComponents<MeshRenderer>()) Object.DestroyImmediate(renderer);
                    foreach (var filter in glow.GetComponents<MeshFilter>()) Object.DestroyImmediate(filter);

                    // A child of the glow bone, and its only one: a PhysBone
                    // does not swing a bone that has more than one child. The
                    // bone's own axes depend on the export, so a spotlight is
                    // aimed by the model's.
                    var lamp = FeaturePrefab.Child(glow.gameObject, "Lamp");
                    lamp.transform.rotation = model.transform.rotation;
                    var source = FeaturePrefab.Ensure<Light>(lamp);
                    source.type = light.Type;
                    source.color = new Color(1f, 0.93f, 0.78f);
                    source.range = light.Range;
                    source.intensity = light.Intensity;
                    if (light.Type == LightType.Spot) source.spotAngle = light.SpotAngle;
                    source.shadows = LightShadows.None;
                    source.enabled = false;
                    lampPath = FeaturePrefab.PathTo(root, lamp.transform);
                }

                var fx = ControllerBuilder.Rebuild(light.Controller(real));
                fx.Int(LightParameter);
                PopToggle.Layer(fx, light.Label, FeaturePrefab.PathTo(root, body),
                    on => on.Equal(LightParameter, light.Choice), off => off.NotEqual(LightParameter, light.Choice),
                    (clip, phase) =>
                    {
                        // The light comes on once the tool has landed in the
                        // hand and goes off before it leaves, like a switch.
                        var lit = phase == "on";
                        if (real)
                        {
                            clip.Constant(lampPath, typeof(Light), "m_Enabled", lit ? 1f : 0f);
                            return;
                        }

                        if (!lit || light.SwingBone == null)
                        {
                            PopToggle.Scale(clip, glowPath, AnimationCurve.Constant(0f, 1f / 60f, lit ? 1f : 0f));
                            return;
                        }

                        // A slow breathing glow, so the flame reads as alive.
                        var breathe = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1.3f, 1.12f), new Keyframe(2.6f, 1f));
                        for (var key = 0; key < breathe.length; key++) breathe.SmoothTangents(key, 0f);
                        PopToggle.Scale(clip, glowPath, breathe);
                        clip.Loop();
                    });

                var parameters = new List<ParameterConfig> { FeaturePrefab.SyncedInt(LightParameter) };
                if (real)
                {
                    Placement(fx, rig, light);
                    parameters.Add(FeaturePrefab.SyncedBool(light.Placed));
                    parameters.AddRange(rig.Parameters());
                }

                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, parameters.ToArray());
                FeaturePrefab.InstallMenu(root);
                if (real)
                {
                    FeaturePrefab.MenuFolder(root, light.Label, light.Id);
                    FeaturePrefab.MenuToggle(FeaturePrefab.Child(root, "Menu Hold"), light.Label, light.Id, LightParameter, light.Choice);
                    FeaturePrefab.MenuToggle(FeaturePrefab.Child(root, "Menu Place"), "Place", "drop", light.Placed);
                }
                else
                {
                    FeaturePrefab.MenuToggle(root, light.Label, light.Id, LightParameter, light.Choice);
                }

                // Side by side on a thumbnail the two plain ones are one
                // thing, a light; a real one is the point of its variant.
                FeaturePrefab.Describe(root, light.Id, real ? light.Label + " (real light)" : light.Label, light.Id, "light", 30,
                    joinAvatarMesh: !real, summaryIcon: real ? light.Id : "flashlight");
            });
        }

        /// <summary>
        /// Leaving the light in the world and picking it up again. Putting
        /// it away picks it up too, so the next time it comes out in the hand
        /// rather than wherever it was left.
        /// </summary>
        private static void Placement(ControllerBuilder fx, SyncedAnchor rig, HandLightSpec light)
        {
            fx.Bool(light.Placed);
            var layer = fx.Layer(light.Label + " Placement");
            var idle = layer.State("Idle");
            var pickUp = layer.State("Pick Up");
            pickUp.Behaviour<VRCAvatarParameterDriver>(driver =>
            {
                driver.localOnly = true;
                driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                {
                    new VRC_AvatarParameterDriver.Parameter
                    {
                        type = VRC_AvatarParameterDriver.ChangeType.Set, name = light.Placed, value = 0f,
                    },
                };
            });
            idle.To(pickUp).NotEqual(LightParameter, light.Choice).IfTrue(light.Placed);
            pickUp.To(idle).IfFalse(light.Placed);
            rig.AddLayers(fx, light.Placed);
        }

        public static void Megaphone()
        {
            const string parameter = "WN/Megaphone";
            FeaturePrefab.Edit(MegaphonePrefab, root =>
            {
                var model = HeldModel(root, "megaphone.fbx", HumanBodyBones.RightHand, parameter, 0.65f, 0.65f);
                var body = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Megaphone"));
                var waves = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Megaphone.Waves"));
                WirenookSounds.Source(root);

                var fx = ControllerBuilder.Rebuild(Generated + "/megaphone-fx.controller");
                var states = PopToggle.Layer(fx, "Megaphone", parameter, body, (clip, phase) =>
                {
                    if (phase != "on")
                    {
                        PopToggle.Scale(clip, waves, AnimationCurve.Constant(0f, 1f / 60f, 0f));
                        return;
                    }

                    // Waves swell out of the horn and snap back, over and over.
                    var pulse = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.5f, 1.25f), new Keyframe(0.55f, 0.55f));
                    for (var key = 0; key < pulse.length; key++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(pulse, key, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(pulse, key, AnimationUtility.TangentMode.Linear);
                    }

                    PopToggle.Scale(clip, waves, pulse);
                    clip.Loop();
                });
                WirenookSounds.PlayOnEnter(states.Appear, WirenookSounds.SourceName, "megaphone");
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(parameter));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Megaphone", "megaphone", parameter);
                FeaturePrefab.Describe(root, "megaphone", "Megaphone", "megaphone", "megaphone", 40, joinAvatarMesh: true);
            });
        }

        /// <summary>
        /// Builds the laser pointer, in one of two builds.
        ///
        /// The plain one is a beam and a dot, joins the avatar's mesh and
        /// costs nothing in performance rank. The other can also ping: a
        /// click leaves a marker standing on the spot the dot is on for a few
        /// seconds. A marker that stays put while the hand moves on has to be
        /// a mesh of its own, so that build costs a mesh, a material slot and
        /// an audio source.
        /// </summary>
        public static void LaserPointer(bool ping)
        {
            const string parameter = "WN/Laser";
            const string ray = "WN/LaserRay";
            FeaturePrefab.Edit(ping ? LaserPingPrefab : LaserPrefab, root =>
            {
                // A slim barrel, with the index finger lying along it to aim.
                var model = HeldModel(root, "laser-pointer.fbx", HumanBodyBones.RightHand, parameter, 0.8f, 0.3f);
                var body = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "LaserPointer"));
                var beam = FeaturePrefab.Bone(model, "LaserPointer.Beam");
                var dot = FeaturePrefab.Bone(model, "LaserPointer.Dot");

                // The ray starts at the tip and runs along the model's forward
                // axis, whatever the bone's own axes turned out to be.
                var origin = FeaturePrefab.Child(beam.gameObject, "Ray");
                origin.transform.rotation = model.transform.rotation;
                var raycast = FeaturePrefab.Ensure<VRCRaycast>(origin);
                var serialized = new SerializedObject(raycast);
                // Version 1.1 is what allows hitting players and leaving the
                // result transform optional.
                serialized.FindProperty("version").enumValueIndex = 1;
                serialized.FindProperty("raycastDirection").vector3Value = Vector3.forward;
                serialized.FindProperty("distance").floatValue = LaserRange;
                serialized.FindProperty("collisionMode").enumValueIndex = 2; // worlds and players
                serialized.FindProperty("resultTransform").objectReferenceValue = dot;
                serialized.FindProperty("applyRotation").boolValue = false;
                serialized.FindProperty("behaviorOnMiss").enumValueIndex = 2; // snap to the far end
                serialized.FindProperty("parameter").stringValue = ray;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var fx = ControllerBuilder.Rebuild(Generated + (ping ? "/laser-pointer-ping-fx.controller" : "/laser-pointer-fx.controller"));
                PopToggle.Layer(fx, "Laser Pointer", parameter, body);

                // The beam mesh is one metre long, so stretching its bone along
                // the forward axis by the hit distance ends it at the surface.
                // The clip sweeps the whole range and the ray's hit ratio picks
                // the frame.
                var forward = beam.InverseTransformDirection(model.transform.forward);
                var axis = Mathf.Abs(forward.x) > Mathf.Abs(forward.y) && Mathf.Abs(forward.x) > Mathf.Abs(forward.z) ? "x"
                    : Mathf.Abs(forward.y) > Mathf.Abs(forward.z) ? "y" : "z";
                var beamPath = FeaturePrefab.PathTo(root, beam);
                fx.Float(ray + "_Ratio").Bool(ray + "_Hit");
                var length = fx.Clip("beam length", clip =>
                    clip.Curve(beamPath, typeof(Transform), "m_LocalScale." + axis, AnimationCurve.Linear(0f, 0f, 1f, LaserRange)));
                fx.Layer("Laser Beam").State("Length", length).MotionTime(ray + "_Ratio");

                var dotPath = FeaturePrefab.PathTo(root, dot);
                var dotLayer = fx.Layer("Laser Dot");
                var hidden = dotLayer.State("Hidden", fx.Clip("dot hidden", clip =>
                    PopToggle.Scale(clip, dotPath, AnimationCurve.Constant(0f, 1f / 60f, 0f))));
                var shown = dotLayer.State("Shown", fx.Clip("dot shown", clip =>
                    PopToggle.Scale(clip, dotPath, AnimationCurve.Constant(0f, 1f / 60f, 1f))));
                hidden.To(shown).IfTrue(ray + "_Hit");
                shown.To(hidden).IfFalse(ray + "_Hit");
                if (ping)
                {
                    Ping(root, fx, dot, parameter, ray + "_Hit");
                    // The fist that pings is a button while the pointer is out, not an expression.
                    var claim = FeaturePrefab.Ensure<WirenookHandClaim>(root);
                    claim.hand = WirenookHand.Right;
                    claim.parameter = parameter;
                }
                fx.Save();

                var parameters = new List<ParameterConfig>
                {
                    FeaturePrefab.SyncedBool(parameter),
                    // Each viewer's client casts the ray itself; nothing to sync.
                    Unsynced(ray + "_Ratio"), Unsynced(ray + "_Hit"), Unsynced(ray + "_Distance"),
                };
                if (ping) parameters.Add(FeaturePrefab.SyncedBool(PingParameter));

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, parameters.ToArray());
                FeaturePrefab.InstallMenu(root);
                if (ping)
                {
                    FeaturePrefab.MenuFolder(root, "Laser Pointer", "laser-pointer");
                    FeaturePrefab.MenuToggle(FeaturePrefab.Child(root, "Menu Hold"), "Laser Pointer", "laser-pointer", parameter);
                    // For hands whose controller never makes a fist on its own.
                    FeaturePrefab.MenuButton(FeaturePrefab.Child(root, "Menu Ping"), "Ping", "ping", PingParameter);
                }
                else
                {
                    FeaturePrefab.MenuToggle(root, "Laser Pointer", "laser-pointer", parameter);
                }

                // On a thumbnail the pointer, the megaphone and the notices are one
                // thing: ways to get a group's attention.
                FeaturePrefab.Describe(root, "laser-pointer", ping ? "Laser pointer with ping" : "Laser pointer", "laser-pointer",
                    "laser pointer", 40, joinAvatarMesh: true, summaryIcon: "megaphone");
            });
        }

        /// <summary>
        /// Clicking with the laser out leaves a marker on the spot it points
        /// at. "Clicking" is closing the right hand into a fist, which is what
        /// a trigger pull is on most controllers; the menu has a button for
        /// the rest.
        ///
        /// One synced bit says "ping now". Every viewer's client casts the
        /// laser's ray itself, so each one stands the marker where its own
        /// dot is at the moment the bit arrives. With the hand held still
        /// through the click that is the same spot for everyone; a hand that
        /// is sweeping when it clicks spreads the marker along the sweep by
        /// however far it travels during the network delay. A marker that
        /// agreed exactly would need a synced anchor: sixty-six bits and
        /// over half a second before it showed, for something that is gone
        /// in four.
        /// </summary>
        private static void Ping(GameObject root, ControllerBuilder fx, Transform dot, string laserParameter, string hitParameter)
        {
            // Under the feature root, not the hand, so it stands upright
            // however the hand is turned; the constraint only moves it.
            var anchor = FeaturePrefab.Child(root, "Ping Anchor");
            var follow = FeaturePrefab.Ensure<VRCPositionConstraint>(anchor);
            follow.ClearSources();
            follow.AddSource(new VRCConstraintSource { SourceTransform = dot, Weight = 1f });
            follow.PositionOffset = Vector3.zero;
            follow.IsActive = true;
            // Unlocked constraints re-measure their offset instead of moving the object.
            follow.Locked = true;
            follow.FreezeToWorld = false;
            follow.RebakeOffsetsWhenUnfrozen = false;

            var model = RigidProp.Build(anchor, Models + "/ping.fbx", Generated);
            var marker = FeaturePrefab.Find(model, "Ping");
            var anchorPath = FeaturePrefab.PathTo(root, anchor.transform);
            var markerPath = FeaturePrefab.PathTo(root, marker);

            // Heard from the spot itself, and from further off than the
            // other cues: it is there to be found.
            WirenookSounds.Source(anchor).GetComponent<VRCSpatialAudioSource>().Far = LaserRange;

            fx.Bool(PingParameter).Bool("IsLocal").Int("GestureRight");

            AnimationClip Marker(string name, bool standing, AnimationCurve scale, System.Action<ClipBuilder> more = null) =>
                fx.Clip("ping " + name, clip =>
                {
                    clip.Constant(anchorPath, typeof(VRCPositionConstraint), "FreezeToWorld", standing ? 1f : 0f,
                        scale[scale.length - 1].time);
                    PopToggle.Scale(clip, markerPath, scale);
                    more?.Invoke(clip);
                });

            // The model repeats every quarter turn, so a quarter turn a
            // second loops without a seam.
            var throb = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 1.12f), new Keyframe(1f, 1f));
            for (var key = 0; key < throb.length; key++) throb.SmoothTangents(key, 0f);

            var layer = fx.Layer("Laser Ping");
            var idle = layer.State("Idle", Marker("idle", false, AnimationCurve.Constant(0f, 1f / 60f, 0f)));
            var appear = layer.State("Appear", Marker("appear", true, PopToggle.Overshoot(0f, 1f, PopToggle.AppearSeconds)));
            var stand = layer.State("Stand", Marker("stand", true, throb, clip => clip
                .Curve(markerPath, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 1f, 90f))
                .Loop()));
            var leave = layer.State("Leave", Marker("leave", true, PopToggle.Overshoot(1f, 0f, PopToggle.DisappearSeconds)));
            var done = layer.State("Done", Marker("done", false, AnimationCurve.Constant(0f, 1f / 60f, 0f)));
            WirenookSounds.PlayOnEnter(appear, anchorPath + "/" + WirenookSounds.SourceName, "ping");

            // The wearer's client ends the ping; everyone else waits for that
            // to sync, so one ping can never play twice on a slow connection.
            done.Behaviour<VRCAvatarParameterDriver>(driver => SetLocally(driver, PingParameter, 0f));
            idle.To(appear).IfTrue(PingParameter);
            appear.To(stand).AfterTime(1f);
            stand.To(leave).AfterTime(PingLoops);
            leave.To(done).AfterTime(1f);
            done.To(idle).IfFalse(PingParameter);

            // The click. It fires once per fist, and only at something: a ray
            // that hits nothing ends in mid-air at the laser's full reach.
            var trigger = fx.Layer("Laser Ping Click");
            var ready = trigger.State("Ready");
            var fire = trigger.State("Fire");
            fire.Behaviour<VRCAvatarParameterDriver>(driver => SetLocally(driver, PingParameter, 1f));
            ready.To(fire).IfTrue("IsLocal").IfTrue(laserParameter).IfTrue(hitParameter).IfFalse(PingParameter).Equal("GestureRight", 1);
            fire.To(ready).NotEqual("GestureRight", 1);
        }

        private static void SetLocally(VRCAvatarParameterDriver driver, string parameter, float value)
        {
            driver.localOnly = true;
            driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
            {
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = parameter, value = value },
            };
        }

        private static ParameterConfig Unsynced(string name)
        {
            return new ParameterConfig { nameOrPrefix = name, syncType = ParameterSyncType.NotSynced, localOnly = true };
        }

        public static void Notice()
        {
            const string parameter = "WN/Notice";
            const string cardName = "Card";

            var spec = JObject.Parse(File.ReadAllText(Package + "/Textures/notices.json"));
            var notices = spec["notices"].Select(token => (id: (string)token["id"], icon: (string)token["icon"])).ToList();

            var material = AssetFolders.LoadOrCreate(Generated + "/notice.mat", () => new Material(Shader.Find("Wirenook/Screen Notice")));
            material.shader = Shader.Find("Wirenook/Screen Notice");
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Package + "/Textures/notice-atlas.png"));
            material.SetFloat("_Columns", (float)spec["columns"]);
            material.SetFloat("_Rows", (float)spec["rows"]);
            material.SetFloat("_Show", 0f);
            EditorUtility.SetDirty(material);

            var quad = AssetFolders.LoadOrCreate(Generated + "/notice-quad.asset", () => new Mesh());
            quad.Clear();
            quad.name = "Notice Quad";
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // The shader draws the card in front of the viewer wherever the
            // avatar is, but Unity only draws meshes whose bounds are in view.
            // These bounds are as large as VRChat's "Good" rank allows, so the
            // card shows whenever the avatar is roughly on screen.
            quad.bounds = new Bounds(Vector3.zero, new Vector3(3.6f, 3.6f, 3.6f));
            EditorUtility.SetDirty(quad);

            FeaturePrefab.Edit(NoticePrefab, root =>
            {
                var card = FeaturePrefab.Child(root, cardName);
                card.transform.localPosition = new Vector3(0f, 1f, 0f);
                FeaturePrefab.Ensure<MeshFilter>(card).sharedMesh = quad;
                var renderer = FeaturePrefab.Ensure<MeshRenderer>(card);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                card.SetActive(false);
                WirenookSounds.Source(root);

                var fx = ControllerBuilder.Rebuild(Generated + "/notice-fx.controller");
                fx.Int(parameter);
                var layer = fx.Layer("Notice");
                var idle = layer.State("Idle", fx.Clip("notice idle", clip => clip.Active(cardName, false)));
                var done = layer.State("Done", fx.Clip("notice done", clip => clip.Active(cardName, false)));

                // The wearer's client clears the choice once the card has
                // played; everyone else just waits for that to sync, so a
                // notice can never replay on a slow connection.
                done.Behaviour<VRCAvatarParameterDriver>(driver =>
                {
                    driver.localOnly = true;
                    driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                    {
                        new VRC_AvatarParameterDriver.Parameter
                        {
                            type = VRC_AvatarParameterDriver.ChangeType.Set, name = parameter, value = 0f,
                        },
                    };
                });
                done.To(idle).Equal(parameter, 0);

                // A card fades in, stays, and fades out, as three states, so
                // that switching it off (or picking another) can cut the stay
                // short and go straight to the fade-out. One that is still
                // fading in finishes that first; a card never just vanishes.
                AnimationClip Card(string name, int index, AnimationCurve show) => fx.Clip("notice " + name, clip => clip
                    .Active(cardName, AnimationCurve.Constant(0f, show[show.length - 1].time, 1f))
                    .Material<MeshRenderer>(cardName, "_Index", AnimationCurve.Constant(0f, show[show.length - 1].time, index))
                    .Material<MeshRenderer>(cardName, "_Show", show));

                var appearing = new List<StateBuilder>();
                var leaving = new List<StateBuilder>();
                for (var i = 0; i < notices.Count; i++)
                {
                    var chosen = i + 1;
                    var id = notices[i].id;
                    var appear = layer.State(id, Card(id + " appear", i, AnimationCurve.EaseInOut(0f, 0f, NoticeFadeIn, 1f)));
                    var stay = layer.State(id + " (stay)", Card(id + " stay", i, AnimationCurve.Constant(0f, NoticeSeconds, 1f)));
                    var leave = layer.State(id + " (leave)", Card(id + " leave", i, AnimationCurve.EaseInOut(0f, 1f, NoticeFadeOut, 0f)));
                    WirenookSounds.PlayOnEnter(appear, WirenookSounds.SourceName, "chime");

                    idle.To(appear).Equal(parameter, chosen);
                    appear.To(leave).AfterTime(1f).NotEqual(parameter, chosen);
                    appear.To(stay).AfterTime(1f);
                    stay.To(leave).NotEqual(parameter, chosen);
                    stay.To(leave).AfterTime(1f);
                    // Still chosen means it ran its full time: clear the choice.
                    leave.To(done).AfterTime(1f).Equal(parameter, chosen);
                    leave.To(idle).AfterTime(1f).Equal(parameter, 0);
                    appearing.Add(appear);
                    leaving.Add(leave);
                }

                // Another notice picked meanwhile comes up once this one has gone.
                for (var from = 0; from < leaving.Count; from++)
                {
                    for (var to = 0; to < appearing.Count; to++)
                    {
                        if (from != to) leaving[from].To(appearing[to]).AfterTime(1f).Equal(parameter, to + 1);
                    }
                }

                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedInt(parameter));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Notice", "notice");
                for (var i = 0; i < notices.Count; i++)
                {
                    var item = FeaturePrefab.Child(root, "Menu " + (i + 1));
                    FeaturePrefab.MenuToggle(item, notices[i].id, notices[i].icon, parameter, i + 1);
                }

                // The card is drawn by a shader of this package's own, and mobile
                // builds may only use VRChat's.
                FeaturePrefab.Describe(root, "notice", "Screen notices", "notice", "screen notice", 20,
                    summaryIcon: "megaphone", pcOnly: true);
            });
        }

        /// <summary>How far away the beacon can be seen from, in metres.</summary>
        private const float BeaconRange = 80f;

        private const float BeaconFade = 0.25f;

        /// <summary>
        /// A pin over the wearer's head that shows through walls, and an
        /// arrow in everyone else's view that points the way to turn when the
        /// pin is out of it: for whoever is leading a group through a world
        /// where people get separated.
        /// </summary>
        public static void Beacon()
        {
            const string parameter = "WN/Beacon";
            const string markName = "Mark";

            var shader = Shader.Find("Wirenook/Beacon");
            var material = AssetFolders.LoadOrCreate(Generated + "/beacon.mat", () => new Material(shader));
            material.shader = shader;
            material.SetFloat("_Show", 0f);
            material.SetFloat("_Range", BeaconRange);
            EditorUtility.SetDirty(material);

            // Two quads: the pin, then the arrow. The shader places both and
            // tells them apart by the second UV set.
            var quads = AssetFolders.LoadOrCreate(Generated + "/beacon-quads.asset", () => new Mesh());
            quads.Clear();
            quads.name = "beacon-quads";
            var corners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quads.vertices = corners.Concat(corners).Select(corner => new Vector3(corner.x - 0.5f, corner.y - 0.5f)).ToArray();
            quads.uv = corners.Concat(corners).ToArray();
            quads.uv2 = corners.Select(_ => Vector2.zero).Concat(corners.Select(_ => Vector2.right)).ToArray();
            quads.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            // Unity only draws meshes whose bounds are in view, and the arrow
            // is for exactly when the wearer is not. Bounds as large as the
            // beacon's range keep it drawn wherever the viewer is looking.
            quads.bounds = new Bounds(Vector3.zero, Vector3.one * (2f * BeaconRange));
            EditorUtility.SetDirty(quads);

            FeaturePrefab.Edit(BeaconPrefab, root =>
            {
                var mount = FeaturePrefab.Child(root, Mount);
                FeaturePrefab.AttachToBone(mount, HumanBodyBones.Head);
                var mark = FeaturePrefab.Child(mount, markName);
                FeaturePrefab.Ensure<MeshFilter>(mark).sharedMesh = quads;
                var renderer = FeaturePrefab.Ensure<MeshRenderer>(mark);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                mark.SetActive(false);
                var markPath = FeaturePrefab.PathTo(root, mark.transform);

                var fx = ControllerBuilder.Rebuild(Generated + "/beacon-fx.controller");
                fx.Bool(parameter).Bool("IsLocal");

                // Switched off, the object is inactive and scaled to nothing:
                // at zero scale its bounds are a point, so a beacon that is
                // not in use does not make the avatar any larger to cull, or
                // to rank.
                AnimationClip Mark(string name, bool present, AnimationCurve show) => fx.Clip("beacon " + name, clip =>
                {
                    var seconds = show[show.length - 1].time;
                    clip.Active(markPath, AnimationCurve.Constant(0f, seconds, present ? 1f : 0f))
                        .Material<MeshRenderer>(markPath, "_Show", show);
                    PopToggle.Scale(clip, markPath, AnimationCurve.Constant(0f, seconds, present ? 1f : 0f));
                });

                var layer = fx.Layer("Beacon");
                var off = layer.State("Off", Mark("off", false, AnimationCurve.Constant(0f, 1f / 60f, 0f)));
                var appear = layer.State("Appear", Mark("appear", true, AnimationCurve.EaseInOut(0f, 0f, BeaconFade, 1f)));
                var on = layer.State("On", Mark("on", true, AnimationCurve.Constant(0f, 1f / 60f, 1f)));
                var disappear = layer.State("Disappear", Mark("disappear", true, AnimationCurve.EaseInOut(0f, 1f, BeaconFade, 0f)));
                // Not for the wearer, who knows where they are; an arrow
                // pointing at one's own head would only ever point down.
                off.To(appear).IfTrue(parameter).IfFalse("IsLocal");
                appear.To(on).AfterTime(1f);
                on.To(disappear).IfFalse(parameter);
                disappear.To(off).AfterTime(1f);
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(parameter));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Beacon", "beacon", parameter);
                // Drawn by a shader of this package's own, and mobile builds
                // may only use VRChat's.
                FeaturePrefab.Describe(root, "beacon", "Beacon and compass", "beacon", "beacon", 35, pcOnly: true);
            });
        }

        /// <summary>
        /// A set of tools behind one "Tools" entry, so adding it costs an
        /// avatar a single slot in its top-level menu.
        /// </summary>
        /// <param name="toolPrefabs">The tools the set carries, each in the build wanted, in menu order.</param>
        private static void Bundle(string bundlePath, params string[] toolPrefabs)
        {
            FeaturePrefab.Edit(bundlePath, root =>
            {
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Tools", "more");
                var wanted = toolPrefabs.Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToList();

                // A set built before the tools were rearranged holds ones that
                // are no longer part of it.
                foreach (var child in root.transform.Cast<Transform>().ToList())
                {
                    if (!wanted.Contains(PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject)))
                        Object.DestroyImmediate(child.gameObject);
                }

                for (var i = 0; i < wanted.Count; i++)
                {
                    var prefab = wanted[i];
                    var instance = root.transform.Cast<Transform>()
                                       .Select(child => child.gameObject)
                                       .FirstOrDefault(child => PrefabUtility.GetCorrespondingObjectFromSource(child) == prefab)
                                   ?? (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    // The menu lists them in the order they sit in.
                    instance.transform.SetSiblingIndex(i);
                    // Each tool installs itself at the top of the menu when
                    // used alone; inside the set the folder above does it.
                    var installer = instance.GetComponent<ModularAvatarMenuInstaller>();
                    if (installer != null) Object.DestroyImmediate(installer);
                }
            });
        }
    }
}
