using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using Wirenook.Assets;
using Wirenook.Assets.Editor;

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
        public const string FlashlightLightPrefab = Prefabs + "/Wirenook Flashlight (Real Light).prefab";
        public const string LanternPrefab = Prefabs + "/Wirenook Lantern.prefab";
        public const string MegaphonePrefab = Prefabs + "/Wirenook Megaphone.prefab";
        public const string LaserPrefab = Prefabs + "/Wirenook Laser Pointer.prefab";
        public const string NoticePrefab = Prefabs + "/Wirenook Notice.prefab";
        public const string BundlePrefab = Prefabs + "/Wirenook Adventure Tools.prefab";

        private const string Mount = "Mount";
        private const string Grip = "Grip";

        /// <summary>How far the laser reaches, in metres.</summary>
        private const float LaserRange = 30f;

        /// <summary>How long a notice stays up, in seconds.</summary>
        private const float NoticeSeconds = 4.5f;

        [MenuItem("Tools/Wirenook/Adventure Tools/Rebuild")]
        public static void Rebuild()
        {
            Flashlight();
            Lantern();
            Megaphone();
            LaserPointer();
            Notice();
            Bundle();
            AssetDatabase.SaveAssets();
            Debug.Log("[adventure-tools] Rebuilt prefabs and animators.");
        }

        /// <summary>
        /// The shared shape of a hand-held tool: a mount that Modular Avatar
        /// moves onto the hand, a grip that fits the tool into that hand, and
        /// the model below it.
        /// </summary>
        private static GameObject HeldModel(GameObject root, string modelFile, HumanBodyBones hand)
        {
            var mount = FeaturePrefab.Child(root, Mount);
            FeaturePrefab.AttachToBone(mount, hand);
            var grip = FeaturePrefab.Child(mount, Grip);
            FeaturePrefab.Ensure<WirenookHandGrip>(grip).hand = hand;
            return FeaturePrefab.Model(grip, Models + "/" + modelFile);
        }

        public static void Flashlight()
        {
            const string parameter = "WN/Flashlight";
            const string controller = Generated + "/flashlight-fx.controller";
            const string lightName = "Light";

            string lightPath = null;
            void Configure(GameObject root)
            {
                var model = HeldModel(root, "flashlight.fbx", HumanBodyBones.LeftHand);
                var body = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Flashlight"));
                var beam = FeaturePrefab.Bone(model, "Flashlight.Beam");
                var beamPath = FeaturePrefab.PathTo(root, beam);
                lightPath = beamPath + "/" + lightName;

                var fx = ControllerBuilder.Rebuild(controller);
                PopToggle.Layer(fx, "Flashlight", parameter, body, (clip, phase) =>
                {
                    // The beam switches on once the torch has landed in the
                    // hand and off before it leaves, like a real switch.
                    var lit = phase == "on";
                    PopToggle.Scale(clip, beamPath, AnimationCurve.Constant(0f, 1f / 60f, lit ? 1f : 0f));
                    // Only the real-light variant has this object; the curve
                    // is inert on the plain one.
                    clip.Constant(lightPath, typeof(Light), "m_Enabled", lit ? 1f : 0f);
                });
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(parameter));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Flashlight", "flashlight", parameter);
                FeaturePrefab.Describe(root, "flashlight", "Flashlight", "flashlight", "flashlight", 30, joinAvatarMesh: true);
            }

            FeaturePrefab.Edit(FlashlightPrefab, Configure);

            // A real spotlight lights the world for everyone, and costs the
            // avatar its performance rank: any light at all is "Poor". It is a
            // separate prefab so that cost is a choice.
            Variant(FlashlightPrefab, FlashlightLightPrefab, root =>
            {
                var beam = FeaturePrefab.Find(root, "Flashlight.Beam");
                var holder = FeaturePrefab.Child(beam.gameObject, lightName);
                // The beam bone's own axes depend on the export; aim along the
                // model's forward instead.
                holder.transform.rotation = FeaturePrefab.Find(root, Grip).GetChild(0).rotation;
                var light = FeaturePrefab.Ensure<Light>(holder);
                light.type = LightType.Spot;
                light.color = new Color(1f, 0.93f, 0.78f);
                light.intensity = 1.6f;
                light.range = 14f;
                light.spotAngle = 55f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                FeaturePrefab.Describe(root, "flashlight", "Flashlight (real light)", "flashlight", "flashlight", 30, joinAvatarMesh: true);
            });
        }

        public static void Lantern()
        {
            const string parameter = "WN/Lantern";
            FeaturePrefab.Edit(LanternPrefab, root =>
            {
                var model = HeldModel(root, "lantern.fbx", HumanBodyBones.LeftHand);
                var handle = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Lantern"));
                var body = FeaturePrefab.Bone(model, "Lantern.Body");
                var glow = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Lantern.Glow"));

                // The body hangs from the handle and swings with the hand.
                var swing = FeaturePrefab.Ensure<VRCPhysBone>(body.gameObject);
                swing.rootTransform = body;
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

                var fx = ControllerBuilder.Rebuild(Generated + "/lantern-fx.controller");
                PopToggle.Layer(fx, "Lantern", parameter, handle, (clip, phase) =>
                {
                    if (phase != "on")
                    {
                        PopToggle.Scale(clip, glow, AnimationCurve.Constant(0f, 1f / 60f, 0f));
                        return;
                    }

                    // A slow breathing glow, so the flame reads as alive.
                    var breathe = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1.3f, 1.12f), new Keyframe(2.6f, 1f));
                    for (var key = 0; key < breathe.length; key++) breathe.SmoothTangents(key, 0f);
                    PopToggle.Scale(clip, glow, breathe);
                    clip.Loop();
                });
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(parameter));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Lantern", "lantern", parameter);
                FeaturePrefab.Describe(root, "lantern", "Lantern", "lantern", "lantern", 30, joinAvatarMesh: true);
            });
        }

        public static void Megaphone()
        {
            const string parameter = "WN/Megaphone";
            FeaturePrefab.Edit(MegaphonePrefab, root =>
            {
                var model = HeldModel(root, "megaphone.fbx", HumanBodyBones.RightHand);
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

        public static void LaserPointer()
        {
            const string parameter = "WN/Laser";
            const string ray = "WN/LaserRay";
            FeaturePrefab.Edit(LaserPrefab, root =>
            {
                var model = HeldModel(root, "laser-pointer.fbx", HumanBodyBones.RightHand);
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

                var fx = ControllerBuilder.Rebuild(Generated + "/laser-pointer-fx.controller");
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
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root,
                    FeaturePrefab.SyncedBool(parameter),
                    // Each viewer's client casts the ray itself; nothing to sync.
                    Unsynced(ray + "_Ratio"), Unsynced(ray + "_Hit"), Unsynced(ray + "_Distance"));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Laser Pointer", "laser-pointer", parameter);
                FeaturePrefab.Describe(root, "laser-pointer", "Laser pointer", "laser-pointer", "laser pointer", 40, joinAvatarMesh: true);
            });
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

                var showing = new List<StateBuilder>();
                for (var i = 0; i < notices.Count; i++)
                {
                    var index = i;
                    var state = layer.State(notices[i].id, fx.Clip("notice " + notices[i].id, clip =>
                    {
                        var fade = new AnimationCurve(
                            new Keyframe(0f, 0f), new Keyframe(0.25f, 1f),
                            new Keyframe(NoticeSeconds - 0.4f, 1f), new Keyframe(NoticeSeconds, 0f));
                        clip.Active(cardName, true)
                            .Material<MeshRenderer>(cardName, "_Index", index)
                            .Material<MeshRenderer>(cardName, "_Show", fade);
                    }));
                    WirenookSounds.PlayOnEnter(state, WirenookSounds.SourceName, "chime", 0.4f);
                    idle.To(state).Equal(parameter, i + 1);
                    state.To(done).AfterTime(1f);
                    showing.Add(state);
                }

                // Picking another notice while one is up replaces it at once.
                for (var from = 0; from < showing.Count; from++)
                {
                    for (var to = 0; to < showing.Count; to++)
                    {
                        if (from != to) showing[from].To(showing[to]).Equal(parameter, to + 1);
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

                FeaturePrefab.Describe(root, "notice", "Screen notices", "notice", "screen notice", 20);
            });
        }

        /// <summary>
        /// All the tools behind one "Tools" entry, so adding the set costs an
        /// avatar a single slot in its top-level menu.
        /// </summary>
        public static void Bundle()
        {
            FeaturePrefab.Edit(BundlePrefab, root =>
            {
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Tools", "more");
                foreach (var path in new[] { LaserPrefab, MegaphonePrefab, FlashlightPrefab, LanternPrefab, NoticePrefab })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var instance = root.transform.Cast<Transform>()
                                       .Select(child => child.gameObject)
                                       .FirstOrDefault(child => PrefabUtility.GetCorrespondingObjectFromSource(child) == prefab)
                                   ?? (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    // Each tool installs itself at the top of the menu when
                    // used alone; inside the set the folder above does it.
                    var installer = instance.GetComponent<ModularAvatarMenuInstaller>();
                    if (installer != null) Object.DestroyImmediate(installer);
                }
            });
        }

        private static void Variant(string parentPath, string path, System.Action<GameObject> configure)
        {
            if (!File.Exists(path))
            {
                var parent = AssetDatabase.LoadAssetAtPath<GameObject>(parentPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(parent);
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Object.DestroyImmediate(instance);
            }

            FeaturePrefab.Edit(path, configure);
        }
    }
}
