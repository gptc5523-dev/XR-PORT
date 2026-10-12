#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 가동부 시각화

        static void BuildTrolleyVisual(Transform trolley)
        {
            // [Option C 기계실 통합 박스] 트롤리를 실척 ≈7m 박스 프레임으로 확장(육지쪽 절반에 운전실 통합).
            //   +X=바다, −X=육지. 인양점(헤드/시브)은 스프레더 직상 X=HoistX, 본체 중심은 육지쪽으로 TrolleyCX 이동 → 육지절반이 캐빈 통합부.
            float tZ = 0.24f;   // nested 본체 폭(z±0.12 = 바퀴·다운레그와 일치, < 갭/2=0.1325)
            PbBox(trolley, "Trolley_Body", new Vector3(TrolleyCX, -0.025f, 0f),
                new Vector3(2f * TrolleyHX, 0.05f, tZ), CTrolley);
            BuildTrolleyBodyFrame(trolley, TrolleyHX, TrolleyCX);   // 1) 본체 프레임화(확장 치수 전달)
            // 헤드(로프 인양점) — 인양점 X=HoistX(항구쪽 이동), 스프레더 직상
            PbBox(trolley, StsPartNames.TrolleyHead, new Vector3(HoistX, -0.06f, 0f),
                new Vector3(0.07f, 0.025f, tZ * 0.85f), CDark);
            // [rail-in-middle] 주행 보기 — 긴 본체 양 끝 근처 2스테이션 × 좌우 레일(Z=±railZ). 레일 위 트레드 접지(중심 0.037).
            //   8륜(2스테이션×2륜×2레일)으로 긴 박스 하중 분산. 다운레그는 레일 안쪽 z=0.10(레일 z=0.12 회피).
            float railZ = GirderGapZ - GirderWidthZ * 0.5f - 0.0125f;   // ≈0.12, 붐 레일·본체와 동일
            float wheelY = BoomRailTopY + 0.011f;   // 바퀴 축 중심 = 레일 윗면 + 트레드 반경(0.011) → 트레드 하단 접지(공유 상수 파생)
            foreach (float bx in new[] { TrolleyCX - TrolleyHX * 0.66f, TrolleyCX + TrolleyHX * 0.66f })   // 보기 X스테이션 ≈ 중심±0.096(끝 근처)
            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * railZ;
                float frameZ = gz - s * 0.015f;   // ≈0.105 — 보기 사이드프레임(저널 판), 레일 안쪽
                Box(trolley, "Trolley_Bogie", new Vector3(bx, wheelY, frameZ),
                    new Vector3(0.075f, 0.03f, 0.006f), CDark);
                foreach (float dwx in new[] { -0.028f, 0.028f })   // 보기당 2륜 — 트레드+가이드 플랜지+축
                {
                    float wx = bx + dwx;
                    Rod(trolley, "Trolley_Wheel", new Vector3(wx, wheelY, gz - 0.007f),
                        new Vector3(wx, wheelY, gz + 0.007f), 0.011f, CRail);          // 트레드(반경 0.011)
                    Rod(trolley, "Wheel_Flange", new Vector3(wx, wheelY, gz - s * 0.008f),
                        new Vector3(wx, wheelY, gz - s * 0.011f), 0.0145f, CRail);     // 안쪽 가이드 플랜지(이탈 방지)
                    Rod(trolley, "Wheel_Axle", new Vector3(wx, wheelY, gz),
                        new Vector3(wx, wheelY, frameZ), 0.003f, CDark);               // 축 → 사이드프레임
                }
                // 다운레그 — 보기(Y0.037) → 본체 윗면(Y0). 레일 안쪽(z0.10)
                Box(trolley, "Trolley_Leg", new Vector3(bx, 0.0185f, s * 0.10f),
                    new Vector3(0.012f, 0.037f, 0.012f), CTrolley);
            }
            //   '케이블 트롤리 내부 통과' 컨셉: 윗구간(HoistU)은 본체 안으로 인입(은폐), 양정 로프(Hoist_Rope)는 트롤리
            //   바닥에서 그대로 강하 → 노출 도르래 없음.

            // 데드엔드 소켓 — 육지쪽(x=-0.03) 로프 falls 2개를 트롤리에 정착(스펠터 소켓 몸체 + 클레비스 핀 + 바스켓)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 sk = new Vector3(HoistX - HoistSprX, -0.052f, sz * HoistSprZ);
                Box(trolley, "Rope_Socket_Body", sk, new Vector3(0.012f, 0.02f, 0.012f), CStruct);
                Rod(trolley, "Rope_Socket_Pin",
                    sk + new Vector3(-0.008f, 0.011f, 0f), sk + new Vector3(0.008f, 0.011f, 0f), 0.0025f, CDark);
                Cone(trolley, "Rope_Socket_Basket",
                    sk + new Vector3(0f, -0.016f, 0f), sk + new Vector3(0f, -0.004f, 0f), 0.003f, 0.007f, CDark);
            }
            // 운전실(운전석) — 현실 STS대로 스프레더의 '육지쪽(−X)'에 매달려 '바다쪽(+X=배)'을 향해 전면 경사창으로 발밑 화물을 내려다본다.
            BuildOperatorCab(trolley);

            // 비활성: 트롤리 주행 모터(사용자 요청 숨김) — git 이력 참조
            PbBox(trolley, "Trolley_FestoonBox", new Vector3(-0.06f, -0.06f, 0f),
                new Vector3(0.018f, 0.025f, 0.03f), CDark);
            // 트롤리 양끝 완충 버퍼(적색) — 본체 끝면(TrolleyCX±TrolleyHX)에서 4mm 돌출.
            for (int sx = -1; sx <= 1; sx += 2)
                PbBox(trolley, "Trolley_Bumper", new Vector3(TrolleyCX + sx * (TrolleyHX + 0.004f), -0.01f, 0f),
                    new Vector3(0.008f, 0.014f, 0.04f), CWarn);

            // 호이스트 윗구간(뒷면→백리치 앵커)은 BuildHoistUpper에서 동적(TrolleyReevingRig)으로 생성.
        }

        // 트롤리 본체 프레임화(Trolley 디테일 1) — 민짜 박스를 용접 프레임(코너 포스트·둘레 종재·리브·이음매)으로 분절.
        //   hx=본체 반길이X, cx=본체 X중심(육지쪽 이동) — 좌표 불변, 표면에 proud 부재만 추가.
        static void BuildTrolleyBodyFrame(Transform trolley, float hx, float cx)
        {
            float hz = 0.12f;   // 본체 반치수 Z — [rail-in-middle] 본체(tZ=0.24)·바퀴 z0.12에 맞춤
            float yTop = 0f, yBot = -0.05f, yMid = -0.025f, h = 0.05f;

            // 코너 포스트 4 — 본체 모서리 수직 트림
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Box(trolley, "TB_Corner", new Vector3(cx + sx * hx, yMid, sz * hz),
                    new Vector3(0.009f, h + 0.006f, 0.009f), CStruct);

            // 상·하 둘레 종재(긴 변 X + 짧은 변 Z)
            foreach (float y in new[] { yTop, yBot })
            {
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(trolley, "TB_Chord", new Vector3(cx, y, sz * hz),
                        new Vector3(2f * hx, 0.007f, 0.008f), CStruct);
                for (int sx = -1; sx <= 1; sx += 2)
                    Box(trolley, "TB_Chord", new Vector3(cx + sx * hx, y, 0f),
                        new Vector3(0.008f, 0.007f, 2f * hz), CStruct);
            }

            // 긴 측면(±Z) 수직 리브 — X 따라 다단(평탄한 긴 면 분절)
            int ribs = 8;
            for (int sz = -1; sz <= 1; sz += 2)
            for (int i = 1; i < ribs; i++)
            {
                float x = cx + Mathf.Lerp(-hx, hx, i / (float)ribs);
                Box(trolley, "TB_Rib", new Vector3(x, yMid, sz * (hz + 0.001f)),
                    new Vector3(0.006f, h, 0.005f), CTrolley);
            }

            // 단부(±X) 수직 리브 3 — 짧은 끝면 분절(Z 따라)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int i = -1; i <= 1; i++)
                Box(trolley, "TB_EndRib", new Vector3(cx + sx * (hx + 0.001f), yMid, i * hz * 0.55f),
                    new Vector3(0.005f, h, 0.006f), CTrolley);

            // 패널 이음매(가로) — 긴 ±Z 면 평탄함 제거
            for (int sz = -1; sz <= 1; sz += 2)
                Box(trolley, "TB_Seam", new Vector3(cx, yMid, sz * (hz + 0.0005f)),
                    new Vector3(2f * hx, 0.003f, 0.003f), CDark);
        }

        // 운전실 — 트롤리 하부 '육지쪽(−X)'에 매달려 '바다쪽(+X=배)'을 향해 전면 경사창으로 화물을 내려다본다.
        //   전면 하부 경사창+좌석/콘솔+측후면 도어+지붕+작업등+후면 플랫폼. 본체·헤드·로프소켓과 안 겹치게 y≤-0.052.

        // 통짜 강철 셸(CSG, 리플렉션 호출) — ProBuilder CSG/Model이 internal이라 리플렉션으로 Subtract 호출.
        //   임시 큐브를 cab-local 좌표로 빼서(CreatePrimitive) 결과 메시를 cab 자식으로 SetParent(false)해 정위치.
        static System.Reflection.MethodInfo _csgSub, _csgToMesh;
        static bool _csgResolved, _csgOk;
        static void ResolveCsg()
        {
            if (_csgResolved) return; _csgResolved = true;
            var csgT = System.Type.GetType("UnityEngine.ProBuilder.Csg.CSG, Unity.ProBuilder.Csg");
            var modelT = System.Type.GetType("UnityEngine.ProBuilder.Csg.Model, Unity.ProBuilder.Csg");
            if (csgT == null || modelT == null) return;
            var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            _csgSub = csgT.GetMethod("Subtract", bf, null, new[] { typeof(GameObject), typeof(GameObject) }, null);
            foreach (var m in modelT.GetMethods(bf))
                if (m.Name == "op_Explicit" && m.ReturnType == typeof(Mesh)) { _csgToMesh = m; break; }
            _csgOk = _csgSub != null && _csgToMesh != null;
            if (!_csgOk) Debug.LogWarning($"[OperatorCab] CSG 리플렉션 해석 실패 — Subtract={_csgSub != null}, op_Explicit→Mesh={_csgToMesh != null}");
        }

        static GameObject CabCsgShell(Transform cab, Vector3 outerC, Vector3 outerS,
                                      Vector3 cavC, Vector3 cavS,
                                      List<Vector3> cutC, List<Vector3> cutS, Color color)
        {
            ResolveCsg();
            if (!_csgOk) throw new System.Exception("ProBuilder CSG(reflection) 사용 불가");
            var mat = GetMaterial(color);
            var temps = new List<GameObject>();
            GameObject Cube(Vector3 c, Vector3 s)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
                go.transform.position = c; go.transform.localScale = s;
                var r = go.GetComponent<MeshRenderer>(); if (r != null) r.sharedMaterial = mat;
                temps.Add(go);
                return go;
            }
            GameObject FromMesh(Mesh m)
            {
                var go = new GameObject("csg_tmp");
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                temps.Add(go);
                return go;
            }
            Mesh ToMesh(object model) => (Mesh) _csgToMesh.Invoke(null, new[] { model });

            // 임시 큐브·중간 메시는 성공·실패·예외 어느 쪽이든 지운다(예외로 빠지면 csg_tmp 가 씬에 남았다).
            Mesh shellMesh = null;
            bool ok = false;
            try
            {
                GameObject body = Cube(outerC, outerS);
                object mdl = _csgSub.Invoke(null, new object[] { body, Cube(cavC, cavS) });
                for (int i = 0; i < cutC.Count; i++)
                {
                    body = FromMesh(ToMesh(mdl));
                    mdl = _csgSub.Invoke(null, new object[] { body, Cube(cutC[i], cutS[i]) });
                }
                shellMesh = ToMesh(mdl);
                shellMesh.name = "Cab_Shell_Mesh";
                // 메시 닫힘검사(수학적) — 정점 용접 후 삼각형 1개에만 속한 에지(경계/구멍) 수. watertight면 0.
                //   >0이면 CSG가 면 빠짐/뒤집힘(see-through) → throw → watertight 패널 셸로 폴백.
                int boundaryEdges = CountBoundaryEdges(shellMesh);
                if (boundaryEdges > 0)
                {
                    Debug.Log($"[OperatorCab] Cab_Shell 닫힘검사 실패 — verts={shellMesh.vertexCount}, tris={shellMesh.triangles.Length / 3}, boundaryEdges={boundaryEdges}(0이어야 watertight)");
                    throw new System.Exception($"CSG 셸 열림(구멍 {boundaryEdges}개) → watertight 패널 셸로 폴백");
                }

                var shell = new GameObject(Numbered("Cab_Shell"));
                shell.transform.SetParent(cab, false);
                shell.AddComponent<MeshFilter>().sharedMesh = shellMesh;
                var mr = shell.AddComponent<MeshRenderer>();
                int sc = Mathf.Max(1, shellMesh.subMeshCount);
                var arr = new Material[sc]; for (int i = 0; i < sc; i++) arr[i] = mat;
                mr.sharedMaterials = arr;
                ok = true;
                return shell;
            }
            finally
            {
                foreach (var t in temps)
                {
                    if (t == null) continue;
                    var mf = t.name == "csg_tmp" ? t.GetComponent<MeshFilter>() : null;
                    if (mf != null && mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
                    Object.DestroyImmediate(t);
                }
                if (!ok && shellMesh != null) Object.DestroyImmediate(shellMesh);
            }
        }

        // 메시 닫힘 검사 — 위치(1e-5 양자화)로 정점을 용접하고, 삼각형 1개에만 속한 에지(경계=구멍) 수를 반환.
        //   닫힌(watertight) 다양체면 모든 에지가 정확히 2개 삼각형에 공유되어 0. 면 빠짐/뒤집힘이 있으면 >0.
        static int CountBoundaryEdges(Mesh m)
        {
            var verts = m.vertices;
            var tris = m.triangles;
            var id = new int[verts.Length];
            var posMap = new Dictionary<string, int>();
            int next = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                string key = Mathf.RoundToInt(p.x * 100000f) + "_" + Mathf.RoundToInt(p.y * 100000f) + "_" + Mathf.RoundToInt(p.z * 100000f);
                if (!posMap.TryGetValue(key, out int vid)) { vid = next++; posMap[key] = vid; }
                id[i] = vid;
            }
            var edge = new Dictionary<long, int>();
            void Add(int a, int b)
            {
                int lo = a < b ? a : b, hi = a < b ? b : a;
                long e = ((long)lo << 32) | (uint)hi;
                edge.TryGetValue(e, out int c); edge[e] = c + 1;
            }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = id[tris[t]], b = id[tris[t + 1]], c = id[tris[t + 2]];
                if (a == b || b == c || c == a) continue;   // 퇴화 삼각형 무시
                Add(a, b); Add(b, c); Add(c, a);
            }
            int boundary = 0;
            foreach (var kv in edge) if (kv.Value == 1) boundary++;
            return boundary;
        }

        // CSG 실패(Experimental) 시 폴백 — 완전 watertight 6면 셸(패널 타일링). 개구(전면창·측면창·후면도어·바닥)는
        //   호출부 유리 좌표에 맞춰 4편 프레임으로 둘러쌈. 인접 면은 코너에서 솔리드 직교 중첩(틈 0).
        static void BuildCabBoxShellFallback(Transform cab, float hx, float hz, float cx,
                                             float floorY, float roofY, float floorTopY, float hdrY,
                                             float sillY, float gfBackX, float rim, Color bodyC)
        {
            float frontX = cx + hx, backX = cx - hx;      // -0.044 / -0.112
            float Zs = hz;                                 // 반폭 0.040
            float Yb = floorY, Yt = roofY + 0.003f;        // 바닥 -0.152 / 지붕 top -0.074
            const float t = 0.004f, tf = 0.006f, tr = 0.005f;
            float Xfp = frontX - t * 0.5f;                 // 전벽 평면 -0.046
            float Xbp = backX + t * 0.5f;                  // 후벽 평면 -0.110
            float Zsp = Zs - t * 0.5f;                     // 측벽 평면 ±0.038
            float Yfp = Yb + tf * 0.5f;                    // 바닥 평면
            float Yrp = Yt - tr * 0.5f;                    // 지붕 평면
            float D = frontX - backX;                      // 깊이 0.068

            // 지붕(솔리드)
            PbBox(cab, "Cab_Fb_Roof", new Vector3(cx, Yrp, 0f), new Vector3(D, tr, 2f * Zs), bodyC);

            // 바닥: 전방 lookdown 개구(X∈[gfBackX,frontX], Z∈±(hz-rim)) 둘러싼 프레임(좌석부 솔리드 + 측면 림)
            float gfz = hz - rim;                          // 0.036
            float rearW = gfBackX - backX;
            PbBox(cab, StsPartNames.CabFloorRear, new Vector3((backX + gfBackX) * 0.5f, Yfp, 0f), new Vector3(rearW, tf, 2f * Zs), bodyC);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_Fb_FloorRim", new Vector3((gfBackX + frontX) * 0.5f, Yfp, sz * (Zs + gfz) * 0.5f), new Vector3(frontX - gfBackX, tf, Zs - gfz), bodyC);

            // 전벽(X=Xfp): 전면창(Y∈[fwBot,hdrY], Z∈±0.9hz) 둘러싼 헤더/실/좌·우 코너기둥
            float fwBot = floorTopY + 0.002f, fwTop = hdrY, fwHz = hz * 0.90f;
            PbBox(cab, "Cab_Fb_FrontHdr",  new Vector3(Xfp, (fwTop + Yt) * 0.5f, 0f), new Vector3(t, Yt - fwTop, 2f * Zs), bodyC);
            PbBox(cab, "Cab_Fb_FrontSill", new Vector3(Xfp, (Yb + fwBot) * 0.5f, 0f), new Vector3(t, fwBot - Yb, 2f * Zs), bodyC);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_Fb_FrontPost", new Vector3(Xfp, (fwBot + fwTop) * 0.5f, sz * (Zs + fwHz) * 0.5f), new Vector3(t, fwTop - fwBot, Zs - fwHz), bodyC);

            // 후벽(X=Xbp): 도어 개구(Z∈±0.012, Y∈[floorTopY, +0.82·bwH]) 둘러싼 헤더/실/좌·우 잼
            float bwH = hdrY - floorTopY;
            float dwb = floorTopY, dwt = floorTopY + bwH * 0.82f, dwHz = 0.012f;
            PbBox(cab, "Cab_Fb_BackHdr",  new Vector3(Xbp, (dwt + Yt) * 0.5f, 0f), new Vector3(t, Yt - dwt, 2f * Zs), bodyC);
            PbBox(cab, "Cab_Fb_BackSill", new Vector3(Xbp, (Yb + dwb) * 0.5f, 0f), new Vector3(t, dwb - Yb, 2f * Zs), bodyC);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_Fb_BackJamb", new Vector3(Xbp, (dwb + dwt) * 0.5f, sz * (Zs + dwHz) * 0.5f), new Vector3(t, dwt - dwb, Zs - dwHz), bodyC);

            // 측벽(Z=±Zsp)×2: 측면창(X∈[-0.100,-0.048], Y∈[sillY,hdrY]) 둘러싼 헤더/실/전·후 잼
            float swX0 = -0.100f, swX1 = -0.048f, swYb = sillY, swYt = hdrY;
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float z = sz * Zsp;
                PbBox(cab, "Cab_Fb_SideHdr",   new Vector3(cx, (swYt + Yt) * 0.5f, z), new Vector3(D, Yt - swYt, t), bodyC);
                PbBox(cab, "Cab_Fb_SideSill",  new Vector3(cx, (Yb + swYb) * 0.5f, z), new Vector3(D, swYb - Yb, t), bodyC);
                PbBox(cab, "Cab_Fb_SideJambF", new Vector3((swX1 + frontX) * 0.5f, (swYb + swYt) * 0.5f, z), new Vector3(frontX - swX1, swYt - swYb, t), bodyC);
                PbBox(cab, "Cab_Fb_SideJambB", new Vector3((backX + swX0) * 0.5f, (swYb + swYt) * 0.5f, z), new Vector3(swX0 - backX, swYt - swYb, t), bodyC);
            }
        }

        static void BuildOperatorCab(Transform trolley)
        {
            const float mountTopY = -0.05f;   // 통합 마운트 상단 Y = 트롤리 박스 하단(cab-local)
            // 육지쪽 매달림 — 운전실은 스프레더의 '육지쪽(−X)'에 있고 운전자가 '바다쪽(+X=배)'의 화물을 내려다본다.
            //   홀더 회전 identity(캡 Z대칭이라 180°Y와 동일 실루엣) + 평행이동 −0.018로 배치.
            Transform cab = new GameObject(StsPartNames.OperatorCab).transform;
            cab.SetParent(trolley, false);
            cab.localRotation = Quaternion.identity;
            // 클리어런스 평행이동도 거울 반전: 스프레더 헤드블록(±0.045)·플랜지(±0.054)와 X그림자 겹침 회피를
            //   육지쪽(−X)으로 0.018 이동해 동일하게 유지(거울상이므로 부호만 반대).
            const float CabLandwardShift = 0.018f;
            cab.localPosition = new Vector3(-CabLandwardShift, 0f, 0f);
            // OPERATOR CAB — CSG 통짜 강철 셸 유지(폐기 금지). cab-local x<0(미러 전), +X=전방(스프레더).
            //   절단 면이 공동 면과 동일평면(coplanar)이면 BSP가 면을 떨군다 — 각 cut을 서로 다른 미소량으로 분리(아래).
            float hx = 0.034f, hz = 0.040f, cx = -0.078f;       // 반깊이/반폭, 중심 X (cab-local; 홀더 identity → 트롤리 −X=육지쪽에 위치)
            float roofY = -0.077f, floorY = -0.152f;            // 지붕 top=-0.074<-0.0725; 높이 0.075(~1.8m)
            float frontX = cx + hx, backX = cx - hx;            // 전 -0.044, 후 -0.112 (둘 다 <0)
            float floorTopY = floorY + 0.005f;                  // 구조 바닥 윗면(밟는 면)
            float gfBackX   = cx + 0.010f;                      // 글래스 플로어 뒤 경계(좌석 앞) = -0.068
            float sillY = floorY + 0.024f;                      // 전면 무릎 transom(=-0.128) — 아래는 그린하우스 유리
            float hdrY  = roofY  - 0.010f;                      // 창 밴드 상단(헤더 하단) = -0.087
            Color frame = CDark, bodyC = CStruct, glass = CGlass;
            Color shellC = CStruct;

            const float rim = 0.004f, wt = 0.004f;
            float roofTopY = roofY + 0.003f;                                   // 지붕 윗면 -0.074 (<-0.0725)
            float gfCx = (gfBackX + frontX) * 0.5f, gfW = frontX - gfBackX;
            float ubY = (sillY + hdrY) * 0.5f, ubH = hdrY - sillY;
            float bwH = hdrY - floorTopY;
            float fwBot = floorTopY + 0.002f;                                  // 전면창 하단(바닥 직전까지)
            // CSG watertight: 절단 면이 공동 면과 동일평면이면 BSP가 면을 떨군다.
            //   공동 top=hdrY(-0.087)·bottom=floorTopY(-0.147) — 아래 4값으로 각 절단을 서로 다른 hidden 면으로 분리.
            float fwTopCut  = hdrY + 0.0020f;                                  // 전면창 top -0.0850 (헤더밴드 -0.0895..-0.0845 안)
            float swTopCutP = hdrY + 0.0015f, swTopCutN = hdrY + 0.0010f;      // 측면창 +Z/-Z top -0.0855 / -0.0860 (서로·공동top과 비동일평면)
            float doorBot   = floorTopY - 0.0015f;                             // 도어 bottom -0.1485 (바닥솔리드 -0.152..-0.147 안에 묻힘)
            float doorTop   = floorTopY + bwH * 0.82f;                         // 도어 top -0.0978 (불변)
            var cutC = new List<Vector3>();
            var cutS = new List<Vector3>();
            cutC.Add(new Vector3(frontX, (fwBot + fwTopCut) * 0.5f, 0f));      // 1) 전면창(지배 요소 — 폭 0.90·코너 기둥 자리만 남김)
            cutS.Add(new Vector3(wt * 4f, fwTopCut - fwBot, 2f * hz * 0.90f));
            for (int sz = -1; sz <= 1; sz += 2)                                // 2) 측면창 ×2(top 좌우 다르게 → 비동일평면)
            {
                float swTop = sz > 0 ? swTopCutP : swTopCutN;
                cutC.Add(new Vector3(-0.074f, (sillY + swTop) * 0.5f, sz * hz));
                cutS.Add(new Vector3(0.048f, swTop - sillY, wt * 4f));
            }
            cutC.Add(new Vector3(backX, (doorBot + doorTop) * 0.5f, 0f));      // 3) 후면 도어 개구(bottom을 floorTopY 아래로 내려 비동일평면)
            cutS.Add(new Vector3(wt * 4f, doorTop - doorBot, 0.024f));
            cutC.Add(new Vector3(gfCx - 0.001f, floorY + 0.0025f, 0f));        // 4) 바닥 lookdown 개구
            cutS.Add(new Vector3(gfW - 0.002f, wt * 4f, 2f * (hz - rim)));

            GameObject shell;
            try
            {
                shell = CabCsgShell(cab,
                    new Vector3(cx, (floorY + roofTopY) * 0.5f, 0f), new Vector3(2f * hx, roofTopY - floorY, 2f * hz),                 // 외곽 솔리드
                    new Vector3(cx, (floorTopY + hdrY) * 0.5f, 0f), new Vector3(2f * hx - 2f * wt, hdrY - floorTopY, 2f * hz - 2f * wt), // 내부 공동
                    cutC, cutS, shellC);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[OperatorCab] CSG 셸 실패 → 박스 셸 폴백: " + e);
                shell = null;
            }
            if (shell == null)
                BuildCabBoxShellFallback(cab, hx, hz, cx, floorY, roofY, floorTopY, hdrY, sillY, gfBackX, rim, shellC);

            // ---- 유리(개구에 끼움; 가장자리를 살짝 키워 벽 두께 뒤로 숨김 → 동일평면 없음) ----
            // 바닥 유리(개구 안쪽에 tuck — 캡 밖 돌출 제거). X∈[-0.069,-0.047](개구 -0.068..-0.046 안), Z 살짝 키워 가장자리 숨김.
            PbBox(cab, "Cab_GlassFloor", new Vector3(-0.058f, floorY + 0.0025f, 0f), new Vector3(0.022f, 0.0015f, 2f * (hz - rim) + 0.004f), glass);
            // 전면 유리(평면, 개구에 flush) + 깔끔한 평면 차양 — v7: 캔티드 wedge 제거(언더사이드 돌출·유리 중첩이 지저분 → 뒷면 수준으로 정리).
            PbBox(cab, "Cab_GlassFront", new Vector3(frontX - wt * 0.5f, (fwBot + hdrY) * 0.5f, 0f), new Vector3(0.002f, (hdrY - fwBot) + 0.004f, 2f * hz * 0.90f + 0.004f), glass);
            PbBox(cab, "Cab_Visor", new Vector3(frontX + 0.005f, hdrY + 0.003f, 0f), new Vector3(0.012f, 0.004f, 2f * hz + 0.006f), frame); // 평면 차양(전방 12mm, 비돌출·비경사)
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_GlassSide", new Vector3(-0.074f, ubY, sz * (hz - wt * 0.5f)), new Vector3(0.048f + 0.004f, ubH + 0.004f, 0.002f), glass);

            // ---- 프레임(슬림 다크): 4 코너기둥 + 헤더 + 지붕캡. v4: 오렌지 바이저/허리선·멀리언·플로어격자 제거(노이즈) ----
            {
                float postBot = ubY - (bwH + 0.02f) * 0.5f;        // -0.1475 (하단 유지)
                float postTop = roofTopY;                           // -0.074: 클리어런스 -0.0725 준수(상단 클램프)
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    PbBox(cab, "Cab_Post", new Vector3(cx + sx * hx, (postBot + postTop) * 0.5f, sz * hz), new Vector3(0.005f, postTop - postBot, 0.005f), frame);   // 코너 기둥(top=roofTopY로 클램프)
            }
            PbBox(cab, "Cab_HeaderRail", new Vector3(cx, hdrY, 0f), new Vector3(2f * hx - 0.004f, 0.005f, 2f * hz + 0.006f), frame); // 헤더 밴드(앞·옆 proud, 양끝 벽에 묻힘)
            PbBox(cab, "Cab_Roof", new Vector3(cx, roofTopY - 0.0013f, 0f), new Vector3(2f * hx + 0.012f, 0.005f, 2f * hz + 0.012f), frame); // 지붕 캡 top = 셸 지붕보다 proud(z-fight 제거, 클리어런스 준수)

            // ---- 후면 도어 리프(개구에 proud로 끼움) + 소창 ----
            PbBox(cab, "Cab_Door",       new Vector3(backX - 0.002f,  floorTopY + bwH * 0.41f, 0f),     new Vector3(0.004f, bwH * 0.82f, 0.024f), frame);
            PbBox(cab, "Cab_DoorWindow", new Vector3(backX - 0.0045f, floorTopY + bwH * 0.60f, 0f),     new Vector3(0.002f, bwH * 0.30f, 0.016f), glass);

            //  후면 외장 의장 — "뒤에 문밖에 없다" → 실제 STS 뒤편 의장.
            //  접근 발판(그레이팅)+안전난간(노랑)+도어 그랩봉+공조(HVAC). 전부 후벽 뒤(X<-0.112)로 돌출/벽에 묻어 z-fight 0. 좌표 산식.
            {
                float pBackX = -0.136f, pWallX = -0.111f, pZ = 0.028f;            // 발판 후방/벽쪽 X(앞단 1mm 벽에 묻음), 반폭 Z
                float pTopY = floorY + 0.003f;                                    // 발판 윗면 -0.149 (≈문턱)
                float railTop = floorY + 0.030f, railMid = floorY + 0.017f;       // 난간 상/중
                // 1) 후면 접근 발판(그레이팅)
                PbBox(cab, "Cab_RearDeck", new Vector3((pBackX + pWallX) * 0.5f, pTopY - 0.002f, 0f), new Vector3(pWallX - pBackX, 0.004f, 2f * pZ + 0.004f), CRail);
                // 2) 안전 난간(노랑, ㄷ자: 후방+양측, 문쪽 개방) — 기둥4 + 상/중 가로봉2
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Rod(cab, "Cab_RailPost", new Vector3(pBackX, pTopY, sz * pZ), new Vector3(pBackX, railTop, sz * pZ), 0.0016f, CSafety);
                    Rod(cab, "Cab_RailPost", new Vector3(pWallX, pTopY, sz * pZ), new Vector3(pWallX, railTop, sz * pZ), 0.0016f, CSafety);
                }
                for (int lvl = 0; lvl < 2; lvl++)
                {
                    float ry = lvl == 0 ? railTop : railMid;
                    Rod(cab, "Cab_RailBack", new Vector3(pBackX, ry, -pZ), new Vector3(pBackX, ry, pZ), 0.0016f, CSafety);
                    for (int sz = -1; sz <= 1; sz += 2)
                        Rod(cab, "Cab_RailSide", new Vector3(pBackX, ry, sz * pZ), new Vector3(pWallX, ry, sz * pZ), 0.0016f, CSafety);
                }
                // 코너 마감 — 둥근 관 조인트는 노드마다 구로 막음(Rb≈1.5r). 후방 코너=3-way, 벽쪽=2-way.
                {
                    float jD = 0.0032f;   // 조인트 구 지름 = 정확히 2r(=관굵기, r=0.0016) — 안 부풀고 틈만 메움.
                    for (int sz = -1; sz <= 1; sz += 2)
                    for (int lvl = 0; lvl < 2; lvl++)
                    {
                        float ry = lvl == 0 ? railTop : railMid;
                        Ball(cab, "Cab_RailJoint", new Vector3(pBackX, ry, sz * pZ), new Vector3(jD, jD, jD), CSafety);
                        Ball(cab, "Cab_RailJoint", new Vector3(pWallX, ry, sz * pZ), new Vector3(jD, jD, jD), CSafety);
                    }
                }
                // 3) 도어 양옆 수직 그랩봉(노랑) — U핸들: 수직봉 + 상/하 벽 리턴 + 굽힘 구 마감(개방 끝 제거)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float gx = backX - 0.004f, gz = sz * 0.018f, gyb = floorTopY + 0.004f, gyt = floorTopY + 0.040f;
                    Rod(cab,  "Cab_DoorGrab",    new Vector3(gx, gyb, gz), new Vector3(gx, gyt, gz), 0.0015f, CSafety);
                    Rod(cab,  "Cab_DoorGrabRet", new Vector3(gx, gyt, gz), new Vector3(backX, gyt, gz), 0.0014f, CSafety);
                    Rod(cab,  "Cab_DoorGrabRet", new Vector3(gx, gyb, gz), new Vector3(backX, gyb, gz), 0.0014f, CSafety);
                    Ball(cab, "Cab_DoorGrabBend", new Vector3(gx, gyt, gz), new Vector3(0.003f, 0.003f, 0.003f), CSafety);   // 굽힘 구 = 정확히 2r(=관굵기, r=0.0015)
                    Ball(cab, "Cab_DoorGrabBend", new Vector3(gx, gyb, gz), new Vector3(0.003f, 0.003f, 0.003f), CSafety);
                }
                // 4) 공조(HVAC) 유닛 + 그릴 — 도어 위(Z=0) 벽에 묻고 뒤로 돌출.
                float hvacY = (floorTopY + bwH * 0.82f + roofY + 0.003f) * 0.5f;   // 도어상단(-0.0978)~지붕top(-0.074) 중간 = -0.0859
                PbBox(cab, "Cab_RearHVAC",        new Vector3(backX - 0.006f,  hvacY, 0f), new Vector3(0.014f, 0.018f, 0.030f), CMachine);  // Z∈[-0.015,0.015] → 그랩봉 ±0.018 밖
                PbBox(cab, "Cab_RearHVAC_Grille", new Vector3(backX - 0.0135f, hvacY, 0f), new Vector3(0.002f, 0.014f, 0.024f), frame);
            }

            // 운전실 통합 마운트(Option C) — 트롤리 박스 하단(Y=-0.05)에서 운전실 지붕(-0.074)까지
            //   짧은 수직 포스트 4개 + 하부 결합 종재로 직결(운전실 4코너가 박스 풋프린트 안에 있어 안착).
            {
                float mountBotY = roofTopY - 0.001f;      // 운전실 지붕 결합부 -0.075 (1mm 묻힘)
                float bt = 0.006f;                        // 종재 단면 t — 코너 솔리드 가로질러 덮기
                // 수직 마운트 포스트 4 (front/back × 좌우) — 굵은 box
                for (int sx = -1; sx <= 1; sx += 2)       // sx<0: 후면(backX, 육지끝) / sx>0: 전면(frontX, 바다측)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float px = (sx < 0) ? backX : frontX;
                    PbBox(cab, "Cab_Mount_Post", new Vector3(px, (mountTopY + mountBotY) * 0.5f, sz * hz),
                        new Vector3(0.010f, mountTopY - mountBotY + bt, 0.010f), CStruct);   // 위 +t/2 박스 하단에 묻힘
                }
                // 전·후 결합 종재(Z방향) — 포스트 머리를 가로질러 덮어 박스 하단에 결합.
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    float px = (sx < 0) ? backX : frontX;
                    PbBox(cab, "Cab_Mount_Tie", new Vector3(px, mountTopY - 0.003f, 0f),
                        new Vector3(0.012f, 0.006f, 2f * hz + bt), CStruct);
                }
                // 좌·우 결합 종재(X방향) — 전후 포스트를 잇는 하부 둘레
                for (int sz = -1; sz <= 1; sz += 2)
                    PbBox(cab, "Cab_Mount_Tie", new Vector3((frontX + backX) * 0.5f, mountTopY - 0.003f, sz * hz),
                        new Vector3((frontX - backX) + bt, 0.006f, 0.012f), CStruct);
            }

            // ---- 내부(좌석+콘솔+모니터) — 유리 너머로 보임 ----
            float seatX = cx + 0.004f, seatY = floorY + 0.014f;
            PbBox(cab, "Cab_SeatPedestal", new Vector3(seatX - 0.002f, floorY + 0.007f, 0f), new Vector3(0.008f, 0.014f, 0.010f), bodyC);
            PbBox(cab, "Cab_Seat",      new Vector3(seatX, seatY, 0f),                   new Vector3(0.016f, 0.006f, 0.018f), frame);
            PbBox(cab, "Cab_SeatBack",  new Vector3(seatX - 0.010f, seatY + 0.016f, 0f), new Vector3(0.005f, 0.028f, 0.018f), frame);
            PbBox(cab, "Cab_HeadRest",  new Vector3(seatX - 0.009f, seatY + 0.034f, 0f), new Vector3(0.005f, 0.008f, 0.012f), frame);
            for (int sz = -1; sz <= 1; sz += 2)   // 좌우 콘솔만(조이스틱/암레스트 잔부품은 증분2)
                PbBox(cab, "Cab_Console", new Vector3(seatX + 0.012f, seatY + 0.006f, sz * 0.014f), new Vector3(0.014f, 0.007f, 0.007f), bodyC);
            PbBox(cab, "Cab_Monitor",       new Vector3(seatX + 0.020f, seatY + 0.016f, 0f), new Vector3(0.004f, 0.010f, 0.013f), bodyC);
            PbBox(cab, "Cab_MonitorScreen", new Vector3(seatX + 0.0222f, seatY + 0.016f, 0f), new Vector3(0.001f, 0.008f, 0.011f), CLight);

            // 운전실 시점 앵커 — VR 카메라가 여기로 정렬(StsCraneVRController가 'Cab_Viewpoint' 최우선 앵커).
            //   수평(피치 0) 유지 — 강제 피치를 넣으면 HMD 수평선과 어긋나 멀미 유발. 발밑은 고개 숙여 본다.
            var viewpoint = new GameObject(StsPartNames.CabViewpoint).transform;
            viewpoint.SetParent(cab, false);
            viewpoint.localPosition = new Vector3(cx + 0.018f, floorY + 0.044f, 0f);
            viewpoint.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);   // 수평 전방(+X) — 멀미 방지

            // v4 보류: 노즈 작업등·후면 안테나 등 외장 잔부품 제거 → 실루엣 확정 후 증분2에서 선별 재도입.
        }
    }
}
#endif
