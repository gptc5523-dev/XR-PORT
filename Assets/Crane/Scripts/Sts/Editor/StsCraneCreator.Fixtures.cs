#if UNITY_EDITOR
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 정션 박스(전기 결선함) — 함체 + 볼트 덮개 + 케이블 글랜드 + 베이스 플랜지.
        //   coverFace=점검 도어 향하는 면 법선, glandDir=케이블 인입 방향(데크형=Vector3.down, 벽부착형=벽 안쪽).
        static void JunctionBox(Transform parent, string name, Vector3 center, Vector3 body,
                                Vector3 coverFace, Vector3 glandDir, Color bodyC)
        {
            Vector3 fAbs = AbsV(coverFace);
            Vector3 u, v;   // 덮개 면내 두 축
            if (fAbs.x > 0.5f) { u = Vector3.up;    v = Vector3.forward; }
            else if (fAbs.y > 0.5f) { u = Vector3.right; v = Vector3.forward; }
            else { u = Vector3.right; v = Vector3.up; }
            float eF = Vector3.Dot(fAbs, body) * 0.5f;
            float eU = Vector3.Dot(AbsV(u), body) * 0.5f;
            float eV = Vector3.Dot(AbsV(v), body) * 0.5f;

            // 1) 함체
            Box(parent, name + "_Body", center, body, bodyC);

            // 2) 볼트 덮개(면내 86% + 모서리 볼트 4)
            float coverT = Mathf.Max(0.0022f, eF * 0.18f);
            Vector3 coverSize = fAbs * coverT + AbsV(u) * (2f * eU * 0.86f) + AbsV(v) * (2f * eV * 0.86f);
            Box(parent, name + "_Cover", center + coverFace * (eF + coverT * 0.5f), coverSize, CStruct);
            float boltR = Mathf.Max(0.0011f, Mathf.Min(eU, eV) * 0.14f);
            Vector3 boltScale = fAbs * (boltR * 1.4f) + (AbsV(u) + AbsV(v)) * (boltR * 2f);
            Vector3 boltBase = center + coverFace * (eF + coverT);
            for (int su = -1; su <= 1; su += 2)
            for (int sv = -1; sv <= 1; sv += 2)
                Ball(parent, name + "_Bolt",
                    boltBase + u * (su * eU * 0.66f) + v * (sv * eV * 0.66f), boltScale, CDark);

            // 3) 케이블 글랜드 2(너트 + 케이블 스텁)
            Vector3 gAbs = AbsV(glandDir);
            Vector3 gu = (gAbs.y > 0.5f) ? Vector3.right : Vector3.up;
            float eG  = Vector3.Dot(gAbs, body) * 0.5f;
            float eGu = Vector3.Dot(AbsV(gu), body) * 0.5f;
            float glandR = Mathf.Max(0.0012f, eGu * 0.18f);
            float glandLen = Mathf.Max(0.004f, eG * 0.8f);
            Vector3 gNutScale = gAbs * (glandR * 1.2f) + (Vector3.one - gAbs) * (glandR * 2.2f);
            Vector3 gBase = center + glandDir * eG;
            for (int g = -1; g <= 1; g += 2)
            {
                Vector3 gp = gBase + gu * (g * eGu * 0.45f);
                Ball(parent, name + "_GlandNut", gp, gNutScale, CStruct);
                Rod(parent, name + "_Cable", gp, gp + glandDir * glandLen, glandR * 0.7f, CCable);
            }

            // 4) 베이스 플랜지(부착면) + 앵커 볼트 2 — 떠 보이지 않게 접지
            Vector3 bodyPerpG = Vector3.Scale(body, Vector3.one - gAbs);
            Box(parent, name + "_Base", center + glandDir * (eG + 0.0015f),
                gAbs * 0.003f + bodyPerpG * 1.35f, CStruct);
            Vector3 abAxis = (gAbs.y > 0.5f) ? Vector3.forward : Vector3.up;
            float eAb = Vector3.Dot(AbsV(abAxis), body) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                Ball(parent, name + "_AnchorBolt",
                    center + glandDir * (eG + 0.003f) + abAxis * (s * eAb * 1.2f),
                    gAbs * (boltR * 1.2f) + (Vector3.one - gAbs) * (boltR * 1.8f), CDark);
        }

        // 제어 캐비닛(직립) — 플린스 + 본체 + 더블도어/손잡이 + 측면 루버 + 지붕 오버행 + 상부 공조 + 인양러그.
        //   frontFace=도어 향하는 수평 면 법선(±X/±Z). 높이는 항상 +Y.
        static void ControlCabinet(Transform parent, string name, Vector3 center, Vector3 body,
                                   Vector3 frontFace, Color bodyC)
        {
            Vector3 up = Vector3.up;
            Vector3 fAbs = AbsV(frontFace);
            Vector3 wide = Vector3.Cross(up, frontFace);
            if (wide.sqrMagnitude < 1e-6f) wide = Vector3.right;
            wide = wide.normalized;
            Vector3 wAbs = AbsV(wide);
            float eF = Vector3.Dot(fAbs, body) * 0.5f;
            float eW = Vector3.Dot(wAbs, body) * 0.5f;
            float eH = body.y * 0.5f;
            float topY = center.y + eH, botY = center.y - eH;

            Box(parent, name + "_Plinth", new Vector3(center.x, botY + 0.004f, center.z),
                fAbs * (2f * eF * 0.9f) + wAbs * (2f * eW * 0.96f) + up * 0.008f, CDark);
            Box(parent, name + "_Body", center + up * 0.004f, body, bodyC);
            Box(parent, name + "_Roof", new Vector3(center.x, topY + 0.003f, center.z),
                fAbs * (2f * eF + 0.006f) + wAbs * (2f * eW + 0.006f) + up * 0.005f, CStruct);

            // 더블 도어(중앙 이음매 분할) + 손잡이
            float doorT = 0.0022f;
            Vector3 doorFacePos = center + frontFace * (eF + doorT * 0.5f) + up * 0.004f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 leaf = doorFacePos + wide * (s * eW * 0.49f);
                Box(parent, name + "_Door", leaf,
                    fAbs * doorT + wAbs * (eW * 0.9f) + up * (2f * eH * 0.82f), CStruct);
                Box(parent, name + "_DoorHandle", leaf - wide * (s * eW * 0.42f) + frontFace * doorT,
                    fAbs * 0.004f + wAbs * 0.004f + up * (eH * 0.5f), CDark);
            }
            // 측면(±wide) 가로 루버 3
            for (int s = -1; s <= 1; s += 2)
            for (int l = -1; l <= 1; l++)
                Box(parent, name + "_Louver",
                    center + wide * (s * (eW + 0.0015f)) + up * (l * eH * 0.4f),
                    wAbs * 0.003f + fAbs * (2f * eF * 0.7f) + up * 0.004f, CDark);
            // 지붕 공조 박스 + 인양 러그 2
            Box(parent, name + "_TopAC",
                new Vector3(center.x, topY + 0.012f, center.z) - frontFace * (eF * 0.3f),
                fAbs * (2f * eF * 0.5f) + wAbs * (2f * eW * 0.5f) + up * 0.014f, CMachine);
            for (int s = -1; s <= 1; s += 2)
                Box(parent, name + "_LiftLug",
                    new Vector3(center.x, topY + 0.008f, center.z) + wide * (s * eW * 0.8f),
                    wAbs * 0.004f + fAbs * 0.006f + up * 0.008f, CStruct);
        }

        // 공조/냉각 유닛(상부 팬) — 스키드 + 본체 + 팬가드(링+십자스포크+허브) + 측면 루버 + 전면 파이프/패널 + 인양러그.
        static void EquipmentUnit(Transform parent, string name, Vector3 center, Vector3 body, Color bodyC)
        {
            float ex = body.x * 0.5f, ey = body.y * 0.5f, ez = body.z * 0.5f;
            float topY = center.y + ey, botY = center.y - ey;

            Box(parent, name + "_Skid", new Vector3(center.x, botY + 0.002f, center.z),
                new Vector3(body.x * 1.1f, 0.005f, body.z * 1.1f), CStruct);
            Box(parent, name + "_Body", center, body, bodyC);

            float fanR = Mathf.Min(ex, ez) * 0.82f;
            Vector3 fanCtr = new Vector3(center.x, topY + 0.003f, center.z);
            Rod(parent, name + "_FanRing",
                fanCtr - Vector3.up * 0.0015f, fanCtr + Vector3.up * 0.0015f, fanR, CDark);
            Box(parent, name + "_FanSpoke", fanCtr, new Vector3(2f * fanR, 0.0015f, 0.0025f), CStruct);
            Box(parent, name + "_FanSpoke", fanCtr, new Vector3(0.0025f, 0.0015f, 2f * fanR), CStruct);
            Ball(parent, name + "_FanHub", fanCtr, Vector3.one * (fanR * 0.4f), CMachine);

            for (int s = -1; s <= 1; s += 2)
            for (int l = -1; l <= 1; l++)
                Box(parent, name + "_Louver",
                    new Vector3(center.x, center.y + l * ey * 0.45f, center.z + s * (ez + 0.0015f)),
                    new Vector3(2f * ex * 0.8f, 0.004f, 0.003f), CDark);

            for (int s = -1; s <= 1; s += 2)
                Rod(parent, name + "_Pipe",
                    new Vector3(center.x - ex, center.y + s * ey * 0.35f, center.z + s * ez * 0.3f),
                    new Vector3(center.x - ex - 0.008f, center.y + s * ey * 0.35f, center.z + s * ez * 0.3f),
                    0.0022f, CMachine);
            Box(parent, name + "_AccessPanel", new Vector3(center.x - ex - 0.0011f, center.y, center.z),
                new Vector3(0.0022f, 2f * ey * 0.7f, 2f * ez * 0.7f), CStruct);
            for (int s = -1; s <= 1; s += 2)
                Box(parent, name + "_LiftLug",
                    new Vector3(center.x + s * ex * 0.8f, topY + 0.006f, center.z),
                    new Vector3(0.004f, 0.008f, 0.004f), CStruct);
        }

        // 유압 파워팩 — 오일탱크 + 상부 전동기/펌프(가로 실린더) + 매니폴드/유압포트 + 필러캡 + 유면계 + 스키드.
        //   매니폴드/포트는 중앙 방향(-X)으로 인출하고, 포트 끝 좌표 2개를 반환 → 호스가 그 끝에 물려 허공에 안 뜬다.
        static Vector3[] PowerPack(Transform parent, string name, Vector3 center, Vector3 body, Color bodyC)
        {
            float ex = body.x * 0.5f, ey = body.y * 0.5f, ez = body.z * 0.5f;
            float mY = center.y + ey + 0.006f;

            Box(parent, name + "_Tank", center, body, bodyC);
            Box(parent, name + "_Skid", new Vector3(center.x, center.y - ey + 0.002f, center.z),
                new Vector3(body.x * 1.1f, 0.004f, body.z * 1.1f), CStruct);
            // 전동기(축 Z) + 펌프(끝단 작은 실린더)
            Rod(parent, name + "_Motor",
                new Vector3(center.x, mY, center.z - ez * 0.6f),
                new Vector3(center.x, mY, center.z + ez * 0.6f), Mathf.Min(ex, ey) * 0.45f, CMachine);
            Rod(parent, name + "_Pump",
                new Vector3(center.x, mY, center.z + ez * 0.6f),
                new Vector3(center.x, mY, center.z + ez * 0.92f), Mathf.Min(ex, ey) * 0.28f, CDark);
            // 매니폴드(중앙 방향 -X 끝 상부) + 유압 포트 2(-X로 인출, 끝에 커플링)
            float portY = center.y + ey + 0.003f;
            float manX = center.x - ex * 0.35f;
            Box(parent, name + "_Manifold", new Vector3(manX, portY, center.z),
                new Vector3(ex * 0.5f, 0.01f, ez * 0.7f), CDark);
            var tips = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                int s = i == 0 ? -1 : 1;
                Vector3 a = new Vector3(manX - ex * 0.2f, portY, center.z + s * ez * 0.35f);
                Vector3 b = new Vector3(center.x - ex - 0.01f, portY, center.z + s * ez * 0.35f);
                Rod(parent, name + "_Port", a, b, 0.0018f, CMachine);
                Ball(parent, name + "_PortCoupling", b, Vector3.one * 0.004f, CStruct);
                tips[i] = b;
            }
            // 주유/브리더 캡 — 떠 보이지 않게 탱크 윗면에 '묻은' 짧은 원통 넥 + 윗 캡(구 아님). +X쪽 배치.
            Vector3 fcBase = new Vector3(center.x + ex * 0.4f, center.y + ey - 0.002f, center.z); // 탱크 안에서 시작
            Rod(parent, name + "_FillerNeck", fcBase, fcBase + Vector3.up * 0.008f, 0.0035f, CMachine);
            Rod(parent, name + "_FillerCap", fcBase + Vector3.up * 0.007f, fcBase + Vector3.up * 0.011f, 0.0042f, CStruct);
            Box(parent, name + "_SightGauge", new Vector3(center.x + ex + 0.0011f, center.y, center.z + ez * 0.4f),
                new Vector3(0.0022f, 2f * ey * 0.5f, 0.004f), CGlass);
            return tips;
        }

        // 투광등 어셈블리 — at=부착점(중심), 렌즈는 -Y로 비춤. w=본체 폭, 수직 치수는 w 비례.
        //   브래킷판 → 요크 암 → 방열핀 3장 → 램프 본체 → 베젤 림 → 평면 발광 렌즈, 전부 수직 적층.
        static void Floodlight(Transform parent, Vector3 at, float w, Color body, Color lens)
        {
            float plateT = 0.222f * w;   // 브래킷 판 두께
            float gap    = 0.278f * w;   // 요크 간격(방열핀이 사는 공간)
            float hBody  = 0.611f * w;   // 본체 높이
            float bezT   = 0.139f * w;   // 베젤 두께
            float lensT  = 0.111f * w;   // 렌즈 두께
            float armT   = 0.170f * w;   // 요크 암 두께
            float finT   = 0.120f * w;   // 방열핀 두께

            float yPlate   = at.y - plateT * 0.5f;             // 브래킷 중심(top=at.y, 면에 물림)
            float yBodyTop = at.y - plateT - gap;              // 본체 윗면
            float yBody    = yBodyTop - hBody * 0.5f;          // 본체 중심
            float yBodyBot = yBodyTop - hBody;                 // 본체 밑면
            float yBez     = yBodyBot - bezT * 0.5f;           // 베젤 중심
            float yLens    = yBodyBot - bezT - lensT * 0.5f;   // 렌즈 중심(최하단, 조사면)

            // 1) 부착 브래킷 판
            PbBox(parent, "Floodlight_Bracket", new Vector3(at.x, yPlate, at.z),
                new Vector3(w * 0.7f, plateT, w * 0.7f), CStruct);
            // 2) 요크 암(±X) — 판 밑에서 본체 밑까지 본체를 감싸 받친다(안쪽면=본체 가장자리)
            float yArm = ((at.y - plateT) + yBodyBot) * 0.5f;
            float hArm = (at.y - plateT) - yBodyBot;
            for (int a = -1; a <= 1; a += 2)
                PbBox(parent, "Floodlight_Yoke", new Vector3(at.x + a * (w * 0.5f + armT * 0.5f), yArm, at.z),
                    new Vector3(armT, hArm, w * 0.55f), CStruct);
            // 3) 방열핀 3장 — 요크 간격에 X로 나란히(±0.27w±finT/2 < 본체 반폭이라 비간섭)
            for (int f = -1; f <= 1; f++)
                PbBox(parent, "Floodlight_Fin", new Vector3(at.x + f * w * 0.27f, yBodyTop + gap * 0.5f, at.z),
                    new Vector3(finT, gap, w * 0.82f), CMachine);
            // 4) 램프 본체
            PbBox(parent, "Floodlight_Housing", new Vector3(at.x, yBody, at.z),
                new Vector3(w, hBody, w), body);
            // 5) 베젤 림 — 렌즈 둘레 프레임(본체보다 살짝 넓게)
            PbBox(parent, "Floodlight_Bezel", new Vector3(at.x, yBez, at.z),
                new Vector3(w * 1.06f, bezT, w * 1.06f), CMachine);
            // 6) 평면 렌즈(발광) — 조사면 보는 납작 원반(구 금지). Rod 짧은 원통=디스크.
            Rod(parent, "Floodlight_Lens", new Vector3(at.x, yLens + lensT * 0.5f, at.z),
                new Vector3(at.x, yLens - lensT * 0.5f, at.z), w * 0.42f, lens);
        }

        // 방향 지정 투광등 — at에 회전 피벗(빈 오브젝트)을 두고 하향 어셈블리를 aim으로 돌린다.
        //   aim=identity면 하향(-Y). Euler(0,0,90)→렌즈 +X 수평, Euler(0,0,45)→+X 아래 45°(안벽 작업역).
        static void Floodlight(Transform parent, Vector3 at, Quaternion aim, float w, Color body, Color lens)
        {
            var pivot = new GameObject(Numbered("Floodlight_Mount"));
            pivot.transform.SetParent(parent, worldPositionStays: false);
            pivot.transform.localPosition = at;
            pivot.transform.localRotation = aim;
            Floodlight(pivot.transform, Vector3.zero, w, body, lens);
        }

        // 비콘(항공장애등/마커등) — 구 돔 대체. baseCenter=바닥 부착점, 위로 적층.
        //   검은 베이스 드럼 + 컬러 발광 렌즈 드럼(구 아님) + 검은 캡. 전부 맞닿음. 총높이 = 2.5r.
        static void Beacon(Transform parent, string prefix, Vector3 baseCenter, float r, Color lensColor)
        {
            float hBase = r * 0.9f, hLens = r * 1.1f, hCap = r * 0.5f;
            float y0 = baseCenter.y;
            Rod(parent, prefix + "_Base", new Vector3(baseCenter.x, y0, baseCenter.z),
                new Vector3(baseCenter.x, y0 + hBase, baseCenter.z), r, CDark);
            Rod(parent, prefix + "_Lens", new Vector3(baseCenter.x, y0 + hBase, baseCenter.z),
                new Vector3(baseCenter.x, y0 + hBase + hLens, baseCenter.z), r * 0.92f, lensColor);
            Rod(parent, prefix + "_Cap", new Vector3(baseCenter.x, y0 + hBase + hLens, baseCenter.z),
                new Vector3(baseCenter.x, y0 + hBase + hLens + hCap, baseCenter.z), r * 0.62f, CDark);
        }

        // 90° 곡관 엘보: Rod segs개로 호 근사 + 절점마다 구로 메움. din/dout = V 도착/출발 단위방향(직교 가정).
        //   접점 T1=V-din·Rb, T2=V+dout·Rb — 직선 구간을 여기서 트림하면 접선 연속.
        static void PipeElbow(Transform parent, string name, Vector3 V, Vector3 din, Vector3 dout,
                              float r, float Rb, Color color, int segs = 10)
        {
            Vector3 O = V + Rb * (dout - din);
            Vector3 prev = V - din * Rb;   // T1 (직선 구간과의 접점)
            // 모든 절점(T1·T2 및 내부 패싯)에 관 굵기 구로 메움 — 짧은 원통은 접점에서도 호의 접선이 아닌 현(chord)이라
            //   ~(90/segs/2)° 꺾여 틈이 생긴다. 구(지름=관 굵기, 안 부풀음)가 그 킹크/틈을 모두 덮는다.
            Ball(parent, name + "_Node", prev, new Vector3(2f * r, 2f * r, 2f * r), color);  // T1 — 직선↔엘보 이음
            for (int i = 1; i <= segs; i++)
            {
                float ph = (i / (float)segs) * (Mathf.PI * 0.5f);
                Vector3 p = O - Rb * Mathf.Cos(ph) * dout + Rb * Mathf.Sin(ph) * din;
                Rod(parent, name, prev, p, r, color);
                Ball(parent, name + "_Node", p, new Vector3(2f * r, 2f * r, 2f * r), color);  // 모든 절점(T2 포함) 메움 → 이음 틈/킹크 제거
                prev = p;
            }
        }

        // 꺾인 도관 경로 — 정점 배열을 직선 Rod로 잇되, 내부 90° 굽힘마다 PipeElbow로 둥글게.
        //   직선 구간은 양 끝이 내부 굽힘이면 Rb만큼 트림해 엘보 접점과 정확히 만난다.
        static void ConduitPath(Transform parent, string name, Vector3[] pts, float r, float Rb, Color color)
        {
            int n = pts.Length;
            var dir = new Vector3[n - 1];
            for (int i = 0; i < n - 1; i++) dir[i] = (pts[i + 1] - pts[i]).normalized;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                if (i > 0)     a += dir[i] * Rb;   // 시작점이 내부 굽힘 → 접점까지 당김
                if (i < n - 2) b -= dir[i] * Rb;   // 끝점이 내부 굽힘 → 접점까지 당김
                Rod(parent, name, a, b, r, color);
            }
            for (int i = 1; i < n - 1; i++)
                PipeElbow(parent, name + "_Elbow", pts[i], dir[i - 1], dir[i], r, Rb, color);
        }

        // 늘어진 케이블(카테너리) — Unity Splines로 a→b 사이에 sag만큼 처지는 곡선 튜브 메시를 만들어 붙인다.
        //   포물선 근사로 생성 시점에 메시를 굽는다(런타임 컴포넌트 불필요). 팽팽한 로프엔 안 쓰고 처지는 케이블에만.
        static GameObject CableCatenary(Transform parent, string name, Vector3 a, Vector3 b,
                                        float sag, float radius, Color color,
                                        int knots = 5, int sides = 5, int segments = 18)
        {
            var spline = new UnityEngine.Splines.Spline();
            for (int i = 0; i < knots; i++)
            {
                float t = i / (float)(knots - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= 4f * sag * t * (1f - t);   // 포물선 처짐(중앙 최대) — 카테너리 근사
                spline.Add(new Unity.Mathematics.float3(p.x, p.y, p.z),
                           UnityEngine.Splines.TangentMode.AutoSmooth);
            }
            var mesh = new Mesh { name = name + "_Mesh" };
            UnityEngine.Splines.SplineMesh.Extrude(spline, mesh, radius, sides, segments);

            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color);
            return go;
        }
    }
}
#endif
