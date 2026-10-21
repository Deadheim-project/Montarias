using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// A mount's body as it is posed now: its triangles, in one fixed space (the mount's own,
    /// unscaled, at the moment of the bake), and rays cast against them along that space's
    /// axes. Rays and not the nearest vertex: a big body is made of big triangles, with no
    /// vertex over the middle of one -- over the Moder's back the nearest vertex was the
    /// wing raised above it.
    /// </summary>
    internal sealed class MountBody
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();
        private Vector3 _min;
        private float _cell;
        private int _nx, _ny, _nz;
        // Triangles by the cells their shadow covers, looking along y (x,z cells) and along x (y,z cells).
        private List<int>[] _alongY, _alongX;

        public Bounds Bounds { get; private set; }
        public int TriangleCount => _triangles.Count / 3;

        /// <summary>Every visible skinned mesh under <paramref name="root"/> but the saddle and
        /// riders, baked in its current pose into the space <paramref name="toSpace"/> takes
        /// world points to. Null when there is none.</summary>
        public static MountBody Bake(Transform root, Matrix4x4 toSpace, StringBuilder report)
        {
            var body = new MountBody();
            var baked = new Mesh();
            try
            {
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (skin == null || !skin.enabled || skin.sharedMesh == null || MountGear.IsSaddle(skin.transform)) continue;
                    if (skin.GetComponentInParent<Player>() != null) continue;
                    skin.BakeMesh(baked, false);
                    var vertices = baked.vertices;
                    var triangles = baked.triangles;
                    if (vertices.Length == 0 || triangles.Length == 0) continue;
                    float scale = BakedScale(skin, vertices, report);
                    var toWorld = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one * scale);
                    var m = toSpace * toWorld;
                    int offset = body._vertices.Count;
                    for (int i = 0; i < vertices.Length; i++)
                        body._vertices.Add(m.MultiplyPoint3x4(vertices[i]));
                    for (int i = 0; i < triangles.Length; i++)
                        body._triangles.Add(offset + triangles[i]);
                }
            }
            finally
            {
                Object.Destroy(baked);
            }
            if (body._triangles.Count == 0) return null;
            body.Index();
            return body;
        }

        /// <summary>
        /// How much BakeMesh's vertices must be scaled, about the renderer and in its axes, to
        /// land where the body is. Whether the renderer's own scale is already in them depends
        /// on where in the hierarchy the scale sits (the boar at 2x came out 4x, the capybara
        /// skin came out right, Eikthyr at 1.5x came out 1.5x too big), so it is read from the
        /// bones, which are always where they are: the skin wraps them a little wider than they
        /// reach, around the same middle.
        /// </summary>
        private static float BakedScale(SkinnedMeshRenderer skin, Vector3[] vertices, StringBuilder report)
        {
            var lossy = skin.transform.lossyScale;
            float s = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
            if (s < 0.001f || Mathf.Abs(s - 1f) < 0.02f) return 1f;

            var toLocal = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one).inverse;
            var bones = skin.bones;
            bool any = false;
            var reach = new Bounds();
            if (bones != null)
            {
                foreach (var bone in bones)
                {
                    if (bone == null) continue;
                    var p = toLocal.MultiplyPoint3x4(bone.position);
                    if (!any) reach = new Bounds(p, Vector3.zero);
                    else reach.Encapsulate(p);
                    any = true;
                }
            }
            float bonesReach = any ? Largest(reach.size) : 0f;
            if (bonesReach < 0.01f) return 1f;

            var skinBounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) skinBounds.Encapsulate(vertices[i]);
            float best = 1f, bestScore = float.PositiveInfinity;
            foreach (float candidate in new[] { 1f, s, 1f / s })
            {
                float ratio = Mathf.Max(1e-4f, Largest(skinBounds.size) * candidate / bonesReach);
                float apart = (skinBounds.center * candidate - reach.center).magnitude / bonesReach;
                float score = Mathf.Abs(Mathf.Log(ratio / 1.15f)) + apart;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            report?.Append($"; {skin.name} scale {s:0.##}, baked x{best:0.##}");
            return best;
        }

        private static float Largest(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

        private void Index()
        {
            var bounds = new Bounds(_vertices[0], Vector3.zero);
            for (int i = 1; i < _vertices.Count; i++) bounds.Encapsulate(_vertices[i]);
            Bounds = bounds;
            _min = bounds.min;
            _cell = Mathf.Max(0.03f, Largest(bounds.size) / 96f);
            _nx = Cells(bounds.size.x);
            _ny = Cells(bounds.size.y);
            _nz = Cells(bounds.size.z);
            _alongY = new List<int>[_nx * _nz];
            _alongX = new List<int>[_ny * _nz];
            for (int t = 0; t < _triangles.Count; t += 3)
            {
                var a = _vertices[_triangles[t]];
                var b = _vertices[_triangles[t + 1]];
                var c = _vertices[_triangles[t + 2]];
                var lo = Vector3.Min(a, Vector3.Min(b, c));
                var hi = Vector3.Max(a, Vector3.Max(b, c));
                int x0 = Cell(lo.x, _min.x, _nx), x1 = Cell(hi.x, _min.x, _nx);
                int y0 = Cell(lo.y, _min.y, _ny), y1 = Cell(hi.y, _min.y, _ny);
                int z0 = Cell(lo.z, _min.z, _nz), z1 = Cell(hi.z, _min.z, _nz);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                        Add(_alongY, x * _nz + z, t);
                    for (int y = y0; y <= y1; y++)
                        Add(_alongX, y * _nz + z, t);
                }
            }
        }

        private int Cells(float size) => Mathf.Max(1, Mathf.CeilToInt(size / _cell) + 1);

        private int Cell(float value, float min, int count) =>
            Mathf.Clamp(Mathf.FloorToInt((value - min) / _cell), 0, count - 1);

        private static void Add(List<int>[] cells, int index, int triangle)
        {
            var list = cells[index];
            if (list == null) cells[index] = list = new List<int>();
            list.Add(triangle);
        }

        private bool OutsideGrid(float value, float min, int count)
        {
            float cell = (value - min) / _cell;
            return cell < 0f || cell >= count;
        }

        /// <summary>From <paramref name="from"/> straight up (<paramref name="direction"/> 1) or
        /// down (-1): how far the first surface is.</summary>
        public bool Vertical(Vector3 from, float direction, out float distance)
        {
            distance = float.PositiveInfinity;
            if (OutsideGrid(from.x, _min.x, _nx) || OutsideGrid(from.z, _min.z, _nz)) return false;
            var list = _alongY[Cell(from.x, _min.x, _nx) * _nz + Cell(from.z, _min.z, _nz)];
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!Hit(list[i], from.x, from.z, 0, 2, 1, out float y)) continue;
                float d = (y - from.y) * direction;
                if (d > 1e-4f && d < distance) distance = d;
            }
            return !float.IsPositiveInfinity(distance);
        }

        /// <summary>From <paramref name="from"/> straight to the right (<paramref name="direction"/>
        /// 1) or left (-1): how far the first surface is.</summary>
        public bool Sideways(Vector3 from, float direction, out float distance)
        {
            distance = float.PositiveInfinity;
            if (OutsideGrid(from.y, _min.y, _ny) || OutsideGrid(from.z, _min.z, _nz)) return false;
            var list = _alongX[Cell(from.y, _min.y, _ny) * _nz + Cell(from.z, _min.z, _nz)];
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!Hit(list[i], from.y, from.z, 1, 2, 0, out float x)) continue;
                float d = (x - from.x) * direction;
                if (d > 1e-4f && d < distance) distance = d;
            }
            return !float.IsPositiveInfinity(distance);
        }

        /// <summary>A point with the body both above and below it: inside the trunk, not under
        /// the belly or between the legs.</summary>
        public bool Inside(Vector3 point) => Vertical(point, 1f, out _) && Vertical(point, -1f, out _);

        /// <summary>Whether the line through (u,v) along axis <paramref name="iw"/> crosses the
        /// triangle, and where along that axis.</summary>
        private bool Hit(int t, float u, float v, int iu, int iv, int iw, out float w)
        {
            w = 0f;
            var a = _vertices[_triangles[t]];
            var b = _vertices[_triangles[t + 1]];
            var c = _vertices[_triangles[t + 2]];
            float ax = a[iu], ay = a[iv], bx = b[iu], by = b[iv], cx = c[iu], cy = c[iv];
            float det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Mathf.Abs(det) < 1e-10f) return false;
            float l1 = ((by - cy) * (u - cx) + (cx - bx) * (v - cy)) / det;
            float l2 = ((cy - ay) * (u - cx) + (ax - cx) * (v - cy)) / det;
            float l3 = 1f - l1 - l2;
            const float edge = -1e-5f;
            if (l1 < edge || l2 < edge || l3 < edge) return false;
            w = l1 * a[iw] + l2 * b[iw] + l3 * c[iw];
            return true;
        }
    }

    /// <summary>
    /// How far out the body reaches on each side below the seat, where the rider's legs go:
    /// sampled on a grid in the seat's space (metres, y up, z forward) when the saddle is
    /// fitted. Only the trunk counts -- a point under the belly or between the legs has no
    /// flank (0).
    /// </summary>
    internal sealed class MountFlanks
    {
        public const float Step = 0.05f;
        public const float Below = 1.6f;
        public const float Above = 0.1f;
        public const float Reach = 0.9f;

        private readonly int _ny, _nz;
        private readonly float[] _left, _right, _top;

        private MountFlanks(int ny, int nz)
        {
            _ny = ny;
            _nz = nz;
            _left = new float[ny * nz];
            _right = new float[ny * nz];
            _top = new float[nz];
            for (int i = 0; i < nz; i++) _top[i] = float.NaN;
        }

        /// <param name="seatToBody">Takes a point of the seat's space to the body's.</param>
        public static MountFlanks Measure(MountBody body, Matrix4x4 seatToBody)
        {
            int ny = Mathf.RoundToInt((Below + Above) / Step) + 1;
            int nz = Mathf.RoundToInt(2f * Reach / Step) + 1;
            var flanks = new MountFlanks(ny, nz);
            for (int iy = 0; iy < ny; iy++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    var p = seatToBody.MultiplyPoint3x4(new Vector3(0f, -Below + iy * Step, -Reach + iz * Step));
                    if (!body.Inside(p)) continue;
                    int i = iy * nz + iz;
                    if (body.Sideways(p, -1f, out float left)) flanks._left[i] = left;
                    if (body.Sideways(p, 1f, out float right)) flanks._right[i] = right;
                }
            }
            // The top of the body down the middle, from the highest point found inside it.
            for (int iz = 0; iz < nz; iz++)
            {
                for (int iy = ny - 1; iy >= 0; iy--)
                {
                    float y = -Below + iy * Step;
                    var p = seatToBody.MultiplyPoint3x4(new Vector3(0f, y, -Reach + iz * Step));
                    if (!body.Inside(p)) continue;
                    if (body.Vertical(p, 1f, out float up)) flanks._top[iz] = y + up;
                    break;
                }
            }
            return flanks;
        }

        /// <summary>The body's reach on one side (-1 left, 1 right) at a point of the seat's space.</summary>
        public float At(float side, float y, float z)
        {
            var grid = side < 0f ? _left : _right;
            float fy = Mathf.Clamp((y + Below) / Step, 0f, _ny - 1.001f);
            float fz = Mathf.Clamp((z + Reach) / Step, 0f, _nz - 1.001f);
            int y0 = (int)fy, z0 = (int)fz;
            float ty = fy - y0, tz = fz - z0;
            float a = Mathf.Lerp(grid[y0 * _nz + z0], grid[y0 * _nz + z0 + 1], tz);
            float b = Mathf.Lerp(grid[(y0 + 1) * _nz + z0], grid[(y0 + 1) * _nz + z0 + 1], tz);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>The top of the body down its middle at one point along it (seat space), or
        /// NaN where there is no body there.</summary>
        public float Top(float z)
        {
            float fz = Mathf.Clamp((z + Reach) / Step, 0f, _nz - 1.001f);
            int z0 = (int)fz;
            float a = _top[z0], b = _top[z0 + 1];
            if (float.IsNaN(a)) return b;
            if (float.IsNaN(b)) return a;
            return Mathf.Lerp(a, b, fz - z0);
        }

        /// <summary>The widest the body gets on one side between two heights, at one point along it.</summary>
        public float Widest(float side, float yMin, float yMax, float z)
        {
            float widest = 0f;
            for (float y = yMin; y <= yMax; y += Step)
                widest = Mathf.Max(widest, At(side, y, z));
            return widest;
        }
    }
}
