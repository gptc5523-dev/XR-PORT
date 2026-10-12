#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 모든 Box/Strut가 공유하는 단위 큐브 메시(모서리 안 깎음 — 사용자 요청으로 챔퍼 제거)
        // ※ init/cleanup 리셋 제외 — 토폴로지 불변(파라미터·GameObject 종속 데이터 없음)이라 빌드 간 영구 공유. 다른 캐시와 라이프사이클 비대칭은 의도된 것.
        static Mesh _unitCube;
        // 트러스 절점 연결판(거싯) — 같은 치수끼리 메시 1개 재사용
        static Dictionary<string, Mesh> _plateCache;
        // 접합부 볼트 패턴 — 한 메시에 여러 볼트 헤드. 같은 패턴끼리 메시 1개 재사용(드로콜·정점 절약)
        static Dictionary<string, Mesh> _boltCache;

        // primitive 헬퍼

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = NewCube(name, parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Colorize(go, color);
            return go;
        }

        // 장비 인클로저 라이브러리(민짜 큐브 → 실제 형상) — 정션박스/캐비닛/공조유닛/파워팩을 '함체+부속'으로 조형.
        //   면 법선에서 면내 축을 산식으로 도출 → 어느 방향으로 두든 부속이 면에 맞물린다.

        static Vector3 AbsV(Vector3 a) => new Vector3(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z));

        // a→b 를 잇는 가는 막대(다리/케이블/대각). 부모는 회전·스케일 없는 노드여야 정확.
        static GameObject Strut(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Color color)
        {
            var go = NewCube(name, parent);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(thickness, len, thickness);
            Colorize(go, color);
            return go;
        }

        // 빌트업 브레이스 — 상수 z 평면 위 대각 부재를 웹 플레이트 + 양 가장자리 플랜지 리브(I/채널 단면)로.
        //   민짜 정사각 Strut보다 단면감↑. a→b 는 같은 z 평면(면밖=±Z). LookRotation으로 평면에 평평히 눕혀 롤 고정.
        static void BuiltUpBrace(Transform parent, string name, Vector3 a, Vector3 b, Color color,
                                 float webWidth = 0.024f, float webThick = 0.007f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, d / len);  // 로컬 Y=축, Z=면밖(+Z), X=면내수직
            Vector3 mid = (a + b) * 0.5f;
            var web = NewCube(name, parent);
            web.transform.localPosition = mid;
            web.transform.localRotation = rot;
            web.transform.localScale = new Vector3(webWidth, len, webThick);
            Colorize(web, color);
            for (int s = -1; s <= 1; s += 2)   // 양 가장자리 플랜지 리브(면밖으로 도드라져 I 단면감)
            {
                var fl = NewCube(name + "_Flange", parent);
                fl.transform.localPosition = mid + rot * new Vector3(s * (webWidth * 0.5f - 0.002f), 0f, 0f);
                fl.transform.localRotation = rot;
                fl.transform.localScale = new Vector3(0.004f, len, webThick * 2.2f);
                Colorize(fl, color);
            }
        }

        // a→b 를 잇는 원통(케이블/로프/바퀴 등 둥근 부재). 기본 실린더 높이 2 기준.
        static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Color color)
        {
            var go = NewPrimitive(PrimitiveType.Cylinder, name, parent);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
            Colorize(go, color);
            return go;
        }

        // 복선 플랜지 레일 휠 — 트레드(레일 접지 원통) + 양측 플랜지(콘 플레어 림, 레일 이탈방지) + 허브 보스.
        //   center: 휠 중심, axleAxis: 축 방향. treadR: 트레드 반지름(접지 기준).
        static void RailWheel(Transform parent, string name, Vector3 center, Vector3 axleAxis,
                              float treadR, float treadHalfW, Color wheelC)
        {
            Vector3 ax = axleAxis.normalized;
            float flangeR = treadR * 1.15f;     // 플랜지 오버행(레일 옆면을 무는 림). 지름 0.0299 < 휠피치 0.032 → 비간섭
            float flangeW = treadR * 0.20f;     // 플랜지 두께
            // 트레드(레일과 구르는 원통면)
            Rod(parent, name, center - ax * treadHalfW, center + ax * treadHalfW, treadR, wheelC);
            // 양측 플랜지 — 트레드(반지름 treadR)에서 림(flangeR)으로 플레어하는 절두콘.
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 inner = center + ax * (s * treadHalfW);                 // 트레드 접합부
                Vector3 outer = center + ax * (s * (treadHalfW + flangeW));     // 림 바깥면
                Cone(parent, name + "_Flange", inner, outer, treadR, flangeR, wheelC, 24);
            }
            // 허브 보스(축 중앙 어두운 보스 — 림보다 안쪽으로 살짝 더 길게 빼 축 느낌)
            Rod(parent, name + "_Hub",
                center - ax * (treadHalfW * 1.25f), center + ax * (treadHalfW * 1.25f),
                treadR * 0.42f, CDark);
        }

        // 구체(램프·돔·풍속계 컵 등 둥근 부재). scale로 눌러 돔/타원도 표현.
        static GameObject Ball(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = NewPrimitive(PrimitiveType.Sphere, name, parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Colorize(go, color);
            return go;
        }

        // 원뿔/절두원뿔 — Unity 기본 프리미티브에 없어 메시를 절차 생성.
        // a=밑면 중심, b=꼭대기 중심. rBottom=밑면 반지름, rTop=윗면 반지름(0이면 뾰족한 콘).
        static GameObject Cone(Transform parent, string name, Vector3 a, Vector3 b,
                               float rBottom, float rTop, Color color, int seg = 24)
        {
            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildFrustumMesh(rBottom, rTop, seg);   // 단위 높이(y=0→1), 반지름은 메시에 반영

            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = a;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(1f, len, 1f);     // 높이만 늘림(반지름 유지)
            mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 단위 높이(y=0→1) 절두원뿔 메시 — 옆면 + 위/아래 캡.
        static Mesh BuildFrustumMesh(float rBottom, float rTop, int seg)
        {
            seg = Mathf.Max(8, seg);
            var verts = new List<Vector3>(seg * 2 + 2);
            var tris  = new List<int>(seg * 12);
            for (int i = 0; i < seg; i++)
            {
                float ang = (i / (float)seg) * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                verts.Add(new Vector3(cx * rBottom, 0f, cz * rBottom));  // 밑면 i → 2i
                verts.Add(new Vector3(cx * rTop,    1f, cz * rTop));     // 윗면 i → 2i+1
            }
            for (int i = 0; i < seg; i++)
            {
                int b0 = 2 * i, t0 = 2 * i + 1;
                int j = (i + 1) % seg;
                int b1 = 2 * j, t1 = 2 * j + 1;
                tris.Add(b0); tris.Add(t0); tris.Add(b1);   // 옆면(바깥 향함)
                tris.Add(b1); tris.Add(t0); tris.Add(t1);
            }
            int cBot = verts.Count; verts.Add(new Vector3(0f, 0f, 0f));
            int cTop = verts.Count; verts.Add(new Vector3(0f, 1f, 0f));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                tris.Add(cBot); tris.Add(2 * i);     tris.Add(2 * j);       // 아래 캡(-Y)
                tris.Add(cTop); tris.Add(2 * j + 1); tris.Add(2 * i + 1);   // 위 캡(+Y)
            }
            var m = new Mesh { name = "STS_Cone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 홈 파인 시브(도르래) 휠 — 두 플랜지 + 중앙 V홈(로프 시트) + 허브 보어. 회전체(lathe).
        //   a/b = 양 축단 중심, rOuter=플랜지 외경, rHub=허브 보어 반경, grooveDepth=홈 깊이.
        static GameObject Sheave(Transform parent, string name, Vector3 a, Vector3 b,
                                 float rOuter, float rHub, float grooveDepth, Color color, int seg = 24)
        {
            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildSheaveMesh(rOuter, rHub, grooveDepth, seg);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = a;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(1f, len, 1f);   // 폭(축 길이)만 늘림, 반경은 메시에 반영
            mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 단위 폭(y=0→1) 홈 파인 시브 메시 — 닫힌 단면(좌면→림 플랜지+V홈→우면→보어)을 Y축으로 회전.
        static Mesh BuildSheaveMesh(float rOuter, float rHub, float grooveDepth, int seg)
        {
            seg = Mathf.Max(12, seg);
            float R  = rOuter;
            float rh = Mathf.Clamp(rHub, 0.0003f, rOuter * 0.6f);
            float rg = Mathf.Max(rh + 0.0005f, rOuter - grooveDepth);   // 홈 바닥(로프 시트) 반경
            // 단면 닫힌 루프 (축 t=0→1, 반경 r): 좌 허브→좌 플랜지(얇은 립)→V홈 바닥→우 플랜지 립→우 허브, 보어로 닫힘.
            //   플랜지를 얇은 립(0.15)으로, 홈을 넓게(중앙 70%) → '아령'이 아닌 납작한 도르래 림.
            (float t, float r)[] prof = {
                (0f, rh), (0f, R), (0.15f, R), (0.5f, rg), (0.85f, R), (1f, R), (1f, rh),
            };
            int n = prof.Length;
            var verts = new List<Vector3>(n * seg);
            var tris  = new List<int>(n * seg * 6);
            for (int p = 0; p < n; p++)
                for (int i = 0; i < seg; i++)
                {
                    float ang = (i / (float)seg) * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(ang) * prof[p].r, prof[p].t, Mathf.Sin(ang) * prof[p].r));
                }
            for (int p = 0; p < n; p++)
            {
                int pa = p * seg, pb = ((p + 1) % n) * seg;   // 마지막 p6→p0 = 내부 보어(닫힘)
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    int a0 = pa + i, a1 = pa + j, b0 = pb + i, b1 = pb + j;
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);   // BuildFrustumMesh와 동일 와인딩(바깥 노멀)
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
            }
            var m = new Mesh { name = "STS_Sheave" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 트러스 절점 연결판(거싯)

        // 트러스 절점에 면과 평평하게 붙는 모서리 챔퍼 8각 판(용접 거싯 표현).
        //   faceRot: 판 로컬 +Z를 면 바깥 노멀로 향하게 하는 회전(+Z면=identity, -Z면=180°, +X면=90°, -X면=-90°).
        static GameObject Gusset(Transform parent, Vector3 localPos, Quaternion faceRot,
                                 float size, float thick, Color color)
        {
            string key = $"g{size:F4}_{thick:F4}";
            if (_plateCache == null) _plateCache = new Dictionary<string, Mesh>();
            if (!_plateCache.TryGetValue(key, out var mesh))
            {
                float s = size, c = size * 0.22f;   // c=모서리 챔퍼 폭(과하면 알약처럼 보임)
                var poly = new[]
                {
                    new Vector2(-s + c, -s), new Vector2(s - c, -s),
                    new Vector2(s, -s + c),  new Vector2(s,  s - c),
                    new Vector2(s - c,  s),  new Vector2(-s + c,  s),
                    new Vector2(-s,  s - c), new Vector2(-s, -s + c),
                };
                mesh = BuildPlateMesh(poly, thick);
                _plateCache[key] = mesh;
            }
            string gnm = Numbered("Truss_Gusset");
            if (_deletedParts.Contains(gnm)) return null;   // 삭제 지정 제외
            var go = new GameObject(gnm);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = faceRot;
            Colorize(go, color);
            return go;
        }

        // 평면 볼록 다각형(로컬 XY 점들)을 thickness(로컬 Z)로 압출한 판재 — 앞/뒤 캡 + 옆면.
        // 플랫 셰이딩: 캡·옆면이 정점을 공유하지 않게 면마다 별도 정점을 둬 모서리가 날카롭게(강철판 느낌).
        static Mesh BuildPlateMesh(Vector2[] poly, float thickness)
        {
            int n = poly.Length;
            // CCW 보정 — 앞면(+Z) 캡 노멀이 바깥(+Z)을 향하게.
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            var p = poly;
            if (area < 0f) { p = new Vector2[n]; for (int i = 0; i < n; i++) p[i] = poly[n - 1 - i]; }

            float hz = thickness * 0.5f;
            var v = new List<Vector3>();
            var t = new List<int>();

            // 앞 캡(+Z) — 자체 정점
            int f0 = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, hz));
            for (int i = 1; i < n - 1; i++) { t.Add(f0); t.Add(f0 + i); t.Add(f0 + i + 1); }
            // 뒤 캡(-Z) — 자체 정점(역와인딩)
            int b0 = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, -hz));
            for (int i = 1; i < n - 1; i++) { t.Add(b0); t.Add(b0 + i + 1); t.Add(b0 + i); }
            // 옆면 — 변마다 4정점(공유 안 함)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int q = v.Count;
                v.Add(new Vector3(p[i].x, p[i].y,  hz));   // fi
                v.Add(new Vector3(p[i].x, p[i].y, -hz));   // bi
                v.Add(new Vector3(p[j].x, p[j].y,  hz));   // fj
                v.Add(new Vector3(p[j].x, p[j].y, -hz));   // bj
                t.Add(q); t.Add(q + 1); t.Add(q + 2);
                t.Add(q + 2); t.Add(q + 1); t.Add(q + 3);
            }
            var m = new Mesh { name = "STS_Plate" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 접합부 볼트 (2단계)

        // 볼트 패턴 — 격자(스플라이스 플레이트 볼트 그리드)
        static Vector2[] BoltGrid(int nx, int ny, float dx, float dy)
        {
            var list = new List<Vector2>(nx * ny);
            for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
                list.Add(new Vector2((ix - (nx - 1) * 0.5f) * dx, (iy - (ny - 1) * 0.5f) * dy));
            return list.ToArray();
        }

        // 여러 볼트 헤드를 담은 공유 메시 — 접합부당 GameObject 1개로 N개 볼트 표현.
        // 평면(로컬 z=0)에서 +Z로 돌출. faceRot로 면 노멀에 맞춰 배치.
        static Mesh GetBoltMesh(string key, Vector2[] pos, float boltR, float boltH, int seg = 6)
        {
            if (_boltCache == null) _boltCache = new Dictionary<string, Mesh>();
            if (_boltCache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in pos) AddBoltPrism(v, t, p, boltR, boltH, seg);
            m = new Mesh { name = "STS_Bolts" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            _boltCache[key] = m;
            return m;
        }

        // 단일 고력볼트 헤드 — 면(z=0)에서 +Z로 h 돌출. 둥근 받침 와셔 + 육각 머리(곧은 벽 + 상단 모따기 + 윗 육각면).
        // 실제 ISO 육각볼트의 시그니처(머리 모서리 챔퍼 + 와셔)를 살려 납작한 프리즘 느낌을 제거. 아랫면은 면에 묻혀 생략.
        static void AddBoltPrism(List<Vector3> v, List<int> t, Vector2 c, float r, float h, int seg)
        {
            // 머리 높이 h 기준 비례(전체 돌출 = h 유지)
            float washerH  = h * 0.2f;                          // 받침 와셔 두께
            float wallTopZ = washerH + (h - washerH) * 0.55f;   // 육각 머리 곧은 벽 상단
            float topZ     = h;                                 // 모따기 끝 = 윗 육각면
            float topR     = r * 0.82f;                         // 모따기 후 윗 육각면 외접반경
            float washerR  = r * 1.12f;                         // 와셔 반경 — 머리 대각폭(r)보다 살짝만 크게(고리처럼 안 보이게)

            // 1) 둥근 받침 와셔(매끈) — 옆면 + 윗 환형 캡
            AddRoundCollar(v, t, c, washerR, 0f, washerH, 12);
            // 2) 육각 머리 곧은 벽(평면 패싯)
            AddHexBand(v, t, c, r, r, washerH, wallTopZ, seg);
            // 3) 상단 모따기 밴드(벽 → 좁아진 윗면)
            AddHexBand(v, t, c, r, topR, wallTopZ, topZ, seg);
            // 4) 윗 육각 캡
            AddHexCap(v, t, c, topR, topZ, seg);
        }

        // 육각 단면 코너 좌표(밴드/캡 정렬용). 원래 프리즘과 동일하게 코너를 step/2만큼 회전.
        static Vector3 HexCorner(Vector2 c, float r, float ang, float z)
            => new Vector3(c.x + Mathf.Cos(ang) * r, c.y + Mathf.Sin(ang) * r, z);

        // 육각 밴드(하단링 rLow@zLow → 상단링 rHigh@zHigh) — 면마다 독립 정점으로 평면 패싯(각진 육각 유지).
        static void AddHexBand(List<Vector3> v, List<int> t, Vector2 c,
                               float rLow, float rHigh, float zLow, float zHigh, int seg)
        {
            float step = Mathf.PI * 2f / seg;
            for (int f = 0; f < seg; f++)
            {
                float a0 = f * step + step * 0.5f, a1 = (f + 1) * step + step * 0.5f;
                int b = v.Count;
                v.Add(HexCorner(c, rLow,  a0, zLow));    // b  : low A
                v.Add(HexCorner(c, rLow,  a1, zLow));    // b+1: low B
                v.Add(HexCorner(c, rHigh, a1, zHigh));   // b+2: high B
                v.Add(HexCorner(c, rHigh, a0, zHigh));   // b+3: high A
                t.Add(b); t.Add(b + 1); t.Add(b + 2);    // 바깥(반경) 노멀
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        // 윗 육각 캡(중심 팬, +Z 향함)
        static void AddHexCap(List<Vector3> v, List<int> t, Vector2 c, float r, float z, int seg)
        {
            float step = Mathf.PI * 2f / seg;
            int cc = v.Count; v.Add(new Vector3(c.x, c.y, z));   // 중심
            int rb = v.Count;
            for (int i = 0; i < seg; i++) v.Add(HexCorner(c, r, i * step + step * 0.5f, z));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                t.Add(cc); t.Add(rb + i); t.Add(rb + j);
            }
        }

        // 둥근 와셔 칼라 — 매끈한 옆면(정점 공유 → RecalculateNormals로 원통 음영) + 윗 환형 디스크(육각 머리 밑면 덮음).
        static void AddRoundCollar(List<Vector3> v, List<int> t, Vector2 c, float r, float zLo, float zHi, int seg)
        {
            int b = v.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a) * r, cy = Mathf.Sin(a) * r;
                v.Add(new Vector3(c.x + cx, c.y + cy, zLo));   // b+2i  : 밑
                v.Add(new Vector3(c.x + cx, c.y + cy, zHi));   // b+2i+1: 위
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                int b0 = b + 2 * i, t0 = b + 2 * i + 1, b1 = b + 2 * j, t1 = b + 2 * j + 1;
                t.Add(b0); t.Add(b1); t.Add(t1);   // 옆면(바깥 노멀)
                t.Add(b0); t.Add(t1); t.Add(t0);
            }
            int cap = v.Count; v.Add(new Vector3(c.x, c.y, zHi));   // 윗 디스크 중심(+Z)
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                t.Add(cap); t.Add(b + 2 * i + 1); t.Add(b + 2 * j + 1);
            }
        }

        // 볼트 패턴 메시를 면(faceRot)에 부착 — 다크 강철. plateOffset만큼 면 바깥으로 띄움.
        static void Bolts(Transform parent, Vector3 pos, Quaternion faceRot, string key,
                          Vector2[] patt, float boltR, float plateOffset)
        {
            var mesh = GetBoltMesh(key, patt, boltR, boltR * 0.85f, 6);
            string bnm = Numbered("Joint_Bolts");
            if (_deletedParts.Contains(bnm)) return;        // 삭제 지정 제외
            var go = new GameObject(bnm);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos + faceRot * new Vector3(0f, 0f, plateOffset);
            go.transform.localRotation = faceRot;
            Colorize(go, CDark);
        }

        // 연결 플레이트(거싯) + 볼트 사각 격자 한 세트 — 면 노멀(faceRot)에 정렬.
        //   실제 구조 접합은 원형 링이 아니라 nx×ny 사각 그리드(행×열) — 그에 맞춰 격자로 박는다.
        static void BoltedPlate(Transform parent, Vector3 pos, Quaternion faceRot,
                                float size, int nx, int ny, float dx, float dy, float boltR)
        {
            float thick = 0.005f;
            Gusset(parent, pos, faceRot, size, thick, CStruct);
            Bolts(parent, pos, faceRot, $"grid{nx}x{ny}_{dx:F4}_{dy:F4}_{boltR:F4}",
                  BoltGrid(nx, ny, dx, dy), boltR, thick * 0.5f + 0.0004f);
        }

        // 안 깎은 단위 큐브를 공유 — 모든 Box/Strut가 같은 메시 사용(모서리 챔퍼 제거, 사용자 요청)
        static GameObject NewCube(string name, Transform parent)
        {
            var go = new GameObject(Numbered(name));
            go.AddComponent<MeshFilter>().sharedMesh = GetUnitCube();
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        // 안 깎은 단위 큐브 메시(모서리 챔퍼 제거 — 사용자 요청). 면 방향은 outward로 자동 보정, 면당 UV 0~1 유지.
        static Mesh GetUnitCube()
        {
            if (_unitCube != null) return _unitCube;
            const float h = 0.5f;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };

            // 6 면(각 면 ±h 전체 — 날카로운 직각 모서리)
            for (int a = 0; a < 3; a++)
            for (int s = -1; s <= 1; s += 2)
            {
                int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
                AddBevQuad(v, t, uv,
                    AxV(a, s * h, a1, -h, a2, -h), AxV(a, s * h, a1, h, a2, -h),
                    AxV(a, s * h, a1, h, a2, h),   AxV(a, s * h, a1, -h, a2, h), ax[a] * s);
            }

            var m = new Mesh { name = "STS_UnitCube" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return _unitCube = m;
        }

        // 축 인덱스로 Vector3 구성
        static Vector3 AxV(int a, float va, int a1, float va1, int a2, float va2)
        { var p = Vector3.zero; p[a] = va; p[a1] = va1; p[a2] = va2; return p; }

        // 사각/삼각 추가 — outward 기준으로 와인딩 자동 보정(뒤집힘 방지) + UV 0~1
        static void AddBevQuad(List<Vector3> v, List<int> t, List<Vector2> uv,
                               Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            else
            { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        }

        // ProBuilder 편집형 박스 — CreatePrimitive 대신 ProBuilder 메시로 생성해 디자이너가 바로 다듬을 수 있다.
        //   euler 주면 회전. 콜라이더는 시각 전용으로 제거(기존 부품과 통일).
        static GameObject PbBox(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, Vector3 euler = default)
        {
            var pb = UnityEngine.ProBuilder.ShapeGenerator.GenerateCube(UnityEngine.ProBuilder.PivotLocation.Center, size);
            pb.name = Numbered(name);
            pb.transform.SetParent(parent, worldPositionStays: false);
            pb.transform.localPosition = localPos;
            if (euler != Vector3.zero) pb.transform.localRotation = Quaternion.Euler(euler);
            // 베벨 비활성 — 전 모서리 일괄 베벨은 blind로 자기교차해 메시가 산산조각 난다.
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return pb.gameObject;
        }

        // 파츠 번호 — 같은 이름마다 _1,_2…를 붙여 하이어라키에서 인스턴스를 구분(작업 지정 편의). 생성마다 _nameSeq 리셋.
        static readonly Dictionary<string, int> _nameSeq = new Dictionary<string, int>();
        // 삭제 지정 인스턴스 — 절차 생성이 결정적이라 이름(번호)이 고정됨. 여기 적힌 이름만 생성 제외(번호는 소비돼 이후 번호 안 밀림).
        //   하이어라키에서 지우고 싶은 객체 이름을 추가하면 재생성해도 안 나옴.
        static readonly HashSet<string> _deletedParts = new HashSet<string>
        {
            "Truss_Gusset_129", "Truss_Gusset_130", "Joint_Bolts_1", "Joint_Bolts_2",
            "MH_DoorKick_1", "MHAccess_Bracket_2", "Ladder_Bracket_5",
            "Boom_Cross_4",   // 권상 로프(HoistU_Rope) 통로의 바닥 횡재 — 로프 관통 제거
        };

        // 삭제 지정 일괄 제거 — Gusset/Joint_Bolts 외 모든 파츠(Box/Strut/Cone/PbBox…)에 대응.
        //   생성은 정상 진행돼 Numbered 번호가 소비되므로 이후 인스턴스 번호가 밀리지 않고, 생성 후 이름으로 제거한다.
        static void PruneDeletedParts(Transform root)
        {
            if (_deletedParts.Count == 0) return;
            var doomed = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && _deletedParts.Contains(t.name))
                    doomed.Add(t.gameObject);
            foreach (var go in doomed)
                if (go != null) Object.DestroyImmediate(go);
        }
        static string Numbered(string n)
        {
            int c = _nameSeq.TryGetValue(n, out var v) ? v + 1 : 1;
            _nameSeq[n] = c;
            return n + "_" + c;
        }

        static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = Numbered(name);
            // 에디터 클릭 방해 줄이려 collider 제거(시각화 전용).
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }
    }
}
#endif
