using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wirenook.Assets.Editor;

namespace Wirenook.AdventureTools.Editor
{
    /// <summary>
    /// A skinned mesh made in code out of flat-coloured shapes: the things
    /// a game master sets down, shows and points with. Nothing in it is
    /// worth opening a modelling program for, and all of it has to be one
    /// mesh: a thing left standing in the world is drawn apart from the
    /// avatar's own mesh, and one mesh with a bone for each thing is one
    /// mesh and one material slot however many things there are.
    ///
    /// Shapes are added in the space of a bone, in the colour of a swatch,
    /// and are carried by that bone alone. A run of shapes can be named as
    /// a blendshape: at full weight it is gathered into one point and draws
    /// nothing. That is how a numeral loses a stroke without a bone for
    /// every stroke.
    ///
    /// Faces are drawn from one side. A closed shape is wound to be seen
    /// from outside.
    /// </summary>
    public sealed class ShapeMesh
    {
        private readonly Func<string, Vector2> _uv;

        private readonly List<Transform> _bones = new List<Transform>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _owners = new List<int>();
        private readonly List<int> _shapes = new List<int>();
        private readonly List<Vector3> _gathered = new List<Vector3>();
        private readonly List<List<int>> _triangles = new List<List<int>> { new List<int>() };
        private readonly List<string> _shapeNames = new List<string>();

        private int _bone = -1;
        private int _submesh;
        private int _shape = -1;
        private Vector3 _gatherAt;
        private Vector2 _colour;
        private Matrix4x4 _place = Matrix4x4.identity;

        /// <param name="uv">Where on the material's texture a colour is, by name.</param>
        public ShapeMesh(Func<string, Vector2> uv)
        {
            _uv = uv ?? throw new ArgumentNullException(nameof(uv));
        }

        public int Triangles => _triangles.Sum(list => list.Count) / 3;
        public IReadOnlyList<string> Blendshapes => _shapeNames;
        public IReadOnlyList<Transform> Bones => _bones;

        /// <summary>Shapes added from here on are carried by this bone, and placed at its origin.</summary>
        public ShapeMesh On(Transform bone)
        {
            _bone = _bones.IndexOf(bone);
            if (_bone < 0)
            {
                _bones.Add(bone);
                _bone = _bones.Count - 1;
            }

            _place = Matrix4x4.identity;
            _shape = -1;
            return this;
        }

        /// <summary>Where in the bone's space the next shapes go.</summary>
        public ShapeMesh At(Vector3 position, Quaternion rotation, float scale = 1f)
        {
            _place = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
            return this;
        }

        public ShapeMesh At(Vector3 position) => At(position, Quaternion.identity);

        public ShapeMesh At(Matrix4x4 place)
        {
            _place = place;
            return this;
        }

        public Matrix4x4 Placed => _place;

        public ShapeMesh Colour(string swatch)
        {
            _colour = _uv(swatch);
            return this;
        }

        /// <summary>Which material slot the next shapes are drawn with, from 0.</summary>
        public ShapeMesh Slot(int index)
        {
            while (_triangles.Count <= index) _triangles.Add(new List<int>());
            _submesh = index;
            return this;
        }

        /// <summary>
        /// The shapes added from here on, until the next call, vanish
        /// together when the blendshape <paramref name="name"/> is at full
        /// weight. Several runs may share a name. Null ends the run.
        /// </summary>
        /// <param name="gatherAt">Where they are gathered to, in the space shapes are being placed in.</param>
        public ShapeMesh Vanishing(string name, Vector3 gatherAt = default)
        {
            if (name == null)
            {
                _shape = -1;
                return this;
            }

            _shape = _shapeNames.IndexOf(name);
            if (_shape < 0)
            {
                _shapeNames.Add(name);
                _shape = _shapeNames.Count - 1;
            }

            _gatherAt = _place.MultiplyPoint3x4(gatherAt);
            return this;
        }

