using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ContainerProject;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 부두 컨테이너 인식 — 가상 PLC 1단계(인식만, 구동 없음).
    /// 씬의 '집을 수 있는' 자유 강체(컨테이너)를 스캔해 위치·규격(20/40ft)·무게·등급·ID를 인식·보고한다.
    /// 크레인 부속(차체·스프레더 등)은 제외. PLC가 작업 전 "무엇이 어디에 있는지" 파악하는 단계에 해당.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Quay Container Scanner (부두 인식)")]
    [DisallowMultipleComponent]
    public sealed class QuayContainerScanner : MonoBehaviour
    {
        /// <summary>인식된 컨테이너 1건.</summary>
        public struct Recognized
        {
            public Transform t;
            public Vector3 center;   // 월드 바운즈 중심
            public Vector3 size;     // 월드 바운즈 크기
            public bool is40ft;
            public float tons;
            public LoadGrade grade;
            public string id;
        }

        [Tooltip("Play 시작 시 자동 스캔.")]
        [SerializeField] bool scanOnStart = true;
        [Tooltip("스캔 결과를 콘솔에 표로 출력.")]
        [SerializeField] bool logResults = true;
        [Tooltip("긴축(모델) 길이가 이 값을 넘으면 40ft로 인식 (20ft≈0.25 / 40ft≈0.51).")]
        [SerializeField] float fortyFtThreshold = 0.38f;
        [Tooltip("Scene 뷰에 등급색 박스+라벨 표시.")]
        [SerializeField] bool drawGizmos = true;

        readonly List<Recognized> found = new List<Recognized>();
        /// <summary>마지막 스캔 결과.</summary>
        public IReadOnlyList<Recognized> Found => found;

        void Start() { if (scanOnStart) Scan(); }

        /// <summary>부두 컨테이너 스캔 — 반환=인식 개수. 에디터 컴포넌트 컨텍스트 메뉴/메뉴에서도 호출.</summary>
        [ContextMenu("부두 컨테이너 스캔")]
        public int Scan()
        {
            found.Clear();
            var all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude);
            foreach (var rb in all)
            {
                if (rb == null) continue;
                if (rb.GetComponentInParent<StsCrane>() != null) continue;   // 크레인 부속 제외
                Transform t = rb.transform;
                if (!TryBounds(t, out Bounds b)) continue;                    // 렌더러 없는 강체 제외
                found.Add(Recognize(t, b));
            }
            if (logResults) LogTable();
            ReportOverlaps();
            return found.Count;
        }

        // 모든 컨테이너 쌍의 실제 바운즈(AABB) 겹침을 계산해 보고 — 겹친 쌍·겹침량 전부 출력.
        void ReportOverlaps()
        {
            int pairs = 0;
            const float eps = 0.002f;   // 2mm 이하 맞닿음은 겹침 아님
            for (int i = 0; i < found.Count; i++)
                for (int j = i + 1; j < found.Count; j++)
                {
                    var a = found[i]; var b = found[j];
                    float ox = (a.size.x + b.size.x) * 0.5f - Mathf.Abs(a.center.x - b.center.x);
                    float oy = (a.size.y + b.size.y) * 0.5f - Mathf.Abs(a.center.y - b.center.y);
                    float oz = (a.size.z + b.size.z) * 0.5f - Mathf.Abs(a.center.z - b.center.z);
                    if (ox > eps && oy > eps && oz > eps)
                    {
                        pairs++;
                        Debug.LogWarning($"[겹침] {NameOf(a.t)} ∩ {NameOf(b.t)} — 겹침 X{ox:F3} Y{oy:F3} Z{oz:F3}");
                    }
                }
            if (pairs == 0) Debug.Log($"[겹침] 검사 완료 — 겹침 0쌍 / {found.Count}개 (정상)");
            else Debug.LogWarning($"[겹침] 검사 완료 — ⚠ {pairs}쌍 겹침 / {found.Count}개");
        }

        static string NameOf(Transform t) => t != null ? t.name : "?";

        Recognized Recognize(Transform t, Bounds b)
        {
            float longSide = Mathf.Max(b.size.x, b.size.z);
            var inst = t.GetComponentInParent<ContainerInstance>();
            float tons = inst != null && inst.LoadTons > 0f ? inst.LoadTons : ContainerLoad.WeightTons(t.name);
            string id = inst != null && !string.IsNullOrEmpty(inst.DisplayId)
                ? inst.DisplayId
                : ContainerIdGenerator.FormatForDisplay(
                      ContainerIdGenerator.GenerateDeterministic(CraneHud.BaseName(t.name)));
            return new Recognized
            {
                t = t,
                center = b.center,
                size = b.size,
                is40ft = longSide > fortyFtThreshold,
                tons = tons,
                grade = ContainerLoad.Grade(tons),
                id = id,
            };
        }

        void LogTable()
        {
            var sb = new StringBuilder();
            sb.Append($"[부두 인식] 컨테이너 {found.Count}개\n");
            for (int i = 0; i < found.Count; i++)
            {
                var r = found[i];
                sb.Append($"  {i + 1,2}. {NameOf(r.t)} · {r.id} · {(r.is40ft ? "40ft" : "20ft")} · {r.tons,5:F1}t · {GradeKo(r.grade)} · " +
                          $"pos({r.center.x:F2}, {r.center.y:F2}, {r.center.z:F2})\n");
            }
            Debug.Log(sb.ToString());
        }

        static string GradeKo(LoadGrade g) => g switch
        {
            LoadGrade.Normal  => "정상",
            LoadGrade.Caution => "주의",
            _                 => "이상",
        };

        static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            foreach (var r in found)
            {
                if (r.t == null) continue;
                Gizmos.color = GradeColor(r.grade);
                Gizmos.DrawWireCube(r.center, r.size);
                UnityEditor.Handles.color = Gizmos.color;
                UnityEditor.Handles.Label(r.center + Vector3.up * (r.size.y * 0.5f + 0.03f),
                    $"{r.id}\n{(r.is40ft ? "40ft" : "20ft")} {r.tons:F1}t {GradeKo(r.grade)}");
            }
        }

        static Color GradeColor(LoadGrade g) => g switch
        {
            LoadGrade.Normal  => new Color(0.30f, 0.85f, 0.40f),
            LoadGrade.Caution => new Color(0.95f, 0.80f, 0.20f),
            _                 => new Color(0.92f, 0.20f, 0.18f),
        };
#endif
    }
}
