using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// One tool of a game master's kit: something that is built into the
    /// kit's prefab, mesh, animator and menu together with the others. The
    /// kit calls each tool's four steps in turn, every tool's first step
    /// before any tool's second, because what comes later needs all of what
    /// came before: the stage has to know everything it will set down
    /// before it is built, and its layers have to come before the tools'.
    ///
    /// A game master made for one module adds tools of its own to the list
    /// (<see cref="GameMasterOptions.Tools"/>) and gets the stage, the
    /// pointer and the two meshes to build them with.
    /// </summary>
    public interface IStageTool
    {
        /// <summary>Names what the tool sets down in the world, if anything.</summary>
        void Things(StagePlan plan);

        /// <summary>Makes the tool's objects and components, holds its bones on the stage and adds its shapes to the meshes.</summary>
        void Build(GameMaster kit);

        /// <summary>Adds the tool's layers, after the stage's and the pointer's.</summary>
        void Layers(GameMaster kit);

        /// <summary>Declares the tool's parameters and puts it in the menu.</summary>
        void Menu(GameMaster kit);
    }

    /// <summary>One thing a tool asks its kit's stage for.</summary>
    public sealed class PlannedThing
    {
        public string name;

        /// <summary>Set down where the wearer stands, at one press, with no aiming.</summary>
        public bool atFeet;

        /// <summary>What the thing's value (<see cref="StageThing.Value"/>) rests at, 0 to 1.</summary>
        public float value;

        /// <summary>Part of a course that is laid out before it is shown: see <see cref="Stage.Live"/>.</summary>
        public bool rehearsed;
    }

    /// <summary>What a kit's stage will set down, gathered before the stage is built.</summary>
    public sealed class StagePlan
    {
        private readonly List<PlannedThing> _things = new List<PlannedThing>();

        public IReadOnlyList<PlannedThing> Things => _things;

        /// <summary>Asks for a thing of this name.</summary>
        /// <param name="atFeet">Set down where the wearer stands, with no aiming.</param>
        /// <param name="value">What its value rests at: a size on a dial, say. Every thing has one, and all of them together sync eight bits.</param>
        /// <param name="rehearsed">Seen by the people backstage alone, as a spectre, until the course goes live.</param>
        public void Thing(string name, bool atFeet = false, float value = 0f, bool rehearsed = false)
        {
            if (_things.Any(thing => thing.name == name)) throw new ArgumentException("Two things are called \"" + name + "\".");
            _things.Add(new PlannedThing { name = name, atFeet = atFeet, value = value, rehearsed = rehearsed });
        }
    }

    /// <summary>How a kit is built.</summary>
    public sealed class GameMasterOptions
    {
        /// <summary>Prefix for every parameter of the kit, unique on an avatar.</summary>
        public string Prefix = "GM";

        /// <summary>What the kit's folder in the menu is called.</summary>
        public string MenuLabel = "Game Master";

        /// <summary>The feature's id (WirenookFeature.id): a kit for one module has its own.</summary>
        public string FeatureId = GameMasterBuild.FeatureId;

        /// <summary>What the feature is called in lists of what an avatar carries.</summary>
        public string FeatureLabel = "Game master's kit";

        /// <summary>
        /// Ids of features this kit stands in for (WirenookReplaces): a
        /// kit for one module, on an avatar made from one that carries
        /// the plain kit, names the plain kit here. Both must then keep
        /// the same <see cref="Prefix"/>, because the parameters of the
        /// one left out stay declared.
        /// </summary>
        public string[] Replaces = new string[0];

        /// <summary>The wearer's eye height in metres: a roll only they see is shown a little below it.</summary>
        public float EyeHeight = 1.55f;

        /// <summary>The top of the avatar's head in metres: a roll everyone sees is shown above it.</summary>
        public float Height = 1.75f;

        /// <summary>The tools, in menu order. Left null, the whole kit (<see cref="GameMasterBuild.Kit"/>).</summary>
        public List<IStageTool> Tools;

        /// <summary>
        /// Prefabs to carry inside the kit's menu folder as they are: each
        /// has a menu entry of its own, which becomes one of the folder's.
        /// True for one that Android and iOS builds leave out.
        /// </summary>
        public List<(string prefab, bool pcOnly)> Carried = new List<(string, bool)>();

        public PointerOptions Pointer;
        public StageOptions Stage;
    }

    /// <summary>
    /// A kit while it is being built: what the tools share.
    /// </summary>
    public sealed class GameMaster
    {
        /// <summary>The object every platform's mesh is drawn by, directly under the root.</summary>
        public const string SolidName = "Stage Mesh";

        /// <summary>The object PC's extra mesh is drawn by: the shapes that need a shader of this package's own.</summary>
        public const string EffectsName = "Stage Effects";

        /// <summary>The audio source for cues heard from the wearer.</summary>
        public const string SoundPath = WirenookSounds.SourceName;

        public GameObject Root { get; internal set; }
        public GameMasterOptions Options { get; internal set; }
        public Pointer Pointer { get; internal set; }
        public Stage Stage { get; internal set; }
        public ControllerBuilder Fx { get; internal set; }

        /// <summary>
        /// The mesh every platform draws, in one material: colours are
        /// swatches of the wirenook palette, and <c>glow-cyan</c> and
        /// <c>cyan</c> follow the wearer's signature colour.
        /// </summary>
        public ShapeMesh Solid { get; internal set; }

        /// <summary>The PC-only mesh. Ask <see cref="Effect"/> for a slot before adding shapes to it.</summary>
        public ShapeMesh Effects { get; internal set; }

        public List<ParameterConfig> Parameters { get; } = new List<ParameterConfig>();

        private readonly List<Material> _effects = new List<Material>();

        internal IReadOnlyList<Material> EffectMaterials => _effects;

        /// <summary>The slot of the PC-only mesh that is drawn with a material, added if it is not there.</summary>
        public int Effect(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var slot = _effects.IndexOf(material);
            if (slot >= 0) return slot;
            _effects.Add(material);
            return _effects.Count - 1;
        }

        public string Prefix => Options.Prefix;

        /// <summary>Animation path of something under the kit's root.</summary>
        public string Path(Transform target) => FeaturePrefab.PathTo(Root, target);

        /// <summary>A child of the kit's root, made if it is not there.</summary>
        public GameObject Child(string name) => FeaturePrefab.Child(Root, name);

        /// <summary>The menu folder's own child for a tool, made a submenu.</summary>
        public GameObject Folder(string label, string icon)
        {
            var holder = Child("Menu " + label);
            FeaturePrefab.MenuFolder(holder, label, icon);
            // The menu lists entries in the order their objects sit in.
            holder.transform.SetAsLastSibling();
            return holder;
        }

        /// <summary>
        /// The people behind the scenes, where the avatar also carries the
        /// backstage circle (presence brings it), and the wearer. On an
        /// avatar without the circle, the wearer alone.
        /// </summary>
        public static Audience Backstage =>
            Audience.WhenTrue(Wirenook.SelectiveAnimation.Core.CircleNames.Seen(PresenceModel.BackstageCircle)).Or(Audience.Wearer);

        /// <summary>Keeps the stage's bounds whole while a clip shows something; see <see cref="Stage.KeepDrawn"/>.</summary>
        public void KeepDrawn(ClipBuilder clip) => Stage.KeepDrawn(clip);
    }
}