        private int Vertex(Vector3 position, Vector3 normal)
        {
            if (_bone < 0) throw new InvalidOperationException("Say which bone carries the shape first (On).");
            _positions.Add(_place.MultiplyPoint3x4(position));
            _normals.Add(_place.MultiplyVector(normal).normalized);
            _uvs.Add(_colour);
            _owners.Add(_bone);
            _shapes.Add(_shape);
            _gathered.Add(_gatherAt);
            return _positions.Count - 1;
        }

        /// <summary>A flat face through the corners given in order round it, seen from the side <paramref name="facing"/> points to.</summary>
        public ShapeMesh Face(Vector3 facing, params Vector3[] corners)
        {
            if (corners.Length < 3) throw new ArgumentException("A face has three corners or more.", nameof(corners));
            var first = _positions.Count;
            foreach (var corner in corners) Vertex(corner, facing);
            var around = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            // Unity's front faces are wound clockwise: the cross product of
            // the first two edges points at whoever can see them.
            var flip = Vector3.Dot(around, facing) < 0f;
            if (_place.determinant < 0f) flip = !flip;
            for (var corner = 1; corner < corners.Length - 1; corner++)
            {
                _triangles[_submesh].Add(first);
                _triangles[_submesh].Add(first + (flip ? corner + 1 : corner));
                _triangles[_submesh].Add(first + (flip ? corner : corner + 1));
            }

            return this;
        }

        /// <summary>
        /// The four corners of a card, all resting on one point: a shader
        /// draws the card somewhere else and takes this point as where it
        /// belongs. Each corner's UV says which corner it is, (0, 0) the
        /// bottom left.
        /// </summary>
        public ShapeMesh Corners(Vector3 at)
        {
            var colour = _colour;
            var first = _positions.Count;
            foreach (var corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) })
            {
                _colour = corner;
                Vertex(at, Vector3.up);
            }

