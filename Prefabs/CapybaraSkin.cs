using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMontarias.Prefabs
{
    internal sealed class CapybaraSkin
    {
        public struct Bone
        {
            public string Name;
            public int Parent;
            public Vector3 RestT;
            public Quaternion RestQ;
            public Vector3 RestS;
            public Matrix4x4 Bindpose;
        }

        public struct Pose
        {
            public Vector3 T;
            public Quaternion Q;
            public Vector3 S;
        }

        public sealed class Clip
        {
            public string Name;
            public float Duration;
            public Pose[][] Frames;
        }

        public float UnityScale;
        public Bone[] Bones;
        public Mesh Mesh;
        public Clip[] Clips;
        public Vector3 SeatLocal;

        public static CapybaraSkin Load(byte[] data)
        {
            if (data == null || data.Length < 16)
                return null;
            if (data[0] != (byte)'C' || data[1] != (byte)'A' || data[2] != (byte)'P' || data[3] != (byte)'Y')
                return null;

            var reader = new BinaryReader(new MemoryStream(data));
            reader.ReadBytes(4);
            int version = reader.ReadInt32();
            if (version != 1 && version != 2)
                return null;
            bool wideBones = version >= 2;

            var skin = new CapybaraSkin();
            skin.UnityScale = reader.ReadSingle();
            int boneCount = reader.ReadInt32();
            skin.Bones = new Bone[boneCount];
            var bindposes = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                var bone = new Bone
                {
                    Name = ReadString(reader),
                    Parent = reader.ReadInt32(),
                    RestT = ReadVec3(reader),
                    RestQ = ReadQuat(reader),
                    RestS = ReadVec3(reader),
                    Bindpose = ReadMatrix(reader)
                };
                skin.Bones[i] = bone;
                bindposes[i] = bone.Bindpose;
            }

            int vertCount = reader.ReadInt32();
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var weights = new BoneWeight[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                vertices[i] = ReadVec3(reader);
                normals[i] = ReadVec3(reader);
                uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                weights[i] = new BoneWeight
                {
                    boneIndex0 = wideBones ? reader.ReadInt32() : reader.ReadByte(),
                    boneIndex1 = wideBones ? reader.ReadInt32() : reader.ReadByte(),
                    boneIndex2 = wideBones ? reader.ReadInt32() : reader.ReadByte(),
                    boneIndex3 = wideBones ? reader.ReadInt32() : reader.ReadByte(),
                    weight0 = reader.ReadSingle(),
                    weight1 = reader.ReadSingle(),
                    weight2 = reader.ReadSingle(),
                    weight3 = reader.ReadSingle()
                };
            }

            int triCount = reader.ReadInt32();
            var triangles = new int[triCount * 3];
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = reader.ReadInt32();

            int clipCount = reader.ReadInt32();
            skin.Clips = new Clip[clipCount];
            for (int c = 0; c < clipCount; c++)
            {
                var clip = new Clip
                {
                    Name = ReadString(reader),
                    Duration = reader.ReadSingle()
                };
                int frames = reader.ReadInt32();
                clip.Frames = new Pose[frames][];
                for (int f = 0; f < frames; f++)
                {
                    clip.Frames[f] = new Pose[boneCount];
                    for (int b = 0; b < boneCount; b++)
                    {
                        clip.Frames[f][b] = new Pose
                        {
                            T = ReadVec3(reader),
                            Q = ReadQuat(reader),
                            S = ReadVec3(reader)
                        };
                    }
                }
                skin.Clips[c] = clip;
            }

            var mesh = new Mesh
            {
                name = "MountSkin",
                indexFormat = vertCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            skin.Mesh = mesh;
            skin.SeatLocal = ComputeSeat(vertices);
            Plugin.Log.LogInfo($"ValheimMontarias: loaded skin ({vertCount} verts, {boneCount} bones, {clipCount} clips) seat {skin.SeatLocal}");
            return skin;
        }

        /// <summary>
        /// Top of the torso along the spine, ignoring head/tail/legs.
        /// </summary>
        private static Vector3 ComputeSeat(Vector3[] verts)
        {
            if (verts == null || verts.Length == 0)
                return new Vector3(0f, 4.68f, 4.72f);

            Vector3 min = verts[0];
            Vector3 max = verts[0];
            for (int i = 1; i < verts.Length; i++)
            {
                min = Vector3.Min(min, verts[i]);
                max = Vector3.Max(max, verts[i]);
            }

            float z0 = min.z + 0.22f * (max.z - min.z);
            float z1 = max.z - 0.28f * (max.z - min.z);
            float xMid = 0.5f * (min.x + max.x);
            float xLim = 0.18f * Mathf.Max(0.001f, max.x - min.x);

            var ys = new System.Collections.Generic.List<float>(256);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (v.z < z0 || v.z > z1) continue;
                if (Mathf.Abs(v.x - xMid) > xLim) continue;
                ys.Add(v.y);
            }

            if (ys.Count < 8)
                return new Vector3(0f, max.y - 0.2f * (max.y - min.y), 0.5f * (min.z + max.z));

            ys.Sort();
            float yCut = ys[Mathf.FloorToInt((ys.Count - 1) * 0.88f)];
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (v.z < z0 || v.z > z1) continue;
                if (Mathf.Abs(v.x - xMid) > xLim) continue;
                if (v.y < yCut) continue;
                sum += v;
                n++;
            }
            return n > 0 ? sum / n : new Vector3(0f, yCut, 0.5f * (min.z + max.z));
        }

        private static string ReadString(BinaryReader reader)
        {
            int len = reader.ReadUInt16();
            return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(len));
        }

        private static Vector3 ReadVec3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static Quaternion ReadQuat(BinaryReader reader)
        {
            return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static Matrix4x4 ReadMatrix(BinaryReader reader)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
                m[i] = reader.ReadSingle();
            return m;
        }
    }
}
