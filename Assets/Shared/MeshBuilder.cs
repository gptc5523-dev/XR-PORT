using System.Collections.Generic;
using UnityEngine;

namespace Procedural
{
    /// <summary>
    /// 절차적 메시 빌더 — 정점/노멀/UV/서브메시 삼각형을 모아 Mesh로 굽는다.
    /// 면마다 정점을 새로 추가하는 flat-shading 방식. 65535 초과 시 UInt32 인덱스로 자동 전환.
    ///
    /// ProceduralContainerMesh(ContainerProject)·ProceduralCraneMesh(CraneProject)가
    /// 글자 그대로 동일한 구현을 각자 중첩 클래스로 보유하던 것을 한곳으로 합친 것.
    /// 두 생성기 모두 `using Procedural;` 로 이 타입을 공유한다.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _verts   = new List<Vector3>(8192);
        readonly List<Vector3> _normals = new List<Vector3>(8192);
        readonly List<Vector2> _uvs     = new List<Vector2>(8192);
        readonly Dictionary<int, List<int>> _tris = new Dictionary<int, List<int>>();

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            _verts.Add(p);
            _normals.Add(n.sqrMagnitude > 0f ? n.normalized : Vector3.up);
            _uvs.Add(uv);
            return _verts.Count - 1;
        }

        public void AddTriangle(int submesh, int a, int b, int c)
        {
            if (!_tris.TryGetValue(submesh, out var list))
            {
                list = new List<int>(4096);
                _tris[submesh] = list;
            }
            list.Add(a); list.Add(b); list.Add(c);
        }

        public void AddQuad(int submesh, int a, int b, int c, int d)
        {
            AddTriangle(submesh, a, b, c);
            AddTriangle(submesh, a, c, d);
        }

        public void AddBox(int submesh, Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            // 8 corners
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = center + new Vector3( h.x, -h.y, -h.z);
            Vector3 p110 = center + new Vector3( h.x,  h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x,  h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y,  h.z);
            Vector3 p101 = center + new Vector3( h.x, -h.y,  h.z);
            Vector3 p111 = center + new Vector3( h.x,  h.y,  h.z);
            Vector3 p011 = center + new Vector3(-h.x,  h.y,  h.z);

            // 6 faces, 면마다 4 vertex (flat shading)
            AddFace(submesh, p001, p101, p111, p011, Vector3.forward);
            AddFace(submesh, p100, p000, p010, p110, Vector3.back);
            AddFace(submesh, p101, p100, p110, p111, Vector3.right);
            AddFace(submesh, p000, p001, p011, p010, Vector3.left);
            AddFace(submesh, p011, p111, p110, p010, Vector3.up);
            AddFace(submesh, p000, p100, p101, p001, Vector3.down);
        }

        void AddFace(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int ia = AddVertex(a, normal, new Vector2(0f, 0f));
            int ib = AddVertex(b, normal, new Vector2(1f, 0f));
            int ic = AddVertex(c, normal, new Vector2(1f, 1f));
            int id = AddVertex(d, normal, new Vector2(0f, 1f));
            AddQuad(submesh, ia, ib, ic, id);
        }

        /// <param name="tangents">
        /// 탄젠트 생성 여부. 기본 true(기존 동작 불변). 노멀맵을 쓰지 않는 저폴리 LOD 는 false 로 —
        /// 정점이 48 B → 32 B 가 되어 GPU 처리율 구간이 달라진다(문서/컨테이너_규격.md Part 5 §11.7 실측).
        /// </param>
        public Mesh ToMesh(string name, bool tangents = true)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);

            int maxSub = 0;
            foreach (var k in _tris.Keys) if (k > maxSub) maxSub = k;
            mesh.subMeshCount = maxSub + 1;
            for (int s = 0; s <= maxSub; s++)
            {
                if (_tris.TryGetValue(s, out var list))
                    mesh.SetTriangles(list, s);
                else
                    mesh.SetTriangles(System.Array.Empty<int>(), s);
            }

            if (tangents) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
