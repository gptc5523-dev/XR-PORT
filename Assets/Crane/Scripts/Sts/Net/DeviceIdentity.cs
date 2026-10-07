using UnityEngine;
using UnityEngine.XR;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>이 인스턴스가 어느 자리·기기인가 — 서버 감시가 넘긴 -slot N·-device MAC, 없으면 헤드셋 일련번호. 사용자 등록의 기기 ID.</summary>
    public sealed class DeviceIdentity : MonoBehaviour
    {
        /// <summary>VR 자리 번호(1~5), 모르면 0.</summary>
        public static int Slot { get; private set; }
        /// <summary>기기 ID — "MAC:aa:bb:…" 또는 "SN:일련번호", 모르면 빈 문자열.</summary>
        public static string DeviceId { get; private set; } = "";
        /// <summary>헤드셋이 알려 준 이름·제조사·일련번호(WiVRn 이 넘기는지 확인용).</summary>
        public static string Headset { get; private set; } = "";

        /// <summary>서버에 등록된 내 정보(GET /users) — 미등록·미조회면 null(조회 끝났는지는 Looked).</summary>
        public static User Me { get; private set; }
        public static bool Looked { get; private set; }

        [System.Serializable] public sealed class User { public string device_id, device_type, device_label, name, position, department, role, color, memo; public int can_host; }
        [System.Serializable] sealed class UserList { public User[] users; }

        bool headLogged;
        float next, nextLookup;
        System.Threading.Tasks.Task<string> lookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<DeviceIdentity>("Device");

        void Awake()
        {
            int.TryParse(Arg("-slot"), out int s);
            Slot = s;
            string mac = Arg("-device");
            DeviceId = string.IsNullOrEmpty(mac) ? "" : "MAC:" + mac.ToLowerInvariant();
            Debug.Log($"[Device] 자리 {(Slot > 0 ? "VR-" + Slot : "모름")} · 기기 {(DeviceId == "" ? "모름" : DeviceId)}");
        }

        void Update()
        {
            LookupStep();
            if (headLogged || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return;
            headLogged = true;
            Headset = $"{head.name} / {head.manufacturer} / {head.serialNumber}";
            if (DeviceId == "" && !string.IsNullOrEmpty(head.serialNumber)) DeviceId = "SN:" + head.serialNumber;
            Debug.Log($"[Device] 헤드셋 {Headset} → 기기 {(DeviceId == "" ? "모름" : DeviceId)}");
        }

        // 통합서버 /users 에서 내 기기 ID, 없으면 자리(SLOT:N)로 찾는다 — 서버가 붙은 PLC 브리지의 연결을 빌려 5초마다 될 때까지.
        void LookupStep()
        {
            if (Looked) return;
            if (lookup == null)
            {
                if (Time.unscaledTime < nextLookup) return;
                nextLookup = Time.unscaledTime + 5f;
                foreach (var b in FindObjectsByType<Plc.PlcBridge>(FindObjectsSortMode.None))
                    if (b.Source is Plc.ServerPlcSource src && src.IsConnected) { lookup = src.GetText("/users"); break; }
                return;
            }
            if (!lookup.IsCompleted) return;
            var t = lookup;
            lookup = null;
            if (t.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) return;
            var list = JsonUtility.FromJson<UserList>(t.Result)?.users ?? new User[0];
            string slotKey = Slot > 0 ? "SLOT:" + Slot : null;
            foreach (var u in list) if (DeviceId != "" && u.device_id == DeviceId) Me = u;
            if (Me == null) foreach (var u in list) if (u.device_id == slotKey) Me = u;
            Looked = true;
            Debug.Log(Me != null ? $"[Device] 등록 사용자 {Me.name} · {Me.position} · {Me.role} ({Me.device_id})" : $"[Device] 미등록 기기 (등록 {list.Length}명 중 없음)");
        }

        /// <summary>아바타에 실어 보내는 원문 "구분|소속|직급|이름" — 구분은 VR-N 또는 관전. 표시는 TagText·LineText.</summary>
        public static string Label =>
            string.Join("|", Slot > 0 ? $"VR-{Slot}" : "관전", Me?.department ?? "", Me?.position ?? "", Me?.name ?? "");

        /// <summary>머리 위 이름표 — "관전\n소속 직급 이름", 미등록이면 둘째 줄이 "미등록".</summary>
        public static string TagText(string raw) { var p = Split(raw); return $"{p[0]}\n{Info(p, " ")}"; }

        /// <summary>참가자 목록 한 줄 — "관전  소속 직급 이름".</summary>
        public static string LineText(string raw) { var p = Split(raw); return $"{p[0]}  {Info(p, " ")}"; }

        static string[] Split(string raw)
        {
            var p = (raw ?? "").Split('|');
            return p.Length == 4 ? p : new[] { string.IsNullOrEmpty(raw) ? "참가자" : raw, "", "", "" };
        }

        static string Info(string[] p, string sep)
        {
            string s = string.Join(sep, System.Array.FindAll(new[] { p[1], p[2], p[3] }, x => x.Length > 0));
            return s.Length > 0 ? s : "미등록";
        }

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], name, System.StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }
    }
}
