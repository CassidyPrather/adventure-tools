using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.WorldSync.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Generates the game master's kit: dice, a pause, zone markers, a
    /// light and a laser on the fingertip, a portal with two ends and a
    /// skeleton key for contacts, behind one "Game Master" entry in the
    /// menu, with the roleplay notices beside them.
    ///
    /// All of it is one prefab, because all of it shares three things: the
    /// <see cref="Stage"/> everything left in the world stands on (one
    /// anchor bus, so one position's worth of synced bits), the
    /// <see cref="Pointer"/> it is aimed with, and two meshes made in code
    /// (<see cref="ShapeMesh"/>): one that every platform draws in a single
    /// material, and one for PC with the shapes that need this package's
    /// own shaders.
    ///
    /// A kit for one module is the same thing with more tools in the list:
    /// <see cref="Build"/> takes the list, and each tool
    /// (<see cref="IStageTool"/>) is handed the stage, the pointer and the
    /// meshes.
    /// </summary>
    public static class GameMasterBuild
    {
        private const string Package = AdventureToolsBuild.Package;
        private const string Generated = Package + "/Generated";
        private const string Prefabs = Package + "/Prefabs";
        private const string Textures = Package + "/Textures";

        public const string KitPrefab = Prefabs + "/Wirenook Game Master.prefab";
        public const string KitController = Generated + "/game-master-fx.controller";
        public const string RoleplayNoticePrefab = Prefabs + "/Wirenook Notice (Roleplay).prefab";

        /// <summary>
        /// What the kit's one mesh is drawn with: the wirenook palette on
        /// the toon shader, with its two cyans marked as the signature
        /// colour, so that they turn with the wearer's choice on every
        /// platform. Only those glow.
        /// </summary>
        public const string StageMaterial = Generated + "/stage.mat";

        /// <summary>The faint colour laid over everything inside a zone.</summary>
        public const string TintMaterial = Generated + "/stage-tint.mat";

        /// <summary>The dashes of the line round a zone.</summary>
        public const string DashMaterial = Generated + "/stage-dash.mat";

        /// <summary>The dimness laid over everything inside a pause.</summary>
        public const string DimMaterial = Generated + "/stage-dim.mat";

        /// <summary>The card shown on the screens of the people inside a pause.</summary>
        public const string CardMaterial = Generated + "/stage-card.mat";

        /// <summary>The trail behind the lit fingertip.</summary>
        public const string TrailMaterial = Generated + "/stage-trail.mat";

        /// <summary>White on the swatches of the wirenook palette that follow the signature colour, and that glow.</summary>
        public const string StageGlow = Textures + "/stage-palette-emission.png";

        public const string CardAtlas = Textures + "/stage-card-atlas.png";
        public const string CardSpec = Textures + "/stage-cards.json";

        public const string FeatureId = "game-master";

        /// <summary>A float that is 1 on every client: the weight of whatever a direct blend tree always plays.</summary>
        public static string One => SyncedAnchor.One;

        /// <summary>The whole kit, in menu order.</summary>
        public static List<IStageTool> Kit() => new List<IStageTool>
        {
            new DiceTool(), new PauseTool(), new ZoneTool(), new PointerTool(), new PortalTool(), new KeyTool(),
        };

        /// <summary>A material this file generates, by its path.</summary>
        public static Material Material(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new InvalidOperationException(path + " is missing; run Tools > Wirenook > Adventure Tools > Rebuild Game Master.");
            return material;
        }

        /// <summary>A parameter that lives in the animator alone, on every client separately.</summary>
        public static ParameterConfig Own(string name, float defaultValue = 0f)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name, syncType = ParameterSyncType.NotSynced, localOnly = true,
                defaultValue = defaultValue, hasExplicitDefaultValue = true,
            };
        }

        /// <summary>A parameter the menu sets on the wearer's client and nobody else hears of.</summary>
        public static ParameterConfig Mine(string name, ParameterSyncType type, bool saved = false)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name, syncType = type, localOnly = true, saved = saved,
                defaultValue = 0f, hasExplicitDefaultValue = true,
            };
        }

        /// <summary>Makes a menu item one that only the wearer's client hears of.</summary>
        public static void Local(GameObject holder)
        {
            var item = holder.GetComponent<ModularAvatarMenuItem>();
            item.isSynced = false;
            item.isSaved = false;
        }

        /// <summary>
        /// A child that carries shapes or stands for a place: made if it is
        /// not there, and put back where it rests. A rebuild finds the
        /// prefab as the last one left it, with bones resting at the scale
        /// their animator starts them at, and a mesh must not be bound to a
        /// bone of no size.
        /// </summary>
        public static Transform Bone(GameObject parent, string name, Vector3 position)
        {
            var bone = FeaturePrefab.Child(parent, name).transform;
            bone.localPosition = position;
            bone.localRotation = Quaternion.identity;
            bone.localScale = Vector3.one;
            return bone;
        }

        /// <summary>
        /// A direct blend tree whose children each have a weight of their
        /// own: what it gives a property is the sum, over the children that
        /// animate it, of the child's value times its weight.
        /// </summary>
        public static BlendTree Weighted(ControllerBuilder fx, string name, params (string weight, Motion motion)[] children)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.Direct, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, fx.Controller);
            foreach (var (_, motion) in children) tree.AddChild(motion);
            var entries = tree.children;
            for (var index = 0; index < entries.Length; index++)
            {
                fx.Float(children[index].weight, children[index].weight == One ? 1f : 0f);
                entries[index].directBlendParameter = children[index].weight;
            }

            tree.children = entries;
            return tree;
        }

        /// <summary>
        /// Adds a layer that scales a bone to the radius a dial says
        /// (<see cref="ZoneModel"/>): one clip that sweeps the whole range,
        /// read at the dial's amount.
        /// </summary>
        /// <param name="also">Further curves that follow the radius: called with the clip and the radius over the dial's travel.</param>
        public static void Sized(ControllerBuilder fx, string layerName, string path, string dial, Action<ClipBuilder, AnimationCurve> also = null)
        {
            fx.Float(dial, ZoneModel.RestingAmount);
            const int keys = 17;
            var radius = new AnimationCurve(Enumerable.Range(0, keys)
                .Select(key => new Keyframe(key / (keys - 1f), ZoneModel.Radius(key / (keys - 1f)))).ToArray());
            for (var key = 0; key < radius.length; key++) radius.SmoothTangents(key, 0f);
            var clip = fx.Clip(layerName.ToLowerInvariant(), built =>
            {
                PopToggle.Scale(built, path, radius);
                also?.Invoke(built, radius);
            });
            fx.Layer(layerName).State("Size", clip).MotionTime(dial);
        }

        [MenuItem("Tools/Wirenook/Adventure Tools/Rebuild Game Master")]
        public static void Rebuild()
        {
            Stage.Prepare();
            Materials();
            Notices.Build(Notices.Roleplay);
            Build(KitPrefab, KitController, Generated + "/game-master", new GameMasterOptions
            {
                Carried = { (RoleplayNoticePrefab, true) },
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[adventure-tools] Rebuilt the game master's kit.");
        }

        /// <summary>Checks the generated kit as far as the editor can; see <see cref="GameMasterCheck"/>.</summary>
        [MenuItem("Tools/Wirenook/Adventure Tools/Verify Game Master")]
        public static string Verify()
        {
            var report = GameMasterCheck.Run();
            Debug.Log("[adventure-tools] " + report);
            return report;
        }

        private static T Asset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new System.IO.FileNotFoundException("Missing " + path + "; see the package's README for the tool that writes it.", path);
            return asset;
        }

        /// <summary>The materials the kit is drawn with. Call before <see cref="Build"/>.</summary>
        public static void Materials()
        {
            var palette = Asset<Texture2D>(WirenookMaterials.PalettePath);
            var glow = Asset<Texture2D>(StageGlow);
            var stage = AssetFolders.LoadOrCreate(StageMaterial, () => new Material(Shader.Find(ToonMaterial.ShaderName)));
            ToonMaterial.Configure(stage, palette, glow, doubleSided: false);
            WirenookSignature.Paint(stage, glow);

            Material Volume(string path, float strength, float dim, float rim, int bit, bool spare)
            {
                var shader = Shader.Find("Wirenook/Stage Volume");
                var material = AssetFolders.LoadOrCreate(path, () => new Material(shader));
                material.shader = shader;
                material.SetFloat(WirenookSignature.Property, 0f);
                material.SetFloat("_Strength", strength);
                material.SetFloat("_Dim", dim);
                material.SetFloat("_Rim", rim);
                material.SetFloat("_Bit", bit);
                material.SetFloat("_Spare", ToonMaterial.AvatarStencil);
                material.SetFloat("_SpareWhen", (float)(spare ? UnityEngine.Rendering.CompareFunction.Equal : UnityEngine.Rendering.CompareFunction.Never));
                EditorUtility.SetDirty(material);
                return material;
            }

            // A bit each, so that a zone's tint, its dashes and a pause's
            // dimness do not cancel where they overlap. They are the three
            // below the one wirenook avatars mark themselves with.
            Volume(TintMaterial, 0.07f, 0f, 0.1f, 0x10, false);
            Volume(DashMaterial, 0.8f, 0f, 0f, 0x20, false);
            Volume(DimMaterial, 0.55f, 1f, 0f, 0x40, true);

            var spec = JObject.Parse(System.IO.File.ReadAllText(CardSpec));
            var cardShader = Shader.Find("Wirenook/Stage Notice");
            var card = AssetFolders.LoadOrCreate(CardMaterial, () => new Material(cardShader));
            card.shader = cardShader;
            card.SetTexture("_MainTex", Asset<Texture2D>(CardAtlas));
            card.SetFloat("_Columns", (float)spec["columns"]);
            card.SetFloat("_Rows", (float)spec["rows"]);
            card.SetFloat("_Index", 0f);
            card.SetFloat("_Show", 0f);
            EditorUtility.SetDirty(card);

            var trailShader = Shader.Find("Wirenook/Glow Trail");
            var trail = AssetFolders.LoadOrCreate(TrailMaterial, () => new Material(trailShader));
            trail.shader = trailShader;
            trail.SetFloat(WirenookSignature.Property, 0f);
            EditorUtility.SetDirty(trail);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Builds a kit into a prefab, in place.
        /// </summary>
        /// <param name="controllerPath">Where its animator is written.</param>
        /// <param name="meshPrefix">Where its meshes are written: this, then "-stage.asset" and "-effects.asset".</param>
        public static void Build(string prefabPath, string controllerPath, string meshPrefix, GameMasterOptions options = null)
        {
            options = options ?? new GameMasterOptions();
            var tools = options.Tools ?? Kit();
            FeaturePrefab.Edit(prefabPath, root =>
            {
                var kit = new GameMaster { Root = root, Options = options };
                var plan = new StagePlan();
                foreach (var tool in tools) tool.Things(plan);

                WirenookSounds.Source(root);
                kit.Pointer = Pointer.Configure(root, options.Pointer ?? new PointerOptions { Prefix = options.Prefix + "/Finger" });
                kit.Stage = Stage.Configure(root, kit.Pointer, options.Stage ?? new StageOptions { Prefix = options.Prefix + "/Stage" },
                    plan.Things.ToArray());
                kit.Solid = new ShapeMesh(WirenookPalette.Uv);
                // The PC-only shapes are coloured by their shaders.
                kit.Effects = new ShapeMesh(_ => Vector2.zero).Colour("");
                kit.Pointer.AddShapes(kit.Solid);
                kit.Stage.AddShapes(kit.Solid);
                foreach (var tool in tools) tool.Build(kit);

                // Not part of the wearer's figure: what hides or redraws
                // the avatar leaves both meshes alone, and each tool says
                // for itself who sees what.
                var solid = kit.Solid.Build(root, GameMaster.SolidName, meshPrefix + "-stage.asset", Material(StageMaterial));
                kit.Stage.Reach(solid);
                FeaturePrefab.Ensure<WirenookApart>(solid.gameObject);

                var effectsHolder = root.transform.Find(GameMaster.EffectsName);
                if (kit.EffectMaterials.Count > 0)
                {
                    var effects = kit.Effects.Build(root, GameMaster.EffectsName, meshPrefix + "-effects.asset", kit.EffectMaterials.ToArray());
                    kit.Stage.Reach(effects);
                    FeaturePrefab.Ensure<WirenookApart>(effects.gameObject);
                    // Shaders of this package's own, and mobile builds may only use VRChat's.
                    FeaturePrefab.Ensure<WirenookPcOnly>(effects.gameObject);
                }
                else if (effectsHolder != null)
                {
                    UnityEngine.Object.DestroyImmediate(effectsHolder.gameObject);
                }

                kit.Pointer.Trail.GetComponent<TrailRenderer>().sharedMaterial = Material(TrailMaterial);

                var fx = ControllerBuilder.Rebuild(controllerPath);
                kit.Fx = fx;
                kit.Stage.AddLayers(fx);
                kit.Pointer.AddLayers(fx, Audience.WhenTrue(kit.Stage.Aiming), kit.KeepDrawn);
                foreach (var tool in tools) tool.Layers(kit);
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                kit.Parameters.AddRange(kit.Stage.Parameters());
                kit.Parameters.AddRange(kit.Pointer.Parameters());

                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, options.MenuLabel, FeatureId);
                foreach (var tool in tools) tool.Menu(kit);
                Carry(root, options.Carried);
                kit.Stage.MenuClear(kit.Child("Menu Clear"));
                Order(root);

                FeaturePrefab.Parameters(root, kit.Parameters.ToArray());
                FeaturePrefab.Describe(root, FeatureId, "Game master's kit", FeatureId, "game master", 8);
            });
        }

        /// <summary>
        /// Carries other prefabs inside the kit's menu folder, as the tool
        /// sets of this package do: each keeps its own menu entry, which
        /// becomes one of the folder's.
        /// </summary>
        private static void Carry(GameObject root, List<(string prefab, bool pcOnly)> carried)
        {
            var wanted = carried.Select(entry => AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefab)).ToList();
            if (wanted.Any(prefab => prefab == null))
                throw new InvalidOperationException("The kit is asked to carry a prefab that is not there: " +
                                                    string.Join(", ", carried.Select(entry => entry.prefab)));

            // One built before the list changed holds prefabs that are no longer on it.
            foreach (var child in root.transform.Cast<Transform>().ToList())
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (source != null && PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) && !wanted.Contains(source))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            for (var index = 0; index < wanted.Count; index++)
            {
                var prefab = wanted[index];
                var instance = root.transform.Cast<Transform>().Select(child => child.gameObject)
                                   .FirstOrDefault(child => PrefabUtility.GetCorrespondingObjectFromSource(child) == prefab)
                               ?? (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                // Each installs itself at the top of the menu when used alone; inside the kit the folder does it.
                var installer = instance.GetComponent<ModularAvatarMenuInstaller>();
                if (installer != null) UnityEngine.Object.DestroyImmediate(installer);
                var marker = instance.GetComponent<WirenookPcOnly>();
                if (carried[index].pcOnly && marker == null) instance.AddComponent<WirenookPcOnly>();
            }
        }

        /// <summary>The menu lists entries in the order their objects sit in: the tools' folders as they were made, then what is carried, then "Clear all".</summary>
        private static void Order(GameObject root)
        {
            foreach (var entry in root.transform.Cast<Transform>().Where(child => PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)).ToList())
                entry.SetAsLastSibling();
            var clear = root.transform.Find("Menu Clear");
            if (clear != null) clear.SetAsLastSibling();
        }
    }
}