            _colour = colour;
            _triangles[_submesh].AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            return this;
        }

        /// <summary>A rectangle in the XY plane, seen from +Z.</summary>
        public ShapeMesh Plate(Vector2 centre, Vector2 size, float z = 0f)
        {
            var half = size * 0.5f;
            return Face(Vector3.forward,
                new Vector3(centre.x - half.x, centre.y - half.y, z), new Vector3(centre.x + half.x, centre.y - half.y, z),
                new Vector3(centre.x + half.x, centre.y + half.y, z), new Vector3(centre.x - half.x, centre.y + half.y, z));
        }

        /// <summary>A box with its sides along the axes.</summary>
        public ShapeMesh Box(Vector3 centre, Vector3 size)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + new Vector3(x * h.x, y * h.y, z * h.z);
            Face(Vector3.right, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
            Face(Vector3.left, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1));
            Face(Vector3.up, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1));
            Face(Vector3.down, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
            Face(Vector3.forward, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
            Face(Vector3.back, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1));
            return this;
        }

        /// <summary>
        /// A bar bent round the Y axis: part of a ring lying flat, with a
        /// rectangular section.
        /// </summary>
        /// <param name="fromDegrees">Where it starts, measured from +X towards +Z.</param>
        public ShapeMesh Bend(float inner, float outer, float bottom, float top, float fromDegrees, float sweepDegrees, int segments = 4)
        {
            Vector3 P(float radius, float y, int step)
            {
                var angle = (fromDegrees + sweepDegrees * step / segments) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }

            Vector3 Out(int step, float half)
            {
                var angle = (fromDegrees + sweepDegrees * (step + half) / segments) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            for (var step = 0; step < segments; step++)
            {
                Face(Vector3.up, P(inner, top, step), P(outer, top, step), P(outer, top, step + 1), P(inner, top, step + 1));
                Face(Vector3.down, P(inner, bottom, step), P(outer, bottom, step), P(outer, bottom, step + 1), P(inner, bottom, step + 1));
                Face(Out(step, 0.5f), P(outer, bottom, step), P(outer, top, step), P(outer, top, step + 1), P(outer, bottom, step + 1));
                Face(-Out(step, 0.5f), P(inner, bottom, step), P(inner, top, step), P(inner, top, step + 1), P(inner, bottom, step + 1));
            }

            if (Mathf.Abs(sweepDegrees) < 359.9f)
            {
                var along = Vector3.Cross(Vector3.up, Out(0, 0f)) * -Mathf.Sign(sweepDegrees);
                Face(-along, P(inner, bottom, 0), P(outer, bottom, 0), P(outer, top, 0), P(inner, top, 0));
                var alongEnd = Vector3.Cross(Vector3.up, Out(segments, 0f)) * -Mathf.Sign(sweepDegrees);
                Face(alongEnd, P(inner, bottom, segments), P(outer, bottom, segments), P(outer, top, segments), P(inner, top, segments));
            }

            return this;
        }

        /// <summary>A flat ring, or part of one, in the XZ plane, seen from above and below.</summary>
        public ShapeMesh Ring(float inner, float outer, float fromDegrees = 0f, float sweepDegrees = 360f, int segments = 24, float y = 0f)
        {
            for (var step = 0; step < segments; step++)
            {
                Vector3 P(float radius, int at)
                {
                    var angle = (fromDegrees + sweepDegrees * at / segments) * Mathf.Deg2Rad;
                    return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                }

                Face(Vector3.up, P(inner, step), P(outer, step), P(outer, step + 1), P(inner, step + 1));
                Face(Vector3.down, P(inner, step), P(outer, step), P(outer, step + 1), P(inner, step + 1));
            }

            return this;
        }

        /// <summary>A faceted ball, seen from outside.</summary>
        public ShapeMesh Ball(Vector3 centre, float radius, int rings = 4, int segments = 8)
        {
            Vector3 P(int ring, int segment)
            {
                var polar = Mathf.PI * ring / rings;
                var around = 2f * Mathf.PI * segment / segments;
                return centre + radius * new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(around));
            }

            for (var ring = 0; ring < rings; ring++)
            {
                for (var segment = 0; segment < segments; segment++)
                {
                    var middle = (P(ring, segment) + P(ring + 1, segment + 1)) * 0.5f - centre;
                    if (ring == 0) Face(middle, P(0, segment), P(1, segment), P(1, segment + 1));
                    else if (ring == rings - 1) Face(middle, P(ring, segment), P(ring + 1, segment), P(ring, segment + 1));
                    else Face(middle, P(ring, segment), P(ring + 1, segment), P(ring + 1, segment + 1), P(ring, segment + 1));
                }
            }

            return this;
        }

        /// <summary>
        /// A slice of a hollow ball: everything between two radii and
        /// between two longitudes, from pole to pole, closed on every side.
        /// With the whole turn and no inner radius, a solid ball.
        /// </summary>
        /// <param name="fromDegrees">The first longitude, measured from +X towards +Z.</param>
        public ShapeMesh Slice(float inner, float outer, float fromDegrees, float sweepDegrees, int rings = 8, int segments = 2)
        {
            var whole = Mathf.Abs(sweepDegrees) > 359.9f;
            Vector3 P(float radius, int ring, int segment)
            {
                var polar = Mathf.PI * ring / rings;
                var around = (fromDegrees + sweepDegrees * segment / segments) * Mathf.Deg2Rad;
                return radius * new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(around));
            }

            void Skin(float radius, bool outward)
            {
                for (var ring = 0; ring < rings; ring++)
                {
                    for (var segment = 0; segment < segments; segment++)
                    {
                        var middle = (P(radius, ring, segment) + P(radius, ring + 1, segment + 1)) * 0.5f * (outward ? 1f : -1f);
                        if (ring == 0) Face(middle, P(radius, 0, segment), P(radius, 1, segment), P(radius, 1, segment + 1));
                        else if (ring == rings - 1) Face(middle, P(radius, ring, segment), P(radius, ring + 1, segment), P(radius, ring, segment + 1));
                        else Face(middle, P(radius, ring, segment), P(radius, ring + 1, segment), P(radius, ring + 1, segment + 1), P(radius, ring, segment + 1));
                    }
                }
            }

            Skin(outer, true);
            if (inner > 0f) Skin(inner, false);
            if (whole) return this;

            // The two cut sides, each a fan of quads between the radii.
            foreach (var segment in new[] { 0, segments })
            {
                var around = (fromDegrees + sweepDegrees * segment / segments) * Mathf.Deg2Rad;
                var side = new Vector3(-Mathf.Sin(around), 0f, Mathf.Cos(around)) * (segment == 0 ? -1f : 1f) * Mathf.Sign(sweepDegrees);
                for (var ring = 0; ring < rings; ring++)
                {
                    if (inner > 0f)
                        Face(side, P(inner, ring, segment), P(outer, ring, segment), P(outer, ring + 1, segment), P(inner, ring + 1, segment));
                    else if (ring == 0)
                        Face(side, Vector3.zero, P(outer, 0, segment), P(outer, 1, segment));
                    else
                        Face(side, Vector3.zero, P(outer, ring, segment), P(outer, ring + 1, segment));
                }
            }

            return this;
        }

        /// <summary>
        /// Writes the mesh and puts it on a renderer named
        /// <paramref name="name"/> directly under <paramref name="root"/>.
        /// The bones must be where they rest when this is called: that pose
        /// is the one the mesh is bound in.
        /// </summary>
        /// <param name="materials">One for each slot used, in order.</param>
        public SkinnedMeshRenderer Build(GameObject root, string name, string meshPath, params Material[] materials)
        {
            if (_bones.Count == 0) throw new InvalidOperationException("No shapes were added.");
            if (materials.Length != _triangles.Count || materials.Any(material => material == null))
                throw new ArgumentException(name + " uses " + _triangles.Count + " material slot(s) and was given " +
                                            materials.Count(material => material != null) + " material(s).");

            var holder = FeaturePrefab.Child(root, name);
            var toMesh = holder.transform.worldToLocalMatrix;
            var count = _positions.Count;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var weights = new BoneWeight[count];
            var gathered = new Vector3[count];
            for (var vertex = 0; vertex < count; vertex++)
            {
                var place = toMesh * _bones[_owners[vertex]].localToWorldMatrix;
                positions[vertex] = place.MultiplyPoint3x4(_positions[vertex]);
                normals[vertex] = place.MultiplyVector(_normals[vertex]).normalized;
                gathered[vertex] = place.MultiplyPoint3x4(_gathered[vertex]);
                weights[vertex] = new BoneWeight { boneIndex0 = _owners[vertex], weight0 = 1f };
            }

            var binds = new Matrix4x4[_bones.Count];
            for (var bone = 0; bone < binds.Length; bone++)
                binds[bone] = _bones[bone].worldToLocalMatrix * holder.transform.localToWorldMatrix;

            var mesh = AssetFolders.LoadOrCreate(meshPath, () => new Mesh());
            mesh.Clear();
            mesh.ClearBlendShapes();
            // An asset's main object goes by its file's name.
            mesh.name = Path.GetFileNameWithoutExtension(meshPath);
            mesh.indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.SetUVs(0, _uvs);
            mesh.boneWeights = weights;
            mesh.bindposes = binds;
            mesh.subMeshCount = _triangles.Count;
            for (var slot = 0; slot < _triangles.Count; slot++) mesh.SetTriangles(_triangles[slot], slot);

            for (var shape = 0; shape < _shapeNames.Count; shape++)
            {
                var deltas = new Vector3[count];
                for (var vertex = 0; vertex < count; vertex++)
                    if (_shapes[vertex] == shape)
                        deltas[vertex] = gathered[vertex] - positions[vertex];
                mesh.AddBlendShapeFrame(_shapeNames[shape], 100f, deltas, null, null);
            }

            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var renderer = FeaturePrefab.Ensure<SkinnedMeshRenderer>(holder);
            renderer.sharedMesh = mesh;
            renderer.bones = _bones.ToArray();
            renderer.rootBone = holder.transform;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            for (var shape = 0; shape < _shapeNames.Count; shape++) renderer.SetBlendShapeWeight(shape, 0f);
            return renderer;
        }
    }
}
