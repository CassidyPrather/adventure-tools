using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.WorldSync.Core;
using Wirenook.WorldSync.Editor;
using Client = Wirenook.WorldSync.Editor.AnchorBusCheck.Client;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Runs the game master's kit in the editor, as far as the editor can.
    /// The generated animator is played on copies of the prefab, one for the
    /// wearer's client and one for a viewer's, with what only VRChat does
    /// stood in for as world-sync's <see cref="AnchorBusCheck"/> stands in
    /// for it: parameter drivers are carried out, the bus's contact readings
    /// are worked out, and the synced parameters are copied across ten
    /// times a second.
    ///
    /// What it proves is the state machines, the blend trees and the
    /// shapes: that a thing set down is on show for a viewer only once the
    /// viewer knows where it stands, that the dice boards draw the numbers
    /// they are given, that each kind of viewer is shown what the settings
    /// say, that the key switches on the one sender chosen. It cannot prove
    /// that VRChat's raycasts, contacts, constraints, stations and drivers
    /// behave as stood in for here; nothing in this package has run in a
    /// client.
    /// </summary>
    public static class GameMasterCheck
    {
        private const string Prefix = "GM";
        private const string StagePrefix = Prefix + "/Stage";
        private const string BusPrefix = StagePrefix + "/Bus";
        private const float Step = AnchorBusCheck.Step;
        private const float NetworkSeconds = 0.1f;

        /// <summary>Seconds within which a thing must be on show for everyone after the click that sets it down.</summary>
        public const float ShownWithinSeconds = 3.5f;

        private static string On(string thing) => StagePrefix + "/" + thing.Replace(" ", "");
        private static string Placed(int slot) => BusPrefix + "/" + slot + "/Set";

        /// <returns>A report ending in "ok" or listing what failed.</returns>
        public static string Run()
        {
            var problems = new List<string>();
            var text = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameMasterBuild.KitPrefab);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(GameMasterBuild.KitController);
            if (prefab == null || controller == null) return "FAILED\n  missing " + GameMasterBuild.KitPrefab + " or " + GameMasterBuild.KitController;

            var plan = new StagePlan();
            foreach (var tool in GameMasterBuild.Kit()) tool.Things(plan);
            var things = plan.Things.Select((thing, index) => (thing.name, slot: index + 1, thing.atFeet)).ToList();

            var bus = AnchorBusCheck.Run(GameMasterBuild.KitPrefab, GameMasterBuild.KitController, BusPrefix, things.Count,
                StagePrefix + "/Place", StagePrefix + "/Clear", StagePrefix + "/Air");
            text.AppendLine("The bus (" + things.Count + " slots): " + bus.TrimEnd().Split('\n').Last().Trim());
            if (!bus.TrimEnd().EndsWith("ok", StringComparison.Ordinal)) problems.Add("The bus fails its own check:\n" + bus);

            Structure(prefab, text, problems);
            SettingDown(prefab, controller, things, text, problems);
            Dice(prefab, controller, text, problems);
            Audiences(prefab, controller, things, text, problems);
            Key(prefab, controller, text, problems);
            return text + (problems.Count == 0 ? "ok" : "FAILED\n  " + string.Join("\n  ", problems.Distinct()));
        }

        private static int Bits(IEnumerable<ParameterConfig> parameters)
        {
            return parameters.Where(parameter => !parameter.localOnly && parameter.syncType != ParameterSyncType.NotSynced)
                .Sum(parameter => parameter.syncType == ParameterSyncType.Bool ? 1 : 8);
        }

        /// <summary>What the prefab carries, counted, and the marks that say what a mobile build and an invisible wearer leave alone.</summary>
        private static void Structure(GameObject prefab, StringBuilder text, List<string> problems)
        {
            var declared = prefab.GetComponentsInChildren<ModularAvatarParameters>(true).SelectMany(component => component.parameters).ToList();
            var contacts = prefab.GetComponentsInChildren<ContactBase>(true);
            var counted = contacts.Count(contact => !contact.localOnly);
            var raycasts = prefab.GetComponentsInChildren<VRCRaycast>(true).Length;
            var stations = prefab.GetComponentsInChildren<VRCStation>(true).Length;
            var sources = prefab.GetComponentsInChildren<AudioSource>(true).Length;
            var constraints = prefab.GetComponentsInChildren<VRCConstraintBase>(true).Length;
            var meshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            text.AppendLine("Carries: " + Bits(declared) + " synced bits, " + counted + " contacts that count (" + (contacts.Length - counted) +
                            " local-only), " + raycasts + " raycast, " + stations + " station, " + sources + " audio sources, " + constraints +
                            " constraints, " + meshes.Sum(mesh => mesh.sharedMesh.triangles.Length / 3) + " triangles in " +
                            meshes.Sum(mesh => mesh.sharedMaterials.Length) + " slots of its own meshes");

            // Whose the triangles are, by the bone that carries them.
            foreach (var mesh in meshes)
            {
                var triangles = mesh.sharedMesh.triangles;
                var weights = mesh.sharedMesh.boneWeights;
                var counts = new SortedDictionary<string, int>();
                for (var corner = 0; corner < triangles.Length; corner += 3)
                {
                    var path = AnimationUtility.CalculateTransformPath(mesh.bones[weights[triangles[corner]].boneIndex0], prefab.transform);
                    var tool = path.StartsWith("Pointer/", StringComparison.Ordinal) || path.StartsWith("Dot", StringComparison.Ordinal) ? "pointer"
                        : path.StartsWith("Stage/Zone", StringComparison.Ordinal) ? "zones"
                        : path.StartsWith("Stage/Pause", StringComparison.Ordinal) ? "pause"
                        : path.StartsWith("Stage/Door", StringComparison.Ordinal) || path.StartsWith("Stage/Exit", StringComparison.Ordinal) ? "portal"
                        : path.StartsWith("Dice/", StringComparison.Ordinal) ? "dice"
                        : path.StartsWith("Key/", StringComparison.Ordinal) ? "key"
                        : "stage";
                    counts[tool] = counts.TryGetValue(tool, out var known) ? known + 1 : 1;
                }

                text.AppendLine("  " + mesh.name + ": " + string.Join(", ", counts.Select(pair => pair.Key + " " + pair.Value)) + " triangles");
            }

            if (raycasts != 1) problems.Add("The kit has " + raycasts + " raycasts; the pointer and the stage share one.");
            if (stations != 1) problems.Add("The kit has " + stations + " stations; the portal is one.");
            if (counted != KeyModel.Traits.Count + 1)
                problems.Add("The kit has " + counted + " contacts that count for rank; the key's " + KeyModel.Traits.Count + " senders and one receiver are all there should be.");

            foreach (var name in new[] { GameMaster.SolidName, GameMaster.EffectsName })
            {
                var holder = prefab.transform.Find(name);
                if (holder == null || holder.GetComponent<WirenookApart>() == null)
                    problems.Add(name + " is not marked as standing apart from the wearer's figure: an invisible game master would take their zones with them.");
            }

            var effects = prefab.transform.Find(GameMaster.EffectsName);
            if (effects != null && effects.GetComponent<WirenookPcOnly>() == null)
                problems.Add(GameMaster.EffectsName + " is drawn with shaders of this package's own and is not marked PC-only.");
            foreach (var contact in contacts.Where(contact => !contact.localOnly))
            {
                if (contact.GetComponentInParent<WirenookPcOnly>(true) == null)
                    problems.Add(contact.name + " counts for rank and is not under anything marked PC-only; Android and iOS allow eight contacts at Medium.");
            }

            foreach (var mesh in meshes)
            {
                var bones = mesh.bones;
                if (bones.Any(bone => bone == null)) problems.Add(mesh.name + " has a bone that is gone.");
                // A mobile build takes PC-only objects away; a bone of the mesh every platform draws must not be among them.
                if (mesh.name == GameMaster.SolidName)
                    foreach (var bone in bones.Where(bone => bone != null && bone.GetComponentInParent<WirenookPcOnly>(true) != null))
                        problems.Add(GameMaster.SolidName + " is skinned to " + bone.name + ", which mobile builds leave out.");
                var vertices = mesh.sharedMesh.vertices;
                if (vertices.Any(vertex => float.IsNaN(vertex.x) || float.IsInfinity(vertex.x)))
                    problems.Add(mesh.name + " has vertices that are not numbers: it was bound to a bone of no size.");
            }
        }

        private static float Size(Client client, string path)
        {
            var found = client.Instance.transform.Find(path);
            if (found == null) throw new InvalidOperationException("The kit has no object at " + path + ".");
            return found.lossyScale.x / Mathf.Max(client.Instance.transform.lossyScale.x, 1e-6f);
        }

        /// <summary>
        /// The whole of setting something down, with a viewer who is there
        /// throughout, one who arrives late, a second setting-down of the
        /// same thing, a thing put down at the wearer's feet, the switch
        /// that takes a thing away and brings it back, and clearing.
        /// </summary>
        private static void SettingDown(GameObject prefab, AnimatorController controller,
            List<(string name, int slot, bool atFeet)> things, StringBuilder text, List<string> problems)
        {
            var grid = new WorldGrid(512f);
            var allowed = grid.Resolution * Mathf.Sqrt(3f) * 1.05f + 512f * 4e-6f;
            var synced = things.Select(thing => On(thing.name)).Append(StagePrefix + "/Aiming").ToList();
            string aim = StagePrefix + "/Aim", now = StagePrefix + "/Now", set = StagePrefix + "/Set", clear = StagePrefix + "/Clear";

            var zone = things.First(thing => !thing.atFeet);
            var feet = things.FirstOrDefault(thing => thing.atFeet);
            var zoneBone = "Stage/" + zone.name;

            using (var wearer = new Client(prefab, controller, BusPrefix, true, false))
            using (var viewer = new Client(prefab, controller, BusPrefix, false, false))
            {
                wearer.AlsoSynced.AddRange(synced);
                viewer.AlsoSynced.AddRange(synced);
                var viewers = new List<Client> { viewer };
                var sinceSync = 0f;

                void Frame()
                {
                    wearer.Advance(grid);
                    sinceSync += Step;
                    var arrives = sinceSync >= NetworkSeconds;
                    if (arrives) sinceSync = 0f;
                    foreach (var other in viewers)
                    {
                        if (arrives) other.Receive(wearer);
                        other.Ease(false);
                        other.Advance(grid);
                    }
                }

                void Run(float seconds)
                {
                    for (var elapsed = 0f; elapsed < seconds; elapsed += Step) Frame();
                }

                bool Shown(Client client, string thing) => Size(client, "Stage/" + thing) > 0.5f;
                bool Hidden(Client client, string thing) => Size(client, "Stage/" + thing) < 0.01f;

                // Sets a thing down at a spot and waits for it to be on show.
                // Throughout, a viewer who is shown the thing must be shown it
                // where it stood before, or where it stands now: never anywhere else.
                float SetDown((string name, int slot, bool atFeet) thing, Vector3 spot, Vector3? before, Action click, Action release)
                {
                    wearer.Cursor = spot;
                    click();
                    var elapsed = 0f;
                    var released = false;
                    while (elapsed < 8f && !(wearer.Animator.GetBool(On(thing.name)) && elapsed > 0.3f))
                    {
                        Frame();
                        elapsed += Step;
                        if (!released && elapsed >= 0.2f)
                        {
                            release();
                            released = true;
                        }

                        foreach (var other in viewers)
                        {
                            if (!Shown(other, thing.name)) continue;
                            var at = other.Pose(thing.slot).position;
                            var old = before.HasValue && (at - before.Value).magnitude <= allowed;
                            if (!old && (at - spot).magnitude > allowed)
                                problems.Add("A viewer was shown " + thing.name + " at " + at + " while it was being set down at " + spot + ".");
                        }
                    }

                    if (!wearer.Animator.GetBool(On(thing.name)))
                        problems.Add(thing.name + " was not on show " + elapsed.ToString("0.0") + " s after it was set down.");
                    return elapsed;
                }

                // Aiming: the wearer alone sees the thing and the mark, and everyone knows their hand is busy.
                wearer.Animator.SetInteger(aim, zone.slot);
                Run(0.5f);
                if (!wearer.Animator.GetBool(StagePrefix + "/Aiming") || !viewer.Animator.GetBool(StagePrefix + "/Aiming"))
                    problems.Add("Aiming did not raise the synced bit that says the pointing hand is a button.");
                if (!Shown(wearer, "Mark") || !Shown(wearer, zone.name)) problems.Add("The wearer does not see the mark, or the thing, while aiming.");
                if (!Hidden(viewer, "Mark") || !Hidden(viewer, zone.name)) problems.Add("A viewer sees the mark, or the thing, while the wearer is still aiming.");

                var first = new Vector3(3.2f, 1.5f, -7.8f);
                var gesture = "GestureRight";
                var took = SetDown(zone, first, null, () => wearer.Animator.SetInteger(gesture, 1), () => wearer.Animator.SetInteger(gesture, 0));
                Run(1f);
                text.AppendLine("Set down with a fist: on show for everyone " + took.ToString("0.0") + " s after the click");
                if (took > ShownWithinSeconds) problems.Add("A thing took " + took.ToString("0.0") + " s to be on show; " + ShownWithinSeconds + " is the most it should.");
                if (wearer.Animator.GetInteger(aim) != 0 || wearer.Animator.GetBool(StagePrefix + "/Aiming"))
                    problems.Add("Setting a thing down did not end the aiming.");
                if (!Shown(viewer, zone.name) || (viewer.Pose(zone.slot).position - first).magnitude > allowed)
                    problems.Add("A viewer who was there does not have " + zone.name + " where it was set down.");
                if (Shown(wearer, "Mark")) problems.Add("The mark is still out after the thing was set down.");

                // Again, somewhere else, with the menu's button: nobody may be shown it anywhere in between.
                var second = new Vector3(-120.4f, 36.7f, 88.9f);
                wearer.Animator.SetInteger(aim, zone.slot);
                Run(0.4f);
                took = SetDown(zone, second, first, () => wearer.Animator.SetBool(set, true), () => wearer.Animator.SetBool(set, false));
                Run(1f);
                text.AppendLine("Moved with the menu's button: on show again " + took.ToString("0.0") + " s after the click");
                if (!Shown(viewer, zone.name) || (viewer.Pose(zone.slot).position - second).magnitude > allowed)
                    problems.Add("A viewer does not have " + zone.name + " where it was moved to.");

                // At the wearer's feet, at one press.
                var third = new Vector3(12.75f, 0f, 44.1f);
                if (feet.name != null)
                {
                    took = SetDown(feet, third, null, () => wearer.Animator.SetInteger(now, feet.slot), () => wearer.Animator.SetInteger(now, 0));
                    Run(1f);
                    text.AppendLine(feet.name + ", at the wearer's feet: on show " + took.ToString("0.0") + " s after the press");
                    if (took > ShownWithinSeconds) problems.Add(feet.name + " took " + took.ToString("0.0") + " s to be on show.");
                    if (!Shown(viewer, feet.name) || (viewer.Pose(feet.slot).position - third).magnitude > allowed)
                        problems.Add("A viewer does not have " + feet.name + " where the wearer stood.");
                }

                // Someone who arrives now has it all within a round of the bus.
                using (var late = new Client(prefab, controller, BusPrefix, false, false))
                {
                    late.AlsoSynced.AddRange(synced);
                    viewers.Add(late);
                    var round = things.Count * (AnchorBus.LoadSeconds + AnchorBus.AirSeconds + AnchorBus.GapSeconds);
                    var arrived = -1f;
                    for (var elapsed = 0f; elapsed < 2f * round + 2f; elapsed += Step)
                    {
                        Frame();
                        if (Shown(late, zone.name) && (late.Pose(zone.slot).position - second).magnitude > allowed)
                            problems.Add("A late arrival was shown " + zone.name + " in the wrong place.");
                        var whole = Shown(late, zone.name) && (feet.name == null || Shown(late, feet.name));
                        if (arrived < 0f && whole) arrived = elapsed;
                    }

                    text.AppendLine("A late arrival has everything after " + arrived.ToString("0.0") + " s (one round of the bus is " + round.ToString("0.0") + " s)");
                    if (arrived < 0f || arrived > round + 1.5f) problems.Add("A late arrival did not have everything within a round of the bus.");
                    viewers.Remove(late);
                }

                // The switch: off takes it away, on brings it back where it stood.
                wearer.Animator.SetBool(On(zone.name), false);
                Run(0.6f);
                if (!Hidden(viewer, zone.name)) problems.Add("Switching " + zone.name + " off did not take it away for a viewer.");
                wearer.Animator.SetBool(On(zone.name), true);
                Run(0.6f);
                if (!Shown(viewer, zone.name) || (viewer.Pose(zone.slot).position - second).magnitude > allowed)
                    problems.Add("Switching " + zone.name + " back on did not bring it back where it stood.");

                // Clearing.
                wearer.Animator.SetBool(clear, true);
                Run(0.3f);
                wearer.Animator.SetBool(clear, false);
                Run(1f);
                var left = things.Count(thing => viewer.Animator.GetBool(On(thing.name)) || !Hidden(viewer, thing.name));
                text.AppendLine("Cleared: a viewer has " + left + " things left");
                if (left > 0) problems.Add("After a clear, a viewer still has " + left + " things on show.");
            }
        }

        private static float Weight(Client client, string blendshape)
        {
            var mesh = client.Instance.transform.Find(GameMaster.SolidName).GetComponent<SkinnedMeshRenderer>();
            var index = mesh.sharedMesh.GetBlendShapeIndex(blendshape);
            if (index < 0) throw new InvalidOperationException("The kit's mesh has no blendshape " + blendshape + ".");
            return mesh.GetBlendShapeWeight(index);
        }

        /// <summary>
        /// The boards draw the numbers they are given, for every pair of
        /// numbers two dice can show; each roll says what it should and
        /// nothing else; and a throw draws numbers the roll can come to.
        /// </summary>
        private static void Dice(GameObject prefab, AnimatorController controller, StringBuilder text, List<string> problems)
        {
            var grid = new WorldGrid(512f);
            var kind = Enumerable.Range(0, DiceModel.KindBits).Select(bit => DiceTool.Kind(Prefix, bit)).ToArray();
            using (var viewer = new Client(prefab, controller, BusPrefix, false, false))
            {
                void Say(Roll roll, int first, int second)
                {
                    viewer.Animator.SetFloat(DiceTool.First(Prefix), first);
                    viewer.Animator.SetFloat(DiceTool.Second(Prefix), second);
                    for (var bit = 0; bit < kind.Length; bit++) viewer.Animator.SetBool(kind[bit], DiceModel.Bit(roll, bit));
                    // A frame for the sums, a frame for the numerals to be read at them, and one to spare.
                    viewer.Run(4 * Step, grid);
                }

                void Numeral(string number, int value, string what)
                {
                    foreach (var tens in new[] { true, false })
                    foreach (var stroke in DiceModel.Strokes)
                    {
                        var gone = Weight(viewer, DiceTool.Stroke(number, tens, stroke)) > 50f;
                        if (gone == DiceModel.Drawn(value, tens, stroke))
                            problems.Add(what + ": the " + (tens ? "tens" : "ones") + " of " + number + " has stroke " + stroke + (gone ? " missing" : " drawn") + " for " + value + ".");
                    }
                }

                var pairs = 0;
                for (var first = 1; first <= DiceModel.MaxFace; first++)
                {
                    for (var second = 1; second <= DiceModel.MaxFace; second++)
                    {
                        Say(Roll.TwoD20, first, second);
                        var what = "2d20 of " + first + " and " + second;
                        Numeral("big", first + second, what);
                        Numeral("a", first, what);
                        Numeral("b", second, what);
                        pairs++;
                        // One wrong pair is enough to say so.
                        if (problems.Count > 20) break;
                    }
                }

                for (var face = 1; face <= DiceModel.MaxFace; face++)
                {
                    Say(Roll.D20, face, 0);
                    Numeral("big", face, "1d20 of " + face);
                    Numeral("a", 0, "1d20 of " + face);
                    Numeral("b", 0, "1d20 of " + face);
                }

                void Words(Roll roll, int first, int second, params string[] shown)
                {
                    Say(roll, first, second);
                    foreach (var word in new[] { "heads", "tails", "d6", "d20", "plus" })
                    {
                        var gone = Weight(viewer, DiceTool.Word(word)) > 50f;
                        if (gone == shown.Contains(word))
                            problems.Add(DiceModel.Label(roll) + " of " + first + ": \"" + word + "\" is " + (gone ? "missing" : "shown") + ".");
                    }
                }

                Words(Roll.Coin, 1, 0, "heads");
                Numeral("big", 0, "a coin");
                Words(Roll.Coin, 2, 0, "tails");
                Numeral("big", 0, "a coin");
                Words(Roll.D6, 4, 0, "d6");
                Words(Roll.TwoD6, 4, 2, "d6", "plus");
                Words(Roll.D20, 13, 0, "d20");
                Words(Roll.TwoD20, 13, 7, "d20", "plus");
                text.AppendLine("Dice: the boards draw all " + pairs + " pairs of two d20, every d20, and each roll's words");
            }

            using (var wearer = new Client(prefab, controller, BusPrefix, true, false))
            {
                var seen = new Dictionary<Roll, HashSet<int>>();
                foreach (var roll in DiceModel.Rolls)
                {
                    seen[roll] = new HashSet<int>();
                    for (var again = 0; again < 40; again++)
                    {
                        wearer.Animator.SetInteger(DiceTool.Throw(Prefix), (int)roll);
                        wearer.Run(0.2f, grid);
                        wearer.Animator.SetInteger(DiceTool.Throw(Prefix), 0);
                        wearer.Run(DiceTool.BetweenSeconds + 0.3f, grid);
                        var said = DiceModel.FromBits(wearer.Animator.GetBool(kind[0]), wearer.Animator.GetBool(kind[1]), wearer.Animator.GetBool(kind[2]));
                        var first = Mathf.RoundToInt(wearer.Animator.GetFloat(DiceTool.First(Prefix)));
                        var second = Mathf.RoundToInt(wearer.Animator.GetFloat(DiceTool.Second(Prefix)));
                        var faces = DiceModel.Faces(roll);
                        if (said != roll) problems.Add("A throw of " + DiceModel.Label(roll) + " said it was " + DiceModel.Label(said) + ".");
                        if (first < 1 || first > faces) problems.Add(DiceModel.Label(roll) + " came to " + first + ".");
                        if (DiceModel.Dice(roll) == 2 ? second < 1 || second > faces : second != 0)
                            problems.Add(DiceModel.Label(roll) + " gave its second die " + second + ".");
                        seen[roll].Add(first);
                    }

                    // Forty throws that only ever show one face would not be dice.
                    if (seen[roll].Count < 2) problems.Add(DiceModel.Label(roll) + " came up the same forty times running.");
                }

                wearer.Animator.SetInteger(DiceTool.Throw(Prefix), DiceTool.TakeDown);
                wearer.Run(0.3f, grid);
                wearer.Animator.SetInteger(DiceTool.Throw(Prefix), 0);
                wearer.Run(0.3f, grid);
                if (kind.Any(bit => wearer.Animator.GetBool(bit))) problems.Add("\"Take it down\" left a roll up.");
                text.AppendLine("Dice: forty throws of each roll came to numbers the roll can show (" +
                                string.Join(", ", seen.Select(pair => DiceModel.Label(pair.Key) + " showed " + pair.Value.Count + " faces")) + ")");
            }
        }

        /// <summary>Who is shown what: the dice boards, a zone that is for backstage only, the light on the finger.</summary>
        private static void Audiences(GameObject prefab, AnimatorController controller,
            List<(string name, int slot, bool atFeet)> things, StringBuilder text, List<string> problems)
        {
            var grid = new WorldGrid(512f);
            var member = CircleNames.Seen(PresenceModel.BackstageCircle);
            var kinds = new[] { (name: "the wearer", local: true, backstage: false), (name: "someone backstage", local: false, backstage: true), (name: "a stranger", local: false, backstage: false) };
            var zone = things.First(thing => !thing.atFeet);
            var cases = 0;
            foreach (var who in kinds)
            {
                using (var client = new Client(prefab, controller, BusPrefix, who.local, false))
                {
                    client.Animator.SetBool(member, who.backstage);
                    void Expect(string path, bool shown, string what)
                    {
                        cases++;
                        var size = Size(client, path);
                        if (shown ? size < 0.5f : size > 0.01f) problems.Add(what + ": " + who.name + (shown ? " is not shown it." : " is shown it."));
                    }

                    foreach (var secret in new[] { false, true })
                    foreach (var share in new[] { false, true })
                    {
                        client.Animator.SetBool(DiceTool.Secret(Prefix), secret);
                        client.Animator.SetBool(DiceTool.Share(Prefix), share);
                        client.Animator.SetBool(DiceTool.Kind(Prefix, 2), true);
                        client.Run(0.6f, grid);
                        var what = "A roll " + (secret ? "in secret" : "for everyone") + (share ? ", shared backstage" : "");
                        Expect("Dice/Public", !who.local && (!secret || (share && who.backstage)), what + ", the board over the head");
                        Expect("Dice/Private", who.local, what + ", the wearer's own board");
                        client.Animator.SetBool(DiceTool.Kind(Prefix, 2), false);
                        client.Run(0.4f, grid);
                    }

                    foreach (var backstageOnly in new[] { false, true })
                    {
                        client.Animator.SetBool(On(zone.name), true);
                        client.Animator.SetBool(Placed(zone.slot), true);
                        client.Animator.SetBool(ZoneTool.BackstageOnly(Prefix, 1), backstageOnly);
                        client.Run(0.6f, grid);
                        Expect("Stage/" + zone.name, !backstageOnly || who.local || who.backstage, "A zone " + (backstageOnly ? "for backstage only" : "for everyone"));
                        client.Animator.SetBool(On(zone.name), false);
                        client.Run(0.4f, grid);
                    }

                    // The light: by the switch, by the gesture where that is on, and not where the wearer is not drawn.
                    const string tip = "Pointer/Finger/Tip";
                    string light = Prefix + "/Finger", gestures = light + "/Gestures";
                    void Light(bool byMenu, bool byGesture, bool pointing, bool hidden, bool lit)
                    {
                        client.Animator.SetBool(light, byMenu);
                        client.Animator.SetBool(gestures, byGesture);
                        client.Animator.SetInteger("GestureRight", pointing ? 3 : 0);
                        client.Animator.SetFloat(WirenookSignals.Hidden, hidden ? 1f : 0f);
                        client.Run(0.6f, grid);
                        Expect(tip, lit, "The finger light" + (byMenu ? ", switched on" : "") + (byGesture ? ", gestures on" : "") +
                                         (pointing ? ", pointing" : "") + (hidden ? ", the wearer not drawn here" : ""));
                    }

                    Light(false, true, false, false, false);
                    Light(true, false, false, false, true);
                    Light(false, true, true, false, true);
                    Light(false, false, true, false, false);
                    Light(true, true, true, true, false);
                    Light(false, false, false, false, false);
                }
            }

            text.AppendLine("Audiences: " + cases + " cases of who is shown what, for the wearer, someone backstage and a stranger");
        }

        /// <summary>The key: the one sender chosen is on and no other, its family's stone shows, and a pip shows for each trait felt.</summary>
        private static void Key(GameObject prefab, AnimatorController controller, StringBuilder text, List<string> problems)
        {
            var grid = new WorldGrid(512f);
            var traits = KeyModel.Traits;
            using (var client = new Client(prefab, controller, BusPrefix, false, false))
            {
                var senders = client.Instance.GetComponentsInChildren<VRCContactSender>(true).Where(sender => !sender.localOnly).ToList();
                client.Animator.SetBool(KeyTool.Out(Prefix), true);
                for (var chosen = 0; chosen <= traits.Count; chosen++)
                {
                    client.Animator.SetInteger(KeyTool.Trait(Prefix), chosen);
                    client.Run(0.3f, grid);
                    var on = senders.Where(sender => sender.enabled && sender.gameObject.activeInHierarchy).Select(sender => sender.collisionTags.Single()).ToList();
                    var wanted = chosen == 0 ? new List<string>() : new List<string> { traits[chosen - 1].Tag };
                    if (!on.SequenceEqual(wanted))
                        problems.Add("With trait " + chosen + " chosen the key sends [" + string.Join(", ", on) + "], not [" + string.Join(", ", wanted) + "].");
                    foreach (var family in KeyModel.Families)
                    {
                        var gone = Weight(client, KeyTool.Stone(family)) > 50f;
                        if (gone == (chosen != 0 && traits[chosen - 1].Family == family))
                            problems.Add("With trait " + chosen + " chosen the stone of " + family + " is " + (gone ? "missing" : "shown") + ".");
                    }
                }

                client.Animator.SetBool(KeyTool.Out(Prefix), false);
                client.Run(0.3f, grid);
                if (senders.Any(sender => sender.gameObject.activeInHierarchy)) problems.Add("The key still sends after it is put away.");

                client.Animator.SetBool(KeyTool.Out(Prefix), true);
                for (var felt = 0; felt < traits.Count; felt++)
                {
                    for (var index = 0; index < traits.Count; index++)
                        client.Animator.SetFloat(KeyTool.Feels(Prefix, traits[index].Tag), index == felt || index == (felt + 7) % traits.Count ? 1f : 0f);
                    client.Run(4 * Step, grid);
                    for (var index = 0; index < traits.Count; index++)
                    {
                        var gone = Weight(client, KeyTool.Pip(traits[index].Tag)) > 50f;
                        var wanted = index == felt || index == (felt + 7) % traits.Count;
                        if (gone == wanted) problems.Add("Feeling " + traits[felt].Tag + ": the pip of " + traits[index].Tag + " is " + (gone ? "missing" : "shown") + ".");
                    }
                }

                text.AppendLine("Key: each of " + traits.Count + " traits switches on its own sender and no other, and each pip follows its trait");
            }
        }
    }
}
