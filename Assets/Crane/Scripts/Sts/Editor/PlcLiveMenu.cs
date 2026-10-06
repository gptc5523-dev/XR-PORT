#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using AIXRCrane.Crane.Sts.Plc;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>라이브 PLC 모드 메뉴 — 켜기·로컬 서버 지정, 그리고 씬 컨테이너 자리를 PLC 좌표(실척 m)로 내보내 PlcSim/live.py 가 읽게 한다.</summary>
    static class PlcLiveMenu
    {
        const string ModePath = "PLC/라이브 PLC 모드 (켜기·끄기)", LocalPath = "PLC/라이브 PLC — 로컬 서버(127.0.0.1:5006)";
        const string LocalUrl = "http://127.0.0.1:5006";

        [MenuItem(ModePath, false, 30)]
        static void ToggleMode() => EditorPrefs.SetBool(PlcLiveMode.EditorPrefKey, !EditorPrefs.GetBool(PlcLiveMode.EditorPrefKey, false));

        [MenuItem(ModePath, true)]
        static bool CheckMode() { Menu.SetChecked(ModePath, EditorPrefs.GetBool(PlcLiveMode.EditorPrefKey, false)); return true; }

        [MenuItem(LocalPath, false, 31)]
        static void ToggleLocal()
        {
            if (EditorPrefs.GetString(PlcLiveMode.EditorUrlKey, "") == LocalUrl) EditorPrefs.DeleteKey(PlcLiveMode.EditorUrlKey);
            else EditorPrefs.SetString(PlcLiveMode.EditorUrlKey, LocalUrl);
        }

        [MenuItem(LocalPath, true)]
        static bool CheckLocal() { Menu.SetChecked(LocalPath, EditorPrefs.GetString(PlcLiveMode.EditorUrlKey, "") == LocalUrl); return true; }

        // 플레이 중 — 크레인마다 '스프레더 기준점이 컨테이너 윗면 중심에 오는 PLC 값'을 잰다. 축 역산은 PlcBridge.WorldAtPose 의 역(같은 식).
        [MenuItem("PLC/라이브 PLC 배치 내보내기 (플레이 중)", false, 32)]
        public static void Export()
        {
            if (!Application.isPlaying) { Debug.LogError("[PlcLive] 플레이 중에만 — 축 범위는 런타임에 정해진다."); return; }
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\"cont_h_m\":2.591,\"cranes\":[");
            bool firstCrane = true;
            int total = 0;
            foreach (var c in Object.FindObjectsByType<StsCrane>(FindObjectsSortMode.InstanceID))
            {
                var bridge = c.GetComponent<PlcBridge>();
                var grabber = c.GetComponent<SpreaderGrabber>();
                if (bridge == null || !bridge.RangesResolved || grabber == null || c.Gantry == null || c.Trolley == null || c.Spreader == null)
                {
                    Debug.LogWarning($"[PlcLive] {c.name} 건너뜀 — PlcBridge 범위 미산출(라이브 모드로 플레이 중인지 확인)");
                    continue;
                }
                var sway = CraneSway.Of(c);
                Vector3 gp = grabber.GrabPoint() - (sway != null ? sway.Offset : Vector3.zero);
                Vector3 R = bridge.RangesM;
                bool rtg = c.GetComponent<RtgBogieSteering>() != null;
                sb.Append(firstCrane ? "" : ",").Append($"{{\"name\":\"{c.name}\",\"kind\":\"{(rtg ? "RTG" : "STS")}\",");
                sb.Append(string.Format(inv, "\"range\":{{\"gt\":{0:F3},\"tr\":{1:F3},\"ho\":{2:F3} }},", R.x, R.y, R.z));
                sb.Append(string.Format(inv, "\"home\":{{\"gt\":{0:F3},\"tr\":{1:F3},\"ho\":{2:F3} }},\"containers\":[",
                    Plc(c.Gantry, R.x, c.Gantry.Current), Plc(c.Trolley, R.y, c.Trolley.Current), Plc(c.Spreader, R.z, c.Spreader.Current)));
                firstCrane = false;
                bool first = true;
                foreach (var ct in PlcCargoFollow.Candidates())
                {
                    if (ct.IsChildOf(c.transform) || !SceneUtil.TryBounds(ct, out var b)) continue;
                    var top = new Vector3(b.center.x, b.max.y, b.center.z);
                    float gt = Plc(c.Gantry, R.x, Target(c.Gantry, top, gp)), tr = Plc(c.Trolley, R.y, Target(c.Trolley, top, gp)), ho = Plc(c.Spreader, R.z, Target(c.Spreader, top, gp));
                    if (gt < 0f || gt > R.x || tr < 0f || tr > R.y || ho < 0f || ho > R.z) continue;   // 이 크레인이 닿지 않는다
                    float longM = Mathf.Max(b.size.x, b.size.z) / c.ModelScale;
                    int ft = longM > 13f ? 45 : longM > 9f ? 40 : 20;
                    var inst = ct.GetComponentInParent<ContainerInstance>();   // 하중은 SpreaderAttach 와 같은 출처 — 화면 하중과 PLC HO_Load 가 맞게
                    float tons = inst != null && inst.LoadTons > 0f ? inst.LoadTons : ContainerLoad.WeightTons(ct.name);
                    sb.Append(first ? "" : ",").Append(string.Format(inv, "{{\"id\":\"{0}\",\"gt\":{1:F3},\"tr\":{2:F3},\"ho_top\":{3:F3},\"ft\":{4},\"t\":{5:F1},\"h\":{6:F3} }}",
                        ct.name, gt, tr, ho, ft, tons, b.size.y / c.ModelScale));
                    first = false;
                    total++;
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PlcSim", "live_layout.json");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[PlcLive] 배치 내보냄 — 컨테이너 {total}개 → {path}");
        }

        // 기준점 gp 를 점 p 로 옮기는 축 값 — 축은 WorldAxis(축 단위당 월드 변위) 방향 성분만 맡는다.
        static float Target(IAxisMover a, Vector3 p, Vector3 gp) => a.Current + Vector3.Dot(p - gp, a.WorldAxis) / a.WorldAxis.sqrMagnitude;

        // 축 값 → PLC 실척 m (PlcBridge.DriveAxis 의 역: realPos / rangeM = 정규화 위치).
        static float Plc(IAxisMover a, float rangeM, float v) => (v - a.Min) / (a.Max - a.Min) * rangeM;
    }
}
#endif
