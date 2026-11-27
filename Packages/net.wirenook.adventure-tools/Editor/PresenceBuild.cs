using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Generates the invisible switch and presence: their prefabs,
    /// animators and the material a spectre is drawn with on mobile. Everything
    /// under Generated/ and Prefabs/ that they name is rebuilt in place by
    /// <see cref="Rebuild"/>.
    /// </summary>
    public static class PresenceBuild
    {
        private const string Package = AdventureToolsBuild.Package;
        private const string Generated = Package + "/Generated";
        private const string Prefabs = Package + "/Prefabs";

        /// <summary>The invisible switch: one synced bit, and nothing of the avatar is drawn for anyone else.</summary>
        public const string UnseenPrefab = Prefabs + "/Wirenook Unseen.prefab";

        /// <summary>Presence for someone who starts out seen and heard by all: a game master.</summary>
        public const string PresencePrefab = Prefabs + "/Wirenook Presence.prefab";

        /// <summary>Presence for someone who starts out a ghost, seen and heard backstage only: a spectator.</summary>
        public const string GhostPrefab = Prefabs + "/Wirenook Presence (Ghost).prefab";

        public const string UnseenController = Generated + "/unseen-fx.controller";
        public const string PresenceController = Generated + "/presence-fx.controller";
        public const string GhostController = Generated + "/presence-ghost-fx.controller";

        /// <summary>
        /// What a spectre is drawn with on Android and iOS: VRChat's
        /// additive particle shader, reading one texel of
        /// <see cref="HueStrip"/>. On PC the avatar's own shader draws it.
        /// </summary>
        public const string SpectreMobileMaterial = Generated + "/spectre-mobile.mat";

        /// <summary>Every signature colour in a row, for a shader that has no colour of its own to set.</summary>
        public const string HueStrip = Generated + "/spectre-hues.asset";

        public const string MobileSpectreShader = "VRChat/Mobile/Particles/Additive";

        public const int HueStripTexels = 64;

        /// <summary>Where along the strip no turn of the hue is: the middle of its first texel.</summary>
        public const float HueStripStart = 0.5f / HueStripTexels;

        /// <summary>How much of the mobile spectre's colour is added to what is behind it.</summary>
        private const float MobileStrength = 0.32f;

        /// <summary>The layer that stops the avatar being drawn; checks look for it.</summary>
        public const string UnseenLayer = "Unseen";

        /// <summary>Seconds an avatar stays undrawn after it loads, on every client.</summary>
        public const float LoadingSeconds = 0.1f;

        public const string UnseenLabel = "Invisible";
        public const string UnseenIcon = "unseen";
        public const string UnseenFeature = "unseen";
        public const string PresenceFeatureId = "presence";

        /// <summary>How a game master starts out.</summary>
        public static PresenceOptions Plain() => new PresenceOptions();

        /// <summary>How a spectator starts out: a ghost, and never drawn where its animations do not run.</summary>
        public static PresenceOptions Ghost() => new PresenceOptions { Shown = Reach.Backstage, Heard = Reach.Backstage, RestHidden = true };

        /// <summary>Checks the generated prefabs and animators as far as the editor can; see <see cref="PresenceCheck"/>.</summary>
        [MenuItem("Tools/Wirenook/Adventure Tools/Verify Presence")]
        public static string Verify()
        {
            var report = PresenceCheck.Run();
            Debug.Log("[adventure-tools] " + report);
            return report;
        }

        [MenuItem("Tools/Wirenook/Adventure Tools/Rebuild Presence")]
        public static void Rebuild()
        {
            Circle.Prepare();
            Materials();
            Unseen();
            PresenceFeature(PresencePrefab, PresenceController, Generated + "/presence-reticle.asset", Plain());
            PresenceFeature(GhostPrefab, GhostController, Generated + "/presence-ghost-reticle.asset", Ghost());
            AssetDatabase.SaveAssets();
            Debug.Log("[adventure-tools] Rebuilt the invisible switch and presence.");
        }

        /// <summary>A parameter that lives in the animator alone, on every client separately.</summary>
        internal static ParameterConfig Own(string name)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name, syncType = ParameterSyncType.NotSynced, localOnly = true,
                defaultValue = 0f, hasExplicitDefaultValue = true,
            };
        }

        private static void Materials()
        {
            var mobileShader = Shader.Find(MobileSpectreShader);
            if (mobileShader == null) throw new System.InvalidOperationException("The VRChat SDK's shader " + MobileSpectreShader + " is missing.");

            // One row of the signature colour at every turn of its hue. The
            // mobile material reads a single texel of it, whichever its
            // offset names, for every point of the avatar: its tiling is
            // nought.
            var strip = AssetFolders.LoadOrCreate(HueStrip, () => new Texture2D(HueStripTexels, 2, TextureFormat.RGBA32, false));
            if (strip.width != HueStripTexels || strip.height != 2) strip.Reinitialize(HueStripTexels, 2, TextureFormat.RGBA32, false);
            strip.name = "spectre-hues";
            strip.wrapModeU = TextureWrapMode.Repeat;
            strip.wrapModeV = TextureWrapMode.Clamp;
            strip.filterMode = FilterMode.Bilinear;
            for (var x = 0; x < HueStripTexels; x++)
            {
                var colour = WirenookSignature.Colour(x / (float)HueStripTexels);
                colour.a = MobileStrength;
                strip.SetPixel(x, 0, colour);
                strip.SetPixel(x, 1, colour);
            }

            strip.Apply(false, false);
            EditorUtility.SetDirty(strip);

            var mobile = AssetFolders.LoadOrCreate(SpectreMobileMaterial, () => new Material(mobileShader));
            mobile.shader = mobileShader;
            mobile.mainTexture = strip;
            mobile.mainTextureScale = Vector2.zero;
            mobile.mainTextureOffset = new Vector2(HueStripStart, 0.5f);
            EditorUtility.SetDirty(mobile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The invisible switch. One layer, which every avatar carries:
        /// nothing is drawn on a client where the wearer has switched
        /// themself invisible (their own excepted, so they still see their
        /// hands) or where a feature that decides viewer by viewer says so
        /// (<see cref="WirenookSignals.Hidden"/>).
        ///
        /// It starts undrawn and stays so for a moment
        /// (<see cref="LoadingSeconds"/>) whatever the parameters say, so
        /// that an avatar which loads hidden from someone is never shown to
        /// them on the way: what says it is hidden there is itself worked
        /// out by an animator, a frame or two after loading.
        /// </summary>
        private static void Unseen()
        {
            FeaturePrefab.Edit(UnseenPrefab, root =>
            {
                var standIn = FeaturePrefab.AvatarRenderers(root);
                var path = FeaturePrefab.PathTo(root, standIn.transform);

                var fx = ControllerBuilder.Rebuild(UnseenController);
                fx.Bool(WirenookSignals.Unseen).Float(WirenookSignals.Hidden);
                fx.Declare(Presence.NotDrawnHere);
                AnimationClip Body(bool drawn) => fx.Clip(drawn ? "drawn" : "not drawn",
                    clip => clip.Constant(path, typeof(SkinnedMeshRenderer), "m_Enabled", drawn ? 1f : 0f));

                var layer = fx.Layer(UnseenLayer);
                var loading = layer.State("Loading", fx.Clip("not drawn yet",
                    clip => clip.Constant(path, typeof(SkinnedMeshRenderer), "m_Enabled", 0f, LoadingSeconds)));
                var gone = layer.State("Not drawn", Body(false));
                var here = layer.State("Drawn", Body(true));
                loading.To(gone).AfterTime(1f);
                gone.To(here).For(Presence.DrawnHere);
                here.To(gone).For(Presence.NotDrawnHere);
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(WirenookSignals.Unseen), Own(WirenookSignals.Hidden));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, UnseenLabel, UnseenIcon, WirenookSignals.Unseen);
                FeaturePrefab.Describe(root, UnseenFeature, "Invisible switch", UnseenIcon, tag: "");
            });
        }

        private static void PresenceFeature(string prefabPath, string controllerPath, string reticlePath, PresenceOptions options)
        {
            var mobile = AssetDatabase.LoadAssetAtPath<Material>(SpectreMobileMaterial);
            FeaturePrefab.Edit(prefabPath, root =>
            {
                var presence = Presence.Configure(root, options, reticlePath, mobile);
                var fx = ControllerBuilder.Rebuild(controllerPath);
                presence.AddLayers(fx);
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, presence.Parameters().ToArray());
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Presence", "presence");
                presence.AddMenu(root);
                FeaturePrefab.Describe(root, PresenceFeatureId, "Presence", "presence", "presence", 6, joinAvatarMesh: true);
            });
        }
    }
}
