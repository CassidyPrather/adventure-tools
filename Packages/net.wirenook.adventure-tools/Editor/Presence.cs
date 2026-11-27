using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.SelectiveAnimation.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>How a presence is built, and how its wearer starts out.</summary>
    public sealed class PresenceOptions
    {
        /// <summary>Who the wearer is shown to when the avatar loads.</summary>
        public Reach Shown = Reach.Everyone;

        /// <summary>Who hears the wearer when the avatar loads.</summary>
        public Reach Heard = Reach.Everyone;

        /// <summary>
        /// Leave the avatar undrawn as it rests (see
        /// <see cref="WirenookAvatarRenderers.restHidden"/>): for one that
        /// starts out hidden from most people, so that nobody sees it for a
        /// frame as it loads, or for good where its animations are blocked.
        /// </summary>
        public bool RestHidden;

        /// <summary>The circle whose members always see and hear each other.</summary>
        public string Circle = PresenceModel.BackstageCircle;

        /// <summary>How the people who are chosen get chosen.</summary>
        public SelectorOptions Selector = new SelectorOptions();
    }

    /// <summary>
    /// Presence: who sees the wearer and who hears them, set from the menu
    /// and decided on each viewer's own client. The rule is
    /// <see cref="PresenceModel"/>; this turns it into contacts, a raycast
    /// and animator layers.
    ///
    ///   Backstage  a circle (net.wirenook.selective-animation): everyone
    ///              who carries it always sees and hears everyone else who
    ///              does. Game masters and spectators carry it.
    ///   Chosen     the people the wearer has pointed at and marked, with
    ///              the same package's selector.
    ///
    /// Each of the two settings is sent as two synced bits, four in all,
    /// and every client works out the rest for itself, for its own copy of
    /// the avatar:
    ///
    ///   not drawn  the client writes <see cref="WirenookSignals.Hidden"/>,
    ///              and the avatar's invisible switch
    ///              (<see cref="PresenceBuild.UnseenPrefab"/>, which every
    ///              avatar carries) stops every renderer there.
    ///   spectral   where the wearer is hidden from most people, those
    ///              backstage and the wearer see a spectre, made of light
    ///              in the wearer's signature colour: on PC the avatar's
    ///              own shader draws it (_WirenookSpectral, in
    ///              net.wirenook.assets), and on Android and iOS every
    ///              material is exchanged for an additive one.
    ///   not heard  the client moves the wearer's humanoid head bone two
    ///              thousand kilometres up. VRChat puts a player's voice,
    ///              and their nameplate, at that bone. Only a rig whose
    ///              head bone carries nothing can do this
    ///              (<see cref="WirenookFreeHead"/>); on any other the
    ///              build leaves the mover out, and "heard by" does
    ///              nothing.
    ///
    /// None of it has run in VRChat. That a voice follows the head bone is
    /// unintended behaviour that VRChat may change.
    /// </summary>
    public sealed class Presence
    {
        /// <summary>
        /// Metres the head bone is moved, straight up. VRChat lets a world
        /// raise the distance a voice carries to a million metres at most;
        /// this is twice that.
        /// </summary>
        public const float OutOfEarshot = 2_000_000f;

        /// <summary>The float of Wirenook/lilToon that draws a surface as a spectre.</summary>
        public const string SpectralProperty = "_WirenookSpectral";

        public const string ShownLayer = "Presence Shown";
        public const string SpectralLayer = "Presence Spectral";
        public const string HeardLayer = "Presence Heard";

        /// <summary>The one on any avatar: it is not drawn on this client. The invisible switch and presence both count.</summary>
        public static Audience NotDrawnHere =>
            Audience.WhenTrue(WirenookSignals.Unseen).And(Audience.Others).Or(Audience.WhenAbove(WirenookSignals.Hidden));

        /// <summary>The wearer is drawn on this client, in one way or the other.</summary>
        public static Audience DrawnHere => NotDrawnHere.Not();

        /// <summary>The wearer is drawn as a spectre on this client.</summary>
        public static Audience SpectralHere => Audience.WhenAbove(WirenookSignals.Spectral);

        /// <summary>The wearer is heard on this client. True on an avatar without presence.</summary>
        public static Audience HeardHere => Audience.WhenBelow(WirenookSignals.Muted);

        public PresenceOptions Options { get; }
        public Selector Selector { get; }
        public Circle Backstage { get; }

        /// <summary>Whoever the "shown to" setting reaches, as the synced bits say it on each client.</summary>
        public Audience Drawn { get; }

        /// <summary>Whoever sees the spectre in place of the avatar.</summary>
        public Audience Spectral { get; }

        /// <summary>Whoever the "heard by" setting reaches.</summary>
        public Audience Heard { get; }

        private readonly GameObject _root;
        private readonly string _renderers;
        private readonly string _materials;
        private readonly string _voice;
        private readonly Material _spectre;

        private Presence(GameObject root, PresenceOptions options, string reticleMeshPath, Material spectreOnMobile)
        {
            _root = root;
            Options = options;
            _spectre = spectreOnMobile;

            Selector = Selector.Configure(root, options.Selector, reticleMeshPath);
            Backstage = Circle.Configure(root, options.Circle);

            // The wearer and the people backstage, always; the chosen where the second bit says so.
            Audience Reached(string few, string withChosen) =>
                Audience.WhenFalse(few).Or(Backstage.Members).Or(Audience.WhenTrue(withChosen).And(Selector.Selected));
            Drawn = Reached(PresenceModel.ShownFew, PresenceModel.ShownChosen);
            Heard = Reached(PresenceModel.HeardFew, PresenceModel.HeardChosen);
            Spectral = Audience.WhenTrue(PresenceModel.ShownFew).And(Backstage.Members);

            var standIn = FeaturePrefab.AvatarRenderers(root);
            standIn.swapsAreForMobile = true;
            standIn.restHidden = options.RestHidden;
            _renderers = FeaturePrefab.PathTo(root, standIn.transform);
            _materials = FeaturePrefab.AvatarMaterials(root);

            // The mover of the head: a constraint here whose target the build
            // points at the humanoid head bone, towards a place far overhead.
            var far = FeaturePrefab.Child(root, "Out Of Earshot");
            far.transform.localPosition = new Vector3(0f, OutOfEarshot, 0f);
            var voice = FeaturePrefab.Child(root, "Voice");
            FeaturePrefab.Ensure<WirenookBoneTarget>(voice).bone = HumanBodyBones.Head;
            var mover = FeaturePrefab.Ensure<VRCPositionConstraint>(voice);
            mover.ClearSources();
            mover.AddSource(new VRC.Dynamics.VRCConstraintSource { SourceTransform = far.transform, Weight = 1f });
            mover.PositionOffset = Vector3.zero;
            mover.PositionAtRest = Vector3.zero;
            mover.GlobalWeight = 1f;
            // Unlocked constraints re-measure their offset instead of moving the object.
            mover.Locked = true;
            mover.FreezeToWorld = false;
            mover.RebakeOffsetsWhenUnfrozen = false;
            // At rest the head is where it is: a client that plays no animations hears the wearer.
            mover.IsActive = false;
            _voice = FeaturePrefab.PathTo(root, voice.transform);
        }

        /// <summary>
        /// Puts presence on <paramref name="root"/>: the selector, the
        /// backstage circle, the stand-in the spectre is drawn through and
        /// the mover of the head. Call <see cref="Circle.Prepare"/> first.
        /// </summary>
        /// <param name="reticleMeshPath">Where the selector's reticle mesh is saved.</param>
        /// <param name="spectreOnMobile">The material a spectre is drawn with on Android and iOS: one with a VRChat mobile shader.</param>
        public static Presence Configure(GameObject root, PresenceOptions options, string reticleMeshPath, Material spectreOnMobile)
        {
            if (spectreOnMobile == null) throw new ArgumentNullException(nameof(spectreOnMobile), "Presence needs the mobile material of the spectre.");
            return new Presence(root, options, reticleMeshPath, spectreOnMobile);
        }

        /// <summary>
        /// Adds the selector's layers and presence's own to a controller
        /// that is merged from the feature's root.
        /// </summary>
        public void AddLayers(ControllerBuilder fx)
        {
            Selector.AddLayers(fx);
            fx.Bool(PresenceModel.ShownFew, PresenceModel.Few(Options.Shown))
                .Bool(PresenceModel.ShownChosen, PresenceModel.WithChosen(Options.Shown))
                .Bool(PresenceModel.HeardFew, PresenceModel.Few(Options.Heard))
                .Bool(PresenceModel.HeardChosen, PresenceModel.WithChosen(Options.Heard))
                .Int(PresenceModel.Shown, (int)Options.Shown)
                .Int(PresenceModel.Heard, (int)Options.Heard)
                .Int(PresenceModel.Preset)
                .Float(WirenookSignals.Hidden).Float(WirenookSignals.Spectral).Float(WirenookSignals.Muted)
                .Float(WirenookSignature.Parameter);
            fx.Declare(Drawn).Declare(Heard).Declare(Spectral);

            // Each layer starts in the state a stranger is in under the
            // settings the avatar loads with, so that a wearer who starts
            // out hidden is never shown to anyone for a frame.
            TwoStates(fx, ShownLayer, Drawn.Not(),
                "Not drawn", fx.Clip("presence not drawn", clip => clip.Parameter(WirenookSignals.Hidden, 1f)),
                "Drawn", fx.Clip("presence drawn", clip => clip.Parameter(WirenookSignals.Hidden, 0f)),
                startInFirst: PresenceModel.Few(Options.Shown));

            // The spectre. On PC the avatar's own shader draws it, at a
            // property every material of that shader has. On Android and
            // iOS every slot of every renderer takes the one additive
            // material instead (a PC build drops that curve); it has no
            // colour of its own to set, so the clip is a second long, is
            // read at the signature colour's amount, and moves the
            // material along a strip of every hue.
            var spectre = fx.Clip("presence spectre", clip =>
            {
                AnimationUtility.SetObjectReferenceCurve(clip.Clip,
                    EditorCurveBinding.PPtrCurve(_renderers, typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                    new[]
                    {
                        new ObjectReferenceKeyframe { time = 0f, value = _spectre },
                        new ObjectReferenceKeyframe { time = 1f, value = _spectre },
                    });
                var along = AnimationCurve.Linear(0f, PresenceBuild.HueStripStart, 1f, PresenceBuild.HueStripStart + 1f);
                clip.Material<SkinnedMeshRenderer>(_renderers, "_MainTex_ST.x", AnimationCurve.Constant(0f, 1f, 0f))
                    .Material<SkinnedMeshRenderer>(_renderers, "_MainTex_ST.y", AnimationCurve.Constant(0f, 1f, 0f))
                    .Material<SkinnedMeshRenderer>(_renderers, "_MainTex_ST.z", along)
                    .Material<SkinnedMeshRenderer>(_renderers, "_MainTex_ST.w", AnimationCurve.Constant(0f, 1f, 0.5f))
                    .Material<SkinnedMeshRenderer>(_materials, SpectralProperty, AnimationCurve.Constant(0f, 1f, 1f))
                    .Parameter(WirenookSignals.Spectral, AnimationCurve.Constant(0f, 1f, 1f));
            });
            var states = TwoStates(fx, SpectralLayer, Spectral,
                "Spectre", spectre,
                "Plain", fx.Clip("presence plain", clip => clip.Parameter(WirenookSignals.Spectral, 0f)),
                startInFirst: false);
            states.first.MotionTime(WirenookSignature.Parameter);

            TwoStates(fx, HeardLayer, Heard.Not(),
                "Not heard", fx.Clip("presence not heard", clip => clip
                    .Constant(_voice, typeof(VRCPositionConstraint), "IsActive", 1f).Parameter(WirenookSignals.Muted, 1f)),
                "Heard", fx.Clip("presence heard", clip => clip
                    .Constant(_voice, typeof(VRCPositionConstraint), "IsActive", 0f).Parameter(WirenookSignals.Muted, 0f)),
                startInFirst: false);

            Controls(fx);
        }

        private static (StateBuilder first, StateBuilder second) TwoStates(ControllerBuilder fx, string name, Audience forFirst,
            string firstName, Motion firstMotion, string secondName, Motion secondMotion, bool startInFirst)
        {
            var layer = fx.Layer(name);
            StateBuilder first, second;
            if (startInFirst)
            {
                first = layer.State(firstName, firstMotion);
                second = layer.State(secondName, secondMotion);
            }
            else
            {
                second = layer.State(secondName, secondMotion);
                first = layer.State(firstName, firstMotion);
            }

            second.To(first).For(forFirst);
            first.To(second).For(forFirst.Not());
            return (first, second);
        }

        /// <summary>
        /// The wearer's own side: the menu sets two numbers that are never
        /// synced, and their client turns each into the two synced bits.
        /// A preset sets both numbers at one press.
        /// </summary>
        private void Controls(ControllerBuilder fx)
        {
            void Setting(string layerName, string choice, string few, string withChosen, Reach start)
            {
                var layer = fx.Layer(layerName);
                var states = new Dictionary<Reach, StateBuilder>();
                // The one the avatar loads with first: it is the layer's default.
                foreach (var reach in new[] { start }.Concat(Reaches.Where(reach => reach != start)))
                {
                    var state = layer.State(reach.ToString());
                    state.Behaviour<VRCAvatarParameterDriver>(driver =>
                    {
                        driver.localOnly = true;
                        driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                        {
                            Set(few, PresenceModel.Few(reach) ? 1f : 0f),
                            Set(withChosen, PresenceModel.WithChosen(reach) ? 1f : 0f),
                        };
                    });
                    states[reach] = state;
                }

                foreach (var reach in Reaches)
                {
                    layer.Any(states[reach]).Equal(choice, (int)reach);

                    // Turning a menu switch off sets its number to nought,
                    // which is no choice at all. Put back the one in force,
                    // so the menu goes on showing it.
                    var restore = layer.State("Keep " + reach);
                    restore.Behaviour<VRCAvatarParameterDriver>(driver =>
                    {
                        driver.localOnly = true;
                        driver.parameters = new List<VRC_AvatarParameterDriver.Parameter> { Set(choice, (int)reach) };
                    });
                    states[reach].To(restore).Equal(choice, 0);
                    restore.To(states[reach]).Equal(choice, (int)reach);
                }
            }

            Setting("Presence Shown Setting", PresenceModel.Shown, PresenceModel.ShownFew, PresenceModel.ShownChosen, Options.Shown);
            Setting("Presence Heard Setting", PresenceModel.Heard, PresenceModel.HeardFew, PresenceModel.HeardChosen, Options.Heard);

            var presets = fx.Layer("Presence Presets");
            var idle = presets.State("Idle");
            for (var index = 0; index < PresenceModel.Presets.Count; index++)
            {
                var preset = PresenceModel.Presets[index];
                var pressed = presets.State(preset.Label);
                pressed.Behaviour<VRCAvatarParameterDriver>(driver =>
                {
                    driver.localOnly = true;
                    driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                    {
                        Set(PresenceModel.Shown, (int)preset.Shown),
                        Set(PresenceModel.Heard, (int)preset.Heard),
                    };
                });
                idle.To(pressed).Equal(PresenceModel.Preset, index + 1);
                pressed.To(idle).NotEqual(PresenceModel.Preset, index + 1);
            }
        }

        private static readonly Reach[] Reaches = { Reach.Everyone, Reach.Backstage, Reach.Chosen };

        private static VRC_AvatarParameterDriver.Parameter Set(string name, float value) =>
            new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = name, value = value };

        /// <summary>
        /// The feature's expression parameters, the selector's and the
        /// circle's included: four synced bits of presence's own, the menu's
        /// three numbers, which only the wearer's client holds, and what
        /// each client works out for itself.
        /// </summary>
        public IEnumerable<ParameterConfig> Parameters()
        {
            yield return FeaturePrefab.SyncedBool(PresenceModel.ShownFew, PresenceModel.Few(Options.Shown));
            yield return FeaturePrefab.SyncedBool(PresenceModel.ShownChosen, PresenceModel.WithChosen(Options.Shown));
            yield return FeaturePrefab.SyncedBool(PresenceModel.HeardFew, PresenceModel.Few(Options.Heard));
            yield return FeaturePrefab.SyncedBool(PresenceModel.HeardChosen, PresenceModel.WithChosen(Options.Heard));
            yield return Mine(PresenceModel.Shown, (int)Options.Shown);
            yield return Mine(PresenceModel.Heard, (int)Options.Heard);
            yield return Mine(PresenceModel.Preset, 0);
            foreach (var name in new[] { WirenookSignals.Hidden, WirenookSignals.Spectral, WirenookSignals.Muted })
                yield return PresenceBuild.Own(name);
            foreach (var parameter in Selector.Parameters()) yield return parameter;
            foreach (var parameter in Backstage.Parameters()) yield return parameter;
        }

        /// <summary>A number the menu sets on the wearer's client and nobody else hears of.</summary>
        private static ParameterConfig Mine(string name, int defaultValue)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name, syncType = ParameterSyncType.Int, localOnly = true, saved = false,
                defaultValue = defaultValue, hasExplicitDefaultValue = true,
            };
        }

        /// <summary>
        /// Puts presence on the menu as children of <paramref name="folder"/>,
        /// which the caller makes a submenu of: the presets asked for, one
        /// press each, then "Shown to", "Heard by" and "Choose". Eight
        /// entries with all five presets, which is as many as a menu holds.
        /// </summary>
        public void AddMenu(GameObject folder, IEnumerable<string> presets = null)
        {
            var wanted = presets != null ? new HashSet<string>(presets) : null;
            for (var index = 0; index < PresenceModel.Presets.Count; index++)
            {
                var preset = PresenceModel.Presets[index];
                var holder = folder.transform.Find("Menu " + preset.Label);
                if (wanted != null && !wanted.Contains(preset.Label))
                {
                    if (holder != null) UnityEngine.Object.DestroyImmediate(holder.gameObject);
                    continue;
                }

                var item = FeaturePrefab.Child(folder, "Menu " + preset.Label);
                FeaturePrefab.MenuButton(item, preset.Label, PresetIcons[preset.Label], PresenceModel.Preset, index + 1);
                Local(item);
            }

            void Choice(string label, string icon, string parameter)
            {
                var submenu = FeaturePrefab.Child(folder, "Menu " + label);
                FeaturePrefab.MenuFolder(submenu, label, icon);
                foreach (var reach in Reaches)
                {
                    var item = FeaturePrefab.Child(submenu, "Menu " + reach);
                    FeaturePrefab.MenuToggle(item, ReachLabels[reach], ReachIcons[reach], parameter, (int)reach);
                    Local(item);
                }
            }

            Choice("Shown to", "seen", PresenceModel.Shown);
            Choice("Heard by", "sound", PresenceModel.Heard);

            var choose = FeaturePrefab.Child(folder, "Menu Choose");
            FeaturePrefab.MenuFolder(choose, "Choose", "target");
            Selector.AddMenu(choose);
        }

        private static void Local(GameObject holder)
        {
            var item = holder.GetComponent<ModularAvatarMenuItem>();
            item.isSynced = false;
            item.isSaved = false;
        }

        private static readonly Dictionary<string, string> PresetIcons = new Dictionary<string, string>
        {
            { "Everyone", "seen" }, { "Ghost", "presence" }, { "Narrate", "megaphone" }, { "Whisper", "sound" }, { "Appear", "target" },
        };

        private static readonly Dictionary<Reach, string> ReachLabels = new Dictionary<Reach, string>
        {
            { Reach.Everyone, "Everyone" }, { Reach.Backstage, "Backstage only" }, { Reach.Chosen, "Chosen people" },
        };

        private static readonly Dictionary<Reach, string> ReachIcons = new Dictionary<Reach, string>
        {
            { Reach.Everyone, "seen" }, { Reach.Backstage, "presence" }, { Reach.Chosen, "target" },
        };
    }
}
