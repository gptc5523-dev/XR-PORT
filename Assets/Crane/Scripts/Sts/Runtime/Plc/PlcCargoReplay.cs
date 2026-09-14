using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Container.Crane.Sts.Plc
{
    /// <summary>
    /// PLC 재생에 화물을 붙인다 — PlcBridge 는 축만 움직여서 스프레더가 빈손으로 오갔다.
    ///
    /// 재생 CSV 옆의 작업 이력(run_NN.history.csv, PlcSim/generate.py)을 읽어
    ///   ① 출발지에 컨테이너를 놓는다 — 자리는 이력의 집기 시각(pick_t_ms) PLC 자세에서 스프레더 콘 바닥이 오는 곳
    ///      (PlcBridge.WorldAtPose). 슬롯 좌표를 여기서 다시 계산하지 않으니 생성기와 어긋날 일이 없다.
    ///   ② PLC 트위스트락 잠금(SP_TwistLock_Locked) 상승에서 집고, 하강에서 놓는다.
    ///   ③ 섀시(트럭)엔 쌓이지 않는다 — 섀시에 놓은 건 트럭이 싣고 떠나고(truckLeaveS 뒤 제거),
    ///      섀시에서 집는 건 직전 작업을 놓는 순간 트럭이 들여온다.
    /// CSV 가 되감기면 전부 치우고 처음부터 다시 놓는다.
    ///
    /// 잡기는 SpreaderGrabber.Grab 을 쓰지 않는다 — 코너 안착 게이트가 PLC 목표 산포와 겹치고,
    /// 이미 PLC 자세 그대로 놓았으니 월드 자세를 보존한 채 SpreaderAttach 에 붙이기만 하면 된다.
    /// 모양은 씬 야드의 Cont40_00 / Cont20_00 을 복제한다(스케일·LOD·머티리얼 그대로). 없으면 ISO 치수 박스.
    ///
    /// 가설 구현·Unity Play 미검증.
    /// </summary>
    [DefaultExecutionOrder(-90)]   // PlcBridge(-100)가 축을 옮긴 뒤 · SpreaderGrabber(50) 클램프 전
    [AddComponentMenu("Container/STS Crane/PLC 화물 재생 (작업 이력)")]
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class PlcCargoReplay : MonoBehaviour
    {
        [Tooltip("섀시(트럭)에 놓은 컨테이너가 실려 떠나기까지(초).")]
        [SerializeField] float truckLeaveS = 3f;

        struct Move { public string id, from, to; public bool ft40; public float pickS, placeS; }

        const string Chassis = "CHASSIS";
        StsCrane crane;
        PlcBridge bridge;
        SpreaderGrabber grabber;
        SpreaderLockAnimator lockAnim;
        SpreaderTelescope telescope;
        RtgSpreaderTelescope rtgTele;
        CsvReplaySource csv;
        Move[] moves;
        Transform[] boxes;
        float grabDrop;   // 트위스트락 중심 → 스프레더 최저점(콘 바닥). 컨테이너 윗면이 여기 온다.
        int next;         // 다음에 집을 이동
        int held = -1;    // 들고 있는 이동
        bool wasLocked, ready;
        float lastT;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            bridge = GetComponent<PlcBridge>();
            grabber = GetComponent<SpreaderGrabber>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            telescope = GetComponentInChildren<SpreaderTelescope>(true);
            rtgTele = GetComponentInChildren<RtgSpreaderTelescope>(true);
        }

        void FixedUpdate()
        {
            if (!ready && !TryInit()) return;

            float t = csv.PlayheadS;
            if (t < lastT) ResetCargo();   // CSV 되감기 — 처음부터
            lastT = t;

            for (int i = next; i < moves.Length; i++)
                if (boxes[i] == null && t >= AppearS(i)) boxes[i] = Spawn(moves[i]);

            bool locked = bridge.Latest.TwistLockLocked;
            if (locked && !wasLocked && next < moves.Length) Pick(next++);
            else if (!locked && wasLocked && held >= 0) Place();
            wasLocked = locked;
        }

        // PlcBridge 가 range 를 산출한 첫 틱에 1회. 이력이 없으면 꺼진다(축 재생은 그대로).
        bool TryInit()
        {
            if (!bridge.Active || !bridge.RangesResolved || crane.Spreader == null || crane.Attach == null) return false;
            csv = bridge.Source as CsvReplaySource;
            string path = string.IsNullOrEmpty(bridge.CsvPath) ? null : Path.ChangeExtension(bridge.CsvPath, ".history.csv");
            if (csv == null || path == null || !File.Exists(path))
            {
                Debug.Log($"[PlcCargo] 작업 이력 없음 — 화물 없이 축만 재생 ({path ?? "CSV 경로 없음"})");
                enabled = false;
                return false;
            }
            moves = ReadHistory(path);
            boxes = new Transform[moves.Length];
            grabDrop = GrabPointNow().y - SpreaderBottomY();
            ready = true;
            Debug.Log($"[PlcCargo] {Path.GetFileName(path)} — {moves.Length}개 이동, 콘 바닥 {grabDrop:F4}u 아래");
            return true;
        }

        // 섀시에서 집는 건 직전 작업을 놓는 순간 트럭이 들어온다. 나머지는 처음부터 제자리에 있다.
        float AppearS(int i) => moves[i].from == Chassis && i > 0 ? moves[i - 1].placeS : 0f;

        void Pick(int i)
        {
            if (boxes[i] == null) boxes[i] = Spawn(moves[i]);   // 등장 전에 잠금이 오면(데이터 이상) 그 자리에 바로
            var c = boxes[i];
            Vector3 pos = c.position; Quaternion rot = c.rotation;
            if (!crane.Attach.Attach(c)) return;                 // 이미 뭔가 들고 있음(VR 잡기 등)
            c.SetPositionAndRotation(pos, rot);                  // Attach 는 부착점 원점·회전으로 스냅한다 — PLC 자세 그대로 둔다
            held = i;
            if (lockAnim != null) lockAnim.SetLocked(true);
            if (telescope != null) telescope.Set40(moves[i].ft40);
            if (rtgTele != null) rtgTele.SetSize(moves[i].ft40 ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
        }

        void Place()
        {
            var c = crane.Attach.Detach();
            if (lockAnim != null) lockAnim.SetLocked(false);
            if (rtgTele != null) rtgTele.SetSize(RtgSpreaderTelescope.Size.Ft40);   // 빈 스프레더 기준자세(SpreaderGrabber.Release 와 같음)
            if (c != null && moves[held].to == Chassis) Destroy(c.gameObject, truckLeaveS);   // 트럭이 싣고 떠난다
            held = -1;
        }

        void ResetCargo()
        {
            if (held >= 0) { crane.Attach.Detach(); held = -1; }
            if (lockAnim != null) lockAnim.SetLocked(false);
            foreach (var b in boxes) if (b != null) Destroy(b.gameObject);
            System.Array.Clear(boxes, 0, boxes.Length);
            next = 0; wasLocked = false;
        }

        // 집기 순간의 PLC 자세에서 콘 바닥 중심이 오는 자리 = 컨테이너 윗면 중심.
        Transform Spawn(Move m)
        {
            Vector3 top = bridge.WorldAtPose(csv.FrameAt(m.pickS), GrabPointNow() - Vector3.up * grabDrop);
            var go = MakeBox(m.ft40);
            go.name = m.id;
            var b = WorldBounds(go);
            go.transform.position += top - new Vector3(b.center.x, b.max.y, b.center.z);
            return go.transform;
        }

        static GameObject MakeBox(bool ft40)
        {
            var tpl = GameObject.Find(ft40 ? "Cont40_00" : "Cont20_00");
            GameObject go;
            if (tpl != null) go = Instantiate(tpl, tpl.transform.parent);   // 야드와 같은 부모 = 같은 스케일·방향
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);          // 야드 없는 씬 — ISO 치수(콜라이더 포함)
                go.transform.localScale = new Vector3(2.438f, 2.591f, ft40 ? 12.192f : 6.058f) * StsConfig.ModelScale;
            }
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var b = WorldBounds(go);
                var s = go.transform.lossyScale;
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                bc.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z);
            }
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;   // 놓은 자리에 그대로 — 물리로 흔들리지 않게
            return go;
        }

        Vector3 GrabPointNow() => grabber != null ? grabber.GrabPoint() : ((Component)crane.Spreader).transform.position;

        float SpreaderBottomY()
        {
            var sp = ((Component)crane.Spreader).transform;
            float y = sp.position.y;
            foreach (var r in sp.GetComponentsInChildren<Renderer>()) y = Mathf.Min(y, r.bounds.min.y);
            return y;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static Move[] ReadHistory(string path)
        {
            var lines = File.ReadAllLines(path);
            var col = new Dictionary<string, int>();
            var head = lines[0].Split(',');
            for (int i = 0; i < head.Length; i++) col[head[i].Trim()] = i;
            var list = new List<Move>();
            for (int li = 1; li < lines.Length; li++)
            {
                if (string.IsNullOrWhiteSpace(lines[li])) continue;
                var c = lines[li].Split(',');
                list.Add(new Move
                {
                    id = c[col["container"]], from = c[col["from"]], to = c[col["to"]],
                    ft40 = c[col["size_ft"]] == "40",
                    // CsvReplaySource 와 같은 식(t_ms / 1000f) — FrameAt 이 정확히 그 행을 찾는다.
                    pickS = int.Parse(c[col["pick_t_ms"]], CultureInfo.InvariantCulture) / 1000f,
                    placeS = int.Parse(c[col["place_t_ms"]], CultureInfo.InvariantCulture) / 1000f,
                });
            }
            return list.ToArray();
        }
    }
}
