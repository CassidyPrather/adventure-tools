using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Wirenook.Assets.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>One set of screen notices: a list of cards, the atlas they are drawn into, and the prefab that shows them.</summary>
    public sealed class NoticeSet
    {
        /// <summary>The list, in menu order (see Textures/notices.json for the format).</summary>
        public string Spec;

        /// <summary>The cards, drawn by tools/notice_atlas.py from the list.</summary>
        public string Atlas;

        public string Prefab;
        public string Controller;
        public string Material;

        /// <summary>The synced number that says which card is up; 0 for none. Two sets on one avatar need two.</summary>
        public string Parameter;

        /// <summary>What the set's folder in the menu is called, and its icon.</summary>
        public string Label;
        public string Icon;

        public string FeatureId;
        public string FeatureLabel;
    }

    /// <summary>
    /// Screen notices: a card that comes up in front of everyone who can see
    /// the wearer, chosen from the menu, with a chime. This builds a set of
    /// them; the package has two, the everyday ones and the ones for running
    /// a scene.
    /// </summary>
    public static class Notices
    {
        private const string Package = AdventureToolsBuild.Package;
        private const string Generated = Package + "/Generated";

        /// <summary>How long a notice stays up between fading in and out, in seconds.</summary>
        public const float StaySeconds = 3.85f;
        public const float FadeInSeconds = 0.25f;
        public const float FadeOutSeconds = 0.4f;

        /// <summary>The everyday notices: "Portal dropping", "Respawn" and so on.</summary>
        public static readonly NoticeSet Plain = new NoticeSet
        {
            Spec = Package + "/Textures/notices.json",
            Atlas = Package + "/Textures/notice-atlas.png",
            Prefab = AdventureToolsBuild.NoticePrefab,
            Controller = Generated + "/notice-fx.controller",
            Material = Generated + "/notice.mat",
            Parameter = "WN/Notice",
            Label = "Notice",
            Icon = "notice",
            FeatureId = "notice",
            FeatureLabel = "Screen notices",
        };

        /// <summary>The notices for running a scene: "Scene starts", "Roll for initiative" and so on.</summary>
        public static readonly NoticeSet Roleplay = new NoticeSet
        {
            Spec = Package + "/Textures/notices-roleplay.json",
            Atlas = Package + "/Textures/notice-roleplay-atlas.png",
            Prefab = GameMasterBuild.RoleplayNoticePrefab,
            Controller = Generated + "/notice-roleplay-fx.controller",
            Material = Generated + "/notice-roleplay.mat",
            Parameter = "WN/Say",
            Label = "Say",
            Icon = "notice",
            FeatureId = "notice-roleplay",
            FeatureLabel = "Roleplay notices",
        };

        public static void Build(NoticeSet set)
        {
            const string cardName = "Card";
            var parameter = set.Parameter;

            var spec = JObject.Parse(File.ReadAllText(set.Spec));
            var notices = spec["notices"].Select(token => (id: (string)token["id"], icon: (string)token["icon"])).ToList();

            var material = AssetFolders.LoadOrCreate(set.Material, () => new Material(Shader.Find("Wirenook/Screen Notice")));
            material.shader = Shader.Find("Wirenook/Screen Notice");
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(set.Atlas));
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

            FeaturePrefab.Edit(set.Prefab, root =>
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

                var fx = ControllerBuilder.Rebuild(set.Controller);
                fx.Int(parameter);
                var layer = fx.Layer(set.Label);
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
                    var appear = layer.State(id, Card(id + " appear", i, AnimationCurve.EaseInOut(0f, 0f, FadeInSeconds, 1f)));
                    var stay = layer.State(id + " (stay)", Card(id + " stay", i, AnimationCurve.Constant(0f, StaySeconds, 1f)));
                    var leave = layer.State(id + " (leave)", Card(id + " leave", i, AnimationCurve.EaseInOut(0f, 1f, FadeOutSeconds, 0f)));
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
                FeaturePrefab.MenuFolder(root, set.Label, set.Icon);

                // A list that has shrunk leaves menu entries behind it.
                foreach (var child in root.transform.Cast<Transform>().ToList())
                {
                    if (!child.name.StartsWith("Menu ", System.StringComparison.Ordinal)) continue;
                    if (!int.TryParse(child.name.Substring(5), out var number) || number > notices.Count)
                        Object.DestroyImmediate(child.gameObject);
                }

                for (var i = 0; i < notices.Count; i++)
                {
                    var item = FeaturePrefab.Child(root, "Menu " + (i + 1));
                    FeaturePrefab.MenuToggle(item, notices[i].id, notices[i].icon, parameter, i + 1);
                }

                // The card is drawn by a shader of this package's own, and mobile
                // builds may only use VRChat's.
                FeaturePrefab.Describe(root, set.FeatureId, set.FeatureLabel, "notice", "screen notice", 20,
                    summaryIcon: "megaphone", pcOnly: true);
            });
        }
    }
}
