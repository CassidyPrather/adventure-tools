using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.SelectiveAnimation.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Checks the invisible switch and presence as far as the editor can.
    /// Who is backstage and who is chosen only a VRChat client can work
    /// out, so this plays the client's part: it gives the generated
    /// animators what each kind of viewer's client would hold (it is the
    /// wearer's, its owner carries the circle, its owner has been marked)
    /// and holds what they then do, for every pair of settings, against
    /// <see cref="PresenceModel"/>. It also checks the prefabs are wired as
    /// the design says.
    ///
    /// What it cannot check: that a client stops drawing a renderer that
    /// is disabled this way, that a voice goes with the head bone, and the
    /// circle and the pointer themselves (net.wirenook.selective-animation
    /// has a probe for those).
    /// </summary>
    public static class PresenceCheck
    {
        private const float Frame = 1f / 72f;

        /// <returns>A report ending in "ok" or listing what failed.</returns>
        public static string Run()
        {
            var problems = new List<string>();
            var text = new StringBuilder();

            Switch(problems);
            foreach (var (prefab, controller, options) in new[]
                     {
                         (PresenceBuild.PresencePrefab, PresenceBuild.PresenceController, PresenceBuild.Plain()),
                         (PresenceBuild.GhostPrefab, PresenceBuild.GhostController, PresenceBuild.Ghost()),
                     })
            {
                Wiring(prefab, options, problems, text);
                Loading(prefab, controller, options, problems);
                Settings(prefab, controller, options, problems, text);
            }

            text.Append(problems.Count == 0 ? "ok" : "FAILED\n  " + string.Join("\n  ", problems));
            return text.ToString();
        }

        // -------------------------------------------------------------------- the animators, played

        /// <summary>
        /// A prefab with its generated controller on an animator of its
        /// own, stepped by hand. Its stand-in for every renderer is given a
        /// renderer to animate, so that what the build would spread to the
        /// whole avatar can be read off one object.
        /// </summary>
        private sealed class Rig : IDisposable
        {
            private readonly GameObject _instance;
            private readonly Animator _animator;
            private readonly Material _resting;
            private readonly SkinnedMeshRenderer _materials;

            public SkinnedMeshRenderer Body { get; }

            public Rig(string prefabPath, string controllerPath)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                if (prefab == null || controller == null) throw new InvalidOperationException("missing " + prefabPath + " or " + controllerPath);
                _instance = UnityEngine.Object.Instantiate(prefab);
                _instance.hideFlags = HideFlags.HideAndDontSave;

                var standIn = _instance.GetComponentInChildren<WirenookAvatarRenderers>(true);
                if (standIn == null) throw new InvalidOperationException(prefabPath + " has no stand-in for the avatar's renderers");
                _resting = new Material(Shader.Find("Unlit/Color")) { name = "resting", hideFlags = HideFlags.HideAndDontSave };
                Body = standIn.gameObject.AddComponent<SkinnedMeshRenderer>();
                Body.sharedMaterials = new[] { _resting };
                var materials = _instance.GetComponentInChildren<WirenookAvatarMaterials>(true);
                if (materials != null)
                {
                    _materials = materials.gameObject.AddComponent<SkinnedMeshRenderer>();
                    _materials.sharedMaterials = new[] { _resting };
                }

                _animator = _instance.AddComponent<Animator>();
                _animator.runtimeAnimatorController = controller;
                _animator.Rebind();
                _animator.Update(0f);
            }

            public bool Has(string name) => _animator.parameters.Any(parameter => parameter.name == name);
            public void Set(string name, bool value) => _animator.SetBool(name, value);
            public void Set(string name, float value) => _animator.SetFloat(name, value);
            public float Float(string name) => _animator.GetFloat(name);

            public void Run(float seconds)
            {
                for (var time = 0f; time < seconds; time += Frame) _animator.Update(Frame);
            }

            public bool Drawn => Body.enabled;

            /// <summary>A material property as the animator has left it on the stand-in for the avatar's materials.</summary>
            public float Property(string name)
            {
                var block = new MaterialPropertyBlock();
                _materials.GetPropertyBlock(block);
                return block.HasFloat(name) ? block.GetFloat(name) : 0f;
            }
            public bool Resting => Body.sharedMaterial == _resting;

            public T Component<T>() where T : Component => _instance.GetComponentsInChildren<T>(true).First();

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                UnityEngine.Object.DestroyImmediate(_resting);
            }
        }

        /// <summary>The invisible switch: gone for everyone else, there for the wearer, and obedient to presence.</summary>
        private static void Switch(List<string> problems)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresenceBuild.UnseenPrefab);
            if (prefab == null)
            {
                problems.Add("missing " + PresenceBuild.UnseenPrefab);
                return;
            }

            var declared = prefab.GetComponent<ModularAvatarParameters>()?.parameters ?? new List<ParameterConfig>();
            var synced = declared.Where(parameter => parameter.syncType != ParameterSyncType.NotSynced && !parameter.localOnly).ToList();
            if (synced.Count != 1 || synced[0].nameOrPrefix != WirenookSignals.Unseen || synced[0].syncType != ParameterSyncType.Bool)
                problems.Add("invisible switch: it should sync the one bit " + WirenookSignals.Unseen);
            if (prefab.GetComponentsInChildren<Component>(true).Any(component => component is Renderer || component is VRCContactReceiver || component is VRCContactSender))
                problems.Add("invisible switch: it carries a component that counts for rank");

            using (var rig = new Rig(PresenceBuild.UnseenPrefab, PresenceBuild.UnseenController))
            {
                if (rig.Drawn) problems.Add("invisible switch: drawn on the first frame; it has to start undrawn");

                void Expect(string when, bool drawn)
                {
                    rig.Run(0.2f);
                    if (rig.Drawn != drawn) problems.Add("invisible switch, " + when + ": " + (rig.Drawn ? "drawn" : "not drawn"));
                }

                rig.Set("IsLocal", false);
                Expect("someone else's client, switch off", true);
                rig.Set(WirenookSignals.Unseen, true);
                Expect("someone else's client, switch on", false);
                rig.Set("IsLocal", true);
                Expect("the wearer's own client, switch on", true);
                rig.Set(WirenookSignals.Unseen, false);
                rig.Set(WirenookSignals.Hidden, 1f);
                Expect("the wearer's own client, hidden by presence", false);
                rig.Set("IsLocal", false);
                Expect("someone else's client, hidden by presence", false);
                rig.Set(WirenookSignals.Hidden, 0f);
                Expect("someone else's client, hidden no longer", true);
            }
        }

        // -------------------------------------------------------------------- wiring

        private static void Wiring(string prefabPath, PresenceOptions options, List<string> problems, StringBuilder text)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                problems.Add("missing " + prefabPath);
                return;
            }

            var label = prefab.name + ": ";
            var declared = prefab.GetComponent<ModularAvatarParameters>()?.parameters ?? new List<ParameterConfig>();
            var synced = declared.Where(parameter => parameter.syncType != ParameterSyncType.NotSynced && !parameter.localOnly).ToList();
            var bits = synced.Sum(parameter => parameter.syncType == ParameterSyncType.Bool ? 1 : 8);
            text.AppendLine(label + bits + " synced bits (" + string.Join(", ", synced.Select(parameter => parameter.nameOrPrefix)) + ")");
            if (bits != 8) problems.Add(label + bits + " synced bits; four of presence's own and the selector's four were meant");

            void Default(string name, float wanted)
            {
                var parameter = declared.FirstOrDefault(candidate => candidate.nameOrPrefix == name);
                if (parameter.nameOrPrefix != name) problems.Add(label + name + " is not declared");
                else if (!Mathf.Approximately(parameter.defaultValue, wanted)) problems.Add(label + name + " loads as " + parameter.defaultValue + ", not " + wanted);
            }

            Default(PresenceModel.ShownFew, PresenceModel.Few(options.Shown) ? 1f : 0f);
            Default(PresenceModel.ShownChosen, PresenceModel.WithChosen(options.Shown) ? 1f : 0f);
            Default(PresenceModel.HeardFew, PresenceModel.Few(options.Heard) ? 1f : 0f);
            Default(PresenceModel.HeardChosen, PresenceModel.WithChosen(options.Heard) ? 1f : 0f);
            Default(PresenceModel.Shown, (int)options.Shown);
            Default(PresenceModel.Heard, (int)options.Heard);

            // The mover of the head.
            var mark = prefab.GetComponentsInChildren<WirenookBoneTarget>(true).FirstOrDefault();
            var mover = mark != null ? mark.GetComponent<VRCPositionConstraint>() : null;
            if (mark == null || mark.bone != HumanBodyBones.Head || mover == null)
            {
                problems.Add(label + "nothing moves the head bone");
            }
            else
            {
                if (mover.IsActive) problems.Add(label + "the head is moved as the avatar rests; a client that plays no animations would not hear the wearer");
                if (!mover.Locked || mover.FreezeToWorld || mover.Sources.Count != 1 || mover.Sources[0].SourceTransform == null)
                    problems.Add(label + "the head's constraint would not move it");
                else if (mover.Sources[0].SourceTransform.localPosition.y < 1_000_000f)
                    problems.Add(label + "the head is moved " + mover.Sources[0].SourceTransform.localPosition.y + " m, which a world can make a voice carry");
            }

            // The spectre, on both kinds of platform.
            var standIn = prefab.GetComponentInChildren<WirenookAvatarRenderers>(true);
            if (standIn == null) problems.Add(label + "no stand-in for the avatar's renderers");
            else
            {
                if (standIn.restHidden != options.RestHidden) problems.Add(label + "rests " + (standIn.restHidden ? "undrawn" : "drawn") + ", against its options");
                if (!standIn.swapsAreForMobile) problems.Add(label + "its material swap would reach PC builds, and cost them their merged mesh");
                var mobile = AssetDatabase.LoadAssetAtPath<Material>(PresenceBuild.SpectreMobileMaterial);
                if (mobile == null) problems.Add(label + "the spectre has no material for mobile");
                else
                {
                    if (!mobile.shader.name.StartsWith("VRChat/Mobile/", StringComparison.Ordinal))
                        problems.Add(label + "the mobile spectre is drawn with " + mobile.shader.name + ", which VRChat does not allow there");
                    if (mobile.mainTexture == null || mobile.mainTextureScale != Vector2.zero)
                        problems.Add(label + "the mobile spectre does not read one texel of the hue strip");
                }

                if (prefab.GetComponentInChildren<WirenookAvatarMaterials>(true) == null)
                    problems.Add(label + "no stand-in for the avatar's materials; a PC build would draw no spectre");
                if (Shader.Find("Wirenook/lilToon") == null || Shader.Find("Wirenook/lilToon").FindPropertyIndex(Presence.SpectralProperty) < 0)
                    problems.Add(label + "Wirenook/lilToon has no " + Presence.SpectralProperty + " to draw a spectre with");
            }

            if (prefab.GetComponentsInChildren<VRCContactReceiver>(true).All(watch => watch.parameter != CircleNames.Seen(options.Circle)))
                problems.Add(label + "it is not in the circle " + options.Circle);

            // Eight entries is all a menu holds.
            var entries = prefab.transform.Cast<Transform>().Count(child => child.GetComponent<ModularAvatarMenuItem>() != null);
            if (entries != 8) problems.Add(label + entries + " entries in its menu; it was laid out for eight");
        }

        // -------------------------------------------------------------------- behaviour

        /// <summary>
        /// The first frames, before anything has arrived: a stranger's
        /// client must never draw a wearer who loads hidden.
        /// </summary>
        private static void Loading(string prefabPath, string controllerPath, PresenceOptions options, List<string> problems)
        {
            var label = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)?.name + ", loading: ";
            using (var rig = new Rig(prefabPath, controllerPath))
            {
                var hidden = PresenceModel.Few(options.Shown);
                for (var frame = 0; frame < 20; frame++)
                {
                    var says = rig.Float(WirenookSignals.Hidden) > 0.5f;
                    if (says != hidden)
                    {
                        problems.Add(label + "on frame " + frame + " a stranger's client is told the wearer is " + (says ? "hidden" : "drawn"));
                        break;
                    }

                    rig.Run(Frame);
                }
            }
        }

        /// <summary>Every pair of settings, for every kind of viewer, against the model.</summary>
        private static void Settings(string prefabPath, string controllerPath, PresenceOptions options, List<string> problems, StringBuilder text)
        {
            var name = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)?.name;
            var names = new SelectorNames(options.Selector.Prefix);
            var seen = CircleNames.Seen(options.Circle);
            var spectre = AssetDatabase.LoadAssetAtPath<Material>(PresenceBuild.SpectreMobileMaterial);
            var reaches = new[] { Reach.Everyone, Reach.Backstage, Reach.Chosen };
            var viewers = new[] { Viewer.Self, Viewer.Member, Viewer.Marked, Viewer.Stranger, new Viewer(false, true, true) };
            var cases = 0;

            foreach (var viewer in viewers)
            {
                using (var rig = new Rig(prefabPath, controllerPath))
                {
                    var mover = rig.Component<VRCPositionConstraint>();
                    rig.Set("IsLocal", viewer.Wearer);
                    rig.Set(seen, viewer.Backstage);
                    if (viewer.Chosen)
                    {
                        // What that viewer's client holds while the wearer marks them: the pointer is on them, nothing is in the way.
                        rig.Set(names.Aim, true);
                        rig.Set(names.MeHit, true);
                        rig.Set(names.MeRatio, 0.2f);
                        rig.Set(names.SightRatio, 1f);
                        rig.Set(names.Mark, true);
                        rig.Run(1f);
                        rig.Set(names.Mark, false);
                        rig.Set(names.MeHit, false);
                        rig.Set(names.Aim, false);
                        rig.Run(0.5f);
                        if (rig.Float(names.Selected) < 0.5f) problems.Add(name + ": marking " + viewer + " did not select them");
                    }

                    foreach (var shown in reaches)
                    {
                        foreach (var heard in reaches)
                        {
                            rig.Set(PresenceModel.ShownFew, PresenceModel.Few(shown));
                            rig.Set(PresenceModel.ShownChosen, PresenceModel.WithChosen(shown));
                            rig.Set(PresenceModel.HeardFew, PresenceModel.Few(heard));
                            rig.Set(PresenceModel.HeardChosen, PresenceModel.WithChosen(heard));
                            rig.Set(WirenookSignature.Parameter, 0.25f);
                            rig.Run(0.25f);
                            cases++;

                            var when = name + ", shown to " + shown + ", heard by " + heard + ", for " + viewer + ": ";
                            var drawn = rig.Float(WirenookSignals.Hidden) < 0.5f;
                            if (drawn != PresenceModel.Drawn(shown, viewer)) problems.Add(when + (drawn ? "drawn" : "not drawn"));

                            var spectral = rig.Float(WirenookSignals.Spectral) > 0.5f;
                            if (spectral != PresenceModel.Spectral(shown, viewer)) problems.Add(when + (spectral ? "a spectre" : "no spectre"));
                            if (spectral == rig.Resting) problems.Add(when + "the body wears " + (rig.Resting ? "its own material" : "the spectre's") + ", against what it says");
                            if (spectral && rig.Body.sharedMaterial != spectre) problems.Add(when + "the spectre is drawn with " + rig.Body.sharedMaterial);
                            if (spectral != rig.Property(Presence.SpectralProperty) > 0.5f)
                                problems.Add(when + "the avatar's own shader is " + (spectral ? "not " : "") + "told to draw a spectre");

                            var audible = rig.Float(WirenookSignals.Muted) < 0.5f;
                            if (audible != PresenceModel.Audible(heard, viewer)) problems.Add(when + (audible ? "heard" : "not heard"));
                            if (mover.IsActive == audible) problems.Add(when + "the head is " + (mover.IsActive ? "moved" : "left") + ", against what it says");
                            if (viewer.Wearer && mover.IsActive) problems.Add(when + "the wearer's own head is moved");
                        }
                    }
                }
            }

            text.AppendLine(name + ": " + cases + " pairs of settings and viewers played against the model");
        }
    }
}
