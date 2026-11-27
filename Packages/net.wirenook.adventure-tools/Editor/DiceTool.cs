using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Wirenook.AdventureTools.Core;
using Wirenook.Assets.Editor;
using Wirenook.SelectiveAnimation.Core;
using Wirenook.SelectiveAnimation.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// Dice: a coin, one or two six-sided dice, one or two twenty-sided
    /// ones, each a button in the menu. The wearer's client draws the
    /// numbers and syncs them, so everyone is shown the same roll.
    ///
    /// A roll is shown to everyone near the wearer, on a board over their
    /// head that reads from both sides, or in secret: to the wearer alone,
    /// on a small board in front of their chest, and to the people
    /// backstage as well if the wearer says so. The wearer always has the
    /// small board; nobody can read a board over their own head.
    ///
    /// The numerals are seven strokes each and every stroke is a blendshape
    /// that gathers it to a point, so three boards of six numerals cost two
    /// bones. Which strokes are drawn is one clip per number, read at the
    /// place the number says; nothing counts states. Two dice also show
    /// their sum, which each client adds up for itself in a blend tree.
    ///
    /// Synced: two numbers (sixteen bits), which roll it was (three bits),
    /// "in secret" and "backstage too" (a bit each). Twenty-one in all.
    /// </summary>
    public sealed class DiceTool : IStageTool
    {
        /// <summary>Seconds a roll stays up before the wearer's client takes it down.</summary>
        public const float UpSeconds = 20f;

        /// <summary>Seconds the board is down between two rolls, so that a second roll of the same kind reads as a new one.</summary>
        public const float BetweenSeconds = 0.3f;

        /// <summary>How tall the large numerals are on each board, in metres.</summary>
        public const float PublicSize = 0.2f;
        public const float PrivateSize = 0.055f;

        private const string PublicName = "Public";
        private const string PrivateName = "Private";

        public static string First(string prefix) => prefix + "/Dice/A";
        public static string Second(string prefix) => prefix + "/Dice/B";
        public static string Kind(string prefix, int bit) => prefix + "/Dice/Kind" + bit;
        public static string Secret(string prefix) => prefix + "/Dice/Secret";
        public static string Share(string prefix) => prefix + "/Dice/Share";
        public static string Throw(string prefix) => prefix + "/Dice/Throw";

        /// <summary>The number the menu's "take it down" button sets <see cref="Throw"/> to.</summary>
        public const int TakeDown = 6;

        private static string Drawn(string prefix, string which) => prefix + "/Dice/Draw" + which;
        private static string Share01(string prefix, string which) => prefix + "/Dice/" + which + "N";

        /// <summary>The blendshape that takes one stroke of one numeral away.</summary>
        public static string Stroke(string number, bool tens, char stroke) => "dice." + number + (tens ? ".t." : ".o.") + stroke;

        private static readonly string[] Words = { "heads", "tails", "d6", "d20", "plus" };

        /// <summary>The blendshape that takes a word or a sign away.</summary>
        public static string Word(string word) => "dice." + word;

        private Transform _public;
        private Transform _private;

        public void Things(StagePlan plan)
        {
        }

        public void Build(GameMaster kit)
        {
            var holder = kit.Child("Dice");
            _public = GameMasterBuild.Bone(holder, PublicName, new Vector3(0f, kit.Options.Height + PublicSize * 2.2f, 0f));
            _private = GameMasterBuild.Bone(holder, PrivateName, new Vector3(0f, kit.Options.EyeHeight - 0.3f, 0.4f));

            // A board is drawn facing -Z. Over the head it stands twice,
            // back to back, so it reads from in front and from behind; the
            // wearer's own leans back to face their eyes.
            Board(kit.Solid, _public, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 180f, 0f), Vector3.one * PublicSize));
            Board(kit.Solid, _public, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * PublicSize));
            Board(kit.Solid, _private, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(35f, 0f, 0f), Vector3.one * PrivateSize));
        }

        // One board, in units of the large numerals' height. The large
        // number is the roll, or the sum of two; under it, for two dice,
        // each die; under that, which dice they were.
        private const float BigY = 0.55f;
        private const float SmallY = -0.42f;
        private const float SmallSize = 0.42f;
        private const float LabelY = -0.98f;

        private static void Board(ShapeMesh mesh, Transform bone, Matrix4x4 place)
        {
            mesh.On(bone).Colour("glow-cyan");
            Numeral(mesh, place, "big", true, new Vector2(-0.33f, BigY), 1f);
            Numeral(mesh, place, "big", false, new Vector2(0.33f, BigY), 1f);
            Numeral(mesh, place, "a", true, new Vector2(-0.72f, SmallY), SmallSize);
            Numeral(mesh, place, "a", false, new Vector2(-0.44f, SmallY), SmallSize);
            Numeral(mesh, place, "b", true, new Vector2(0.44f, SmallY), SmallSize);
            Numeral(mesh, place, "b", false, new Vector2(0.72f, SmallY), SmallSize);

            mesh.At(place).Vanishing(Word("plus"), new Vector3(0f, SmallY, 0f));
            Page(mesh, new Vector2(0f, SmallY), new Vector2(0.2f, 0.05f));
            Page(mesh, new Vector2(0f, SmallY), new Vector2(0.05f, 0.2f));

            mesh.Colour("white");
            Lettering(mesh, place, "heads", "HEADS", new Vector2(0f, BigY), 0.44f);
            Lettering(mesh, place, "tails", "TAILS", new Vector2(0f, BigY), 0.44f);
            Lettering(mesh, place, "d6", "d6", new Vector2(0f, LabelY), 0.24f);
            Lettering(mesh, place, "d20", "d20", new Vector2(0f, LabelY), 0.24f);
            mesh.Vanishing(null);
        }

        /// <summary>A rectangle in the board's plane, seen from -Z, where +X is to a reader's right.</summary>
        private static void Page(ShapeMesh mesh, Vector2 centre, Vector2 size)
        {
            var half = size * 0.5f;
            mesh.Face(Vector3.back,
                new Vector3(centre.x - half.x, centre.y - half.y, 0f), new Vector3(centre.x + half.x, centre.y - half.y, 0f),
                new Vector3(centre.x + half.x, centre.y + half.y, 0f), new Vector3(centre.x - half.x, centre.y + half.y, 0f));
        }

        private static void Numeral(ShapeMesh mesh, Matrix4x4 place, string number, bool tens, Vector2 centre, float size)
        {
            const float width = 0.5f;
            const float thick = 0.13f;
            var across = new Vector2(width - thick * 1.25f, thick) * size;
            var down = new Vector2(thick, 0.5f - thick * 0.75f) * size;
            var side = (width - thick) * 0.5f * size;
            var strokes = new Dictionary<char, (Vector2 at, Vector2 size)>
            {
                { 'a', (new Vector2(0f, (0.5f - thick * 0.5f) * size), across) },
                { 'g', (Vector2.zero, across) },
                { 'd', (new Vector2(0f, -(0.5f - thick * 0.5f) * size), across) },
                { 'b', (new Vector2(side, 0.25f * size), down) },
                { 'c', (new Vector2(side, -0.25f * size), down) },
                { 'e', (new Vector2(-side, -0.25f * size), down) },
                { 'f', (new Vector2(-side, 0.25f * size), down) },
            };
            foreach (var stroke in DiceModel.Strokes)
            {
                var (at, extent) = strokes[stroke];
                mesh.At(place).Vanishing(Stroke(number, tens, stroke), new Vector3(centre.x + at.x, centre.y + at.y, 0f));
                Page(mesh, centre + at, extent);
            }
        }

        // Five rows of three, top row first.
        private static readonly Dictionary<char, string[]> Letters = new Dictionary<char, string[]>
        {
            { 'H', new[] { "101", "101", "111", "101", "101" } },
            { 'E', new[] { "111", "100", "110", "100", "111" } },
            { 'A', new[] { "010", "101", "111", "101", "101" } },
            { 'D', new[] { "110", "101", "101", "101", "110" } },
            { 'S', new[] { "011", "100", "010", "001", "110" } },
            { 'T', new[] { "111", "010", "010", "010", "010" } },
            { 'I', new[] { "111", "010", "010", "010", "111" } },
            { 'L', new[] { "100", "100", "100", "100", "111" } },
            { 'd', new[] { "001", "001", "011", "101", "011" } },
            { '2', new[] { "110", "001", "010", "100", "111" } },
            { '0', new[] { "111", "101", "101", "101", "111" } },
            { '6', new[] { "011", "100", "111", "101", "111" } },
        };

        private static void Lettering(ShapeMesh mesh, Matrix4x4 place, string word, string text, Vector2 centre, float height)
        {
            var pixel = height / 5f;
            var width = (text.Length * 4 - 1) * pixel;
            mesh.At(place).Vanishing(Word(word), new Vector3(centre.x, centre.y, 0f));
            for (var letter = 0; letter < text.Length; letter++)
            {
                var rows = Letters[text[letter]];
                for (var row = 0; row < 5; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        if (rows[row][column] != '1') continue;
                        var x = centre.x - width * 0.5f + (letter * 4 + column + 0.5f) * pixel;
                        var y = centre.y + height * 0.5f - (row + 0.5f) * pixel;
                        Page(mesh, new Vector2(x, y), new Vector2(pixel, pixel) * 0.94f);
                    }
                }
            }
        }

        /// <summary>
        /// A curve one second long that holds <paramref name="value"/> of
        /// each whole number from 0 to <paramref name="most"/> around the
        /// place that number is read at (the number over the most), and
        /// jumps between them.
        /// </summary>
        public static AnimationCurve Steps(int most, Func<int, float> value)
        {
            var keys = new List<Keyframe> { new Keyframe(0f, value(0)) };
            for (var number = 1; number <= most; number++)
            {
                if (Mathf.Approximately(value(number), value(number - 1))) continue;
                keys.Add(new Keyframe((number - 0.5f) / most, value(number)));
            }

            keys.Add(new Keyframe(1f, value(most)));
            var curve = new AnimationCurve(keys.ToArray());
            for (var key = 0; key < curve.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, key, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, key, AnimationUtility.TangentMode.Constant);
            }

            return curve;
        }

        public void Layers(GameMaster kit)
        {
            var fx = kit.Fx;
            var prefix = kit.Prefix;
            string a = First(prefix), b = Second(prefix), toss = Throw(prefix);
            string sum01 = Share01(prefix, "Sum"), a01 = Share01(prefix, "A"), b01 = Share01(prefix, "B");
            string drawA = Drawn(prefix, "A"), drawB = Drawn(prefix, "B");
            var kind = Enumerable.Range(0, DiceModel.KindBits).Select(bit => Kind(prefix, bit)).ToArray();

            // The numbers are whole on the network and floats here: blend
            // trees and motion times only take floats, and VRChat converts.
            fx.Float(a).Float(b).Float(sum01).Float(a01).Float(b01).Int(drawA).Int(drawB).Int(toss)
                .Bool(Secret(prefix)).Bool(Share(prefix)).Bool("IsLocal");
            foreach (var bit in kind) fx.Bool(bit);

            // Each number as a share of the most it can be, and the two
            // together as a share of the most they can come to: every child
            // of a direct blend tree adds its clip's value times its weight.
            AnimationClip Says(string parameter, float amount) => fx.Clip("dice " + parameter + " " + amount.ToString("0.####"),
                clip => clip.Parameter(parameter, amount));
            fx.Layer("Dice Sums").State("Sums", GameMasterBuild.Weighted(fx, "dice sums",
                (a, Says(sum01, 1f / DiceModel.MaxSum)), (b, Says(sum01, 1f / DiceModel.MaxSum)),
                (a, Says(a01, 1f / DiceModel.MaxFace)), (b, Says(b01, 1f / DiceModel.MaxFace))));

            var mesh = GameMaster.SolidName;
            void Numerals(string name, string number, string share, int most)
            {
                var clip = fx.Clip("dice " + number + " numerals", built =>
                {
                    foreach (var tens in new[] { true, false })
                    foreach (var stroke in DiceModel.Strokes)
                        built.Blendshape(mesh, Stroke(number, tens, stroke),
                            Steps(most, value => DiceModel.Drawn(value, tens, stroke) ? 0f : 100f));
                });
                fx.Layer(name).State("Numerals", clip).MotionTime(share);
            }

            Numerals("Dice Sum", "big", sum01, DiceModel.MaxSum);
            Numerals("Dice First", "a", a01, DiceModel.MaxFace);
            Numerals("Dice Second", "b", b01, DiceModel.MaxFace);

            // What else the board says, by which roll it was. This layer
            // comes after the numerals and overrules them where a roll has
            // no use for them: a coin has no number, one die no pair.
            var says = fx.Layer("Dice Roll");
            AnimationClip Board(string name, string word, string label, bool pair, bool number) => fx.Clip("dice board " + name, clip =>
            {
                foreach (var each in Words)
                    clip.Blendshape(mesh, Word(each), each == word || each == label || (each == "plus" && pair) ? 0f : 100f);
                foreach (var tens in new[] { true, false })
                foreach (var stroke in DiceModel.Strokes)
                {
                    if (!pair)
                    {
                        clip.Blendshape(mesh, Stroke("a", tens, stroke), 100f);
                        clip.Blendshape(mesh, Stroke("b", tens, stroke), 100f);
                    }

                    if (!number) clip.Blendshape(mesh, Stroke("big", tens, stroke), 100f);
                }
            });

            void Rolled(string name, Roll roll, AnimationClip clip, Action<TransitionBuilder> also = null)
            {
                // The first state added, "None", is where the layer starts.
                var state = says.State(name, clip);
                var transition = says.Any(state);
                for (var bit = 0; bit < kind.Length; bit++)
                {
                    if (DiceModel.Bit(roll, bit)) transition.IfTrue(kind[bit]);
                    else transition.IfFalse(kind[bit]);
                }

                also?.Invoke(transition);
            }

            Rolled("None", Roll.None, Board("none", null, null, false, false));
            Rolled("Heads", Roll.Coin, Board("heads", "heads", null, false, false), heads => heads.Less(a, 1.5f));
            Rolled("Tails", Roll.Coin, Board("tails", "tails", null, false, false), tails => tails.Greater(a, 1.5f));
            Rolled("One d6", Roll.D6, Board("d6", null, "d6", false, true));
            Rolled("Two d6", Roll.TwoD6, Board("2d6", null, "d6", true, true));
            Rolled("One d20", Roll.D20, Board("d20", null, "d20", false, true));
            Rolled("Two d20", Roll.TwoD20, Board("2d20", null, "d20", true, true));

            // Who is shown it. The wearer has their own board; the one over
            // their head is for everybody else, or for nobody but the people
            // backstage.
            var rolled = kind.Aggregate(Audience.Nobody, (any, bit) => any.Or(Audience.WhenTrue(bit)));
            var open = Audience.WhenFalse(Secret(prefix))
                .Or(Audience.WhenTrue(Share(prefix)).And(GameMaster.Backstage));
            Shown(kit, "Dice Board", _public, rolled.And(open).And(Audience.Others));
            Shown(kit, "Dice Own Board", _private, rolled.And(Audience.Wearer));

            ThrowLayer(kit, kind);
        }

        private static void Shown(GameMaster kit, string name, Transform board, Audience audience)
        {
            var path = kit.Path(board);
            var states = kit.Stage.Show(kit.Fx, name, new[] { path }, audience, (clip, phase) =>
            {
                if (phase != "appear") return;
                // It tumbles into place.
                clip.Curve(path, typeof(Transform), "localEulerAnglesRaw.y",
                    AnimationCurve.EaseInOut(0f, -360f, PopToggle.AppearSeconds, 0f));
            });
            WirenookSounds.PlayOnEnter(states.Appear, GameMaster.SoundPath, "dice");
        }

        /// <summary>
        /// The throw, on the wearer's client. The board comes down for a
        /// moment first, so that the same roll twice running is seen to be
        /// two rolls; then the numbers are drawn and the kind is said. A
        /// roll stays up for <see cref="UpSeconds"/>, or until the next.
        /// </summary>
        private static void ThrowLayer(GameMaster kit, string[] kind)
        {
            var fx = kit.Fx;
            var prefix = kit.Prefix;
            string a = First(prefix), b = Second(prefix), toss = Throw(prefix);
            string drawA = Drawn(prefix, "A"), drawB = Drawn(prefix, "B");

            VRC_AvatarParameterDriver.Parameter Set(string name, float value) =>
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = name, value = value };
            VRC_AvatarParameterDriver.Parameter Copy(string source, string name) =>
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Copy, source = source, name = name };
            VRC_AvatarParameterDriver.Parameter Draw(string name, int faces) =>
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Random, name = name, valueMin = 1f, valueMax = faces };
            void Drive(StateBuilder state, IEnumerable<VRC_AvatarParameterDriver.Parameter> changes) =>
                state.Behaviour<VRCAvatarParameterDriver>(driver =>
                {
                    driver.localOnly = true;
                    driver.parameters = changes.ToList();
                });
            IEnumerable<VRC_AvatarParameterDriver.Parameter> Say(Roll roll) =>
                Enumerable.Range(0, kind.Length).Select(bit => Set(kind[bit], DiceModel.Bit(roll, bit) ? 1f : 0f));

            var layer = fx.Layer("Dice Throw");
            var idle = layer.State("Idle");
            var up = layer.State("Up", fx.Clip("dice up", clip => clip.Hold(UpSeconds)));
            var down = layer.State("Down");
            Drive(down, Say(Roll.None));
            up.To(down).AfterTime(1f);
            up.To(down).Equal(toss, TakeDown);
            down.To(idle).Equal(toss, 0);

            var between = fx.Clip("dice between", clip => clip.Hold(BetweenSeconds));
            foreach (var roll in DiceModel.Rolls)
            {
                var clear = layer.State("Clear for " + DiceModel.Label(roll), between);
                Drive(clear, Say(Roll.None));
                var draw = layer.State("Draw " + DiceModel.Label(roll));
                // Drawn into whole numbers first: a number drawn straight
                // into a float would not be whole.
                Drive(draw, new[]
                {
                    Draw(drawA, DiceModel.Faces(roll)),
                    DiceModel.Dice(roll) > 1 ? Draw(drawB, DiceModel.Faces(roll)) : Set(drawB, 0f),
                    Copy(drawA, a), Copy(drawB, b),
                }.Concat(Say(roll)));

                idle.To(clear).IfTrue("IsLocal").Equal(toss, (int)roll);
                up.To(clear).Equal(toss, (int)roll);
                clear.To(draw).AfterTime(1f);
                // The button is let go of before the next press counts.
                draw.To(up).Equal(toss, 0);
            }
        }

        public void Menu(GameMaster kit)
        {
            var prefix = kit.Prefix;
            kit.Parameters.Add(FeaturePrefab.SyncedInt(First(prefix)));
            kit.Parameters.Add(FeaturePrefab.SyncedInt(Second(prefix)));
            for (var bit = 0; bit < DiceModel.KindBits; bit++) kit.Parameters.Add(FeaturePrefab.SyncedBool(Kind(prefix, bit)));
            kit.Parameters.Add(FeaturePrefab.SyncedBool(Secret(prefix), saved: true));
            kit.Parameters.Add(FeaturePrefab.SyncedBool(Share(prefix), saved: true));
            kit.Parameters.Add(GameMasterBuild.Mine(Throw(prefix), ParameterSyncType.Int));
            foreach (var name in new[] { Drawn(prefix, "A"), Drawn(prefix, "B"), Share01(prefix, "Sum"), Share01(prefix, "A"), Share01(prefix, "B") })
                kit.Parameters.Add(GameMasterBuild.Own(name));

            var folder = kit.Folder("Dice", "dice");
            foreach (var roll in DiceModel.Rolls)
            {
                var item = FeaturePrefab.Child(folder, "Menu " + DiceModel.Label(roll));
                FeaturePrefab.MenuButton(item, DiceModel.Label(roll), roll == Roll.Coin ? "coin" : "dice", Throw(prefix), (int)roll);
                GameMasterBuild.Local(item);
            }

            var secret = FeaturePrefab.Child(folder, "Menu Secret");
            FeaturePrefab.MenuToggle(secret, "Roll in secret", "unseen", Secret(prefix));
            secret.GetComponent<ModularAvatarMenuItem>().isSaved = true;
            var share = FeaturePrefab.Child(folder, "Menu Share");
            FeaturePrefab.MenuToggle(share, "Backstage sees secrets", "presence", Share(prefix));
            share.GetComponent<ModularAvatarMenuItem>().isSaved = true;
            var take = FeaturePrefab.Child(folder, "Menu Take Down");
            FeaturePrefab.MenuButton(take, "Take it down", "pick-up", Throw(prefix), TakeDown);
            GameMasterBuild.Local(take);
        }
    }
}
