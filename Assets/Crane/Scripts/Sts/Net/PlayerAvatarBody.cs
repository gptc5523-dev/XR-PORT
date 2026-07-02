using UnityEngine;

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// 머리+양손만 있던 아바타에 전신(목·가슴·골반·다리·양팔)을 절차적으로 붙이는 차콜 마네킹 빌더.
    /// 외부 3D 모델 import 없이 캡슐/구 프리미티브로 실루엣을 만든다(이목구비 없는 마네킹 스타일).
    ///
    /// [좌표] 빌더는 머리(HMD) 위치를 원점으로, 머리의 수평 방향(yaw)만 따라가는 'bodyRoot'를 매 프레임 세운다.
    ///   - 몸통/다리는 bodyRoot 로컬에 고정 오프셋(실척 m)으로 매달려 머리 아래 수직으로 선다(피치/롤은 무시 → 고개 숙여도 몸 안 기움).
    ///   - 양팔은 어깨(고정)→손(네트워크/로컬 추적)으로 2본 IK를 풀어 팔꿈치를 잡고 캡슐 2개로 잇는다(손을 따라 굽음).
    /// [스케일] 아바타 루트가 1/24(미니어처)이라 자식 로컬 m는 자동으로 1/24 축소 — 머리/손과 시각 크기가 맞는다.
    /// [가시성] 빌드 직후 PlayerAvatarSync.ReapplyVisibility()로 현재 표시 상태(소유자 1인칭이면 숨김)를 새 부품에도 적용한다.
    /// </summary>
    [AddComponentMenu("Container/Net/Player Avatar Body")]
    [DisallowMultipleComponent]
    public sealed class PlayerAvatarBody : MonoBehaviour
    {
        // ── 비율(눈높이 0에서 아래로, 로컬 실척 m. +Z=정면, +X=오른쪽). S≈1.75 기준 인체 비율 ──
        const float NeckDrop = 0.10f;   // 목 밑동
        const float ShoulderDrop = 0.20f, ShoulderHalf = 0.20f;   // 어깨 높이/반폭
        const float ChestR = 0.15f;     // 가슴 굵기 (가슴→복부→골반을 겹쳐 끊김 없이 연결)
        const float PelvisDrop = 0.70f, PelvisR = 0.13f, HipHalf = 0.09f;
        const float KneeDrop = 1.14f, KneeFwd = 0.02f;
        const float AnkleDrop = 1.60f, AnkleFwd = 0.05f, FootFwd = 0.13f;
        const float ThighR = 0.075f, ShinR = 0.060f;
        const float UpperArm = 0.30f, ForeArm = 0.25f, UpperArmR = 0.052f, ForeArmR = 0.046f;
        const float JointR = 0.07f;    // 어깨/골반/무릎/팔꿈치 솔리드 구 — 둥근 관절이라 구가 정석

        static readonly Color Charcoal = new Color(0.17f, 0.17f, 0.19f);

        PlayerAvatarSync sync;
        Material mat;
        Transform bodyRoot;

        // 매 프레임 갱신하는 팔 부품(어깨 고정·팔꿈치/캡슐은 IK로 이동)
        Transform shoulderL, shoulderR, upperArmL, upperArmR, foreArmL, foreArmR, elbowL, elbowR;

        Vector3 shoulderPosL, shoulderPosR;   // bodyRoot 로컬 어깨 좌표(고정)

        void Awake() => sync = GetComponent<PlayerAvatarSync>();

        void LateUpdate()   // PlayerAvatarSync.Update가 머리/손을 세운 뒤 실행
        {
            if (sync == null || sync.Head == null) return;
            if (bodyRoot == null) Build();

            // bodyRoot = 머리 위치 + 머리 yaw만(피치/롤 제거 → 몸이 수직으로 섬)
            Transform head = sync.Head;
            bodyRoot.position = head.position;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) { fwd = -head.up; fwd.y = 0f; }   // 정수리/바닥 응시 시 폴백
            if (fwd.sqrMagnitude > 1e-4f) bodyRoot.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            // 양팔 IK — 손 월드 → bodyRoot 로컬에서 풀고 캡슐 배치
            SolveArm(shoulderPosL, shoulderL, upperArmL, foreArmL, elbowL, sync.LeftHand, -1f);
            SolveArm(shoulderPosR, shoulderR, upperArmR, foreArmR, elbowR, sync.RightHand, +1f);
        }

        void SolveArm(Vector3 shoulder, Transform shJoint, Transform upper, Transform fore, Transform elbowJoint,
                      Transform handT, float side)
        {
            // 손 로컬 좌표(없거나 미추적이면 옆구리 휴식 자세)
            Vector3 hand;
            if (handT != null && handT.position.sqrMagnitude > 1e-6f)
            {
                hand = bodyRoot.InverseTransformPoint(handT.position);
                float reach = UpperArm + ForeArm;
                if ((hand - shoulder).magnitude > reach * 3f)   // 비정상(미추적 → 원점 근처) → 휴식 자세
                    hand = new Vector3(side * 0.18f, -0.62f, 0.06f);
            }
            else hand = new Vector3(side * 0.18f, -0.62f, 0.06f);

            // 2본 IK — 팔꿈치는 뒤·아래·바깥으로 굽힘
            Vector3 bend = new Vector3(side * 0.25f, -0.5f, -1f);
            Vector3 elbow = SolveIk(shoulder, hand, UpperArm, ForeArm, bend);

            shJoint.localPosition = shoulder;
            elbowJoint.localPosition = elbow;
            Span(upper, shoulder, elbow, UpperArmR);
            Span(fore, elbow, hand, ForeArmR);
        }

        // 어깨(a)·손(b) 사이 2본 IK — 코사인 법칙으로 팔꿈치 위치. bendHint로 굽힘 평면 결정.
        static Vector3 SolveIk(Vector3 a, Vector3 b, float l1, float l2, Vector3 bendHint)
        {
            Vector3 ab = b - a;
            float d = ab.magnitude;
            float dc = Mathf.Clamp(d, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = d > 1e-5f ? ab / d : Vector3.down;
            float adj = (l1 * l1 + dc * dc - l2 * l2) / (2f * dc);   // 어깨→(팔꿈치 투영) 거리
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - adj * adj));
            Vector3 bend = Vector3.ProjectOnPlane(bendHint, dir);
            if (bend.sqrMagnitude < 1e-4f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.zero;
            return a + dir * adj + bend * h;
        }

        // 캡슐(seg)을 로컬 점 a→b로 펴기 — 길이/방향/굵기 설정(Unity 캡슐 기본 높이 2 → scale.y = 길이/2)
        static void Span(Transform seg, Vector3 a, Vector3 b, float r)
        {
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a; float len = dir.magnitude;
            seg.localPosition = mid;
            if (len > 1e-5f) seg.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            seg.localScale = new Vector3(r * 2f, Mathf.Max(len * 0.5f, r), r * 2f);
        }

        void Build()
        {
            // 차콜 머티리얼 — 기존 렌더러 셰이더를 복제해 URP 호환 보장(머리만 참가자색, 몸은 차콜)
            Shader sh = null;
            var refR = sync.Head != null ? sync.Head.GetComponentInChildren<Renderer>() : null;
            if (refR != null && refR.sharedMaterial != null) sh = refR.sharedMaterial.shader;
            mat = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            // [임시] 마네킹 전체 빨강(요청). 차콜 마네킹으로 되돌리려면 Charcoal로 교체.
            var body = Color.red;
            mat.color = body;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", body);

            // [임시] 머리·목·양손(프리팹 구체)도 같은 빨강으로 칠해 전신 통일.
            TintExisting(sync.Head);
            TintExisting(sync.LeftHand);
            TintExisting(sync.RightHand);

            bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(transform, false);   // 아바타 루트(1/24) 자식 — 로컬 m가 자동 축소

            // ── 몸통(고정, 가슴→복부→골반을 서로 겹쳐 허리에서 끊기지 않게 연속화) ──
            Capsule("Chest",   new Vector3(0, -0.13f, 0.01f), new Vector3(0, -0.40f, 0f), ChestR);
            Capsule("Abdomen", new Vector3(0, -0.38f, 0f),    new Vector3(0, -0.64f, 0f), 0.135f);   // 가슴↔골반 다리(허리 공백 제거)
            Sphere("Pelvis", new Vector3(0, -PelvisDrop, 0), PelvisR);
            // 어깨-가슴 윗단 연결(목→어깨 라인)
            Capsule("Shoulders", new Vector3(-ShoulderHalf, -ShoulderDrop, 0), new Vector3(ShoulderHalf, -ShoulderDrop, 0), 0.075f);

            // ── 다리(고정) ──
            BuildLeg(-1f);
            BuildLeg(+1f);

            // ── 참가자 식별 헬멧(머리에 부착해 머리 회전 따라감) — 몸/머리는 차콜, ID는 헬멧 색으로 ──
            BuildHelmet();

            // ── 팔 부품(매 프레임 IK로 갱신) ──
            shoulderPosL = new Vector3(-ShoulderHalf, -ShoulderDrop, 0f);
            shoulderPosR = new Vector3(ShoulderHalf, -ShoulderDrop, 0f);
            shoulderL = Sphere("ShoulderL", shoulderPosL, JointR * 0.9f);
            shoulderR = Sphere("ShoulderR", shoulderPosR, JointR * 0.9f);
            elbowL = Sphere("ElbowL", Vector3.zero, UpperArmR * 1.1f);
            elbowR = Sphere("ElbowR", Vector3.zero, UpperArmR * 1.1f);
            upperArmL = Capsule("UpperArmL", Vector3.zero, Vector3.up * 0.1f, UpperArmR);
            upperArmR = Capsule("UpperArmR", Vector3.zero, Vector3.up * 0.1f, UpperArmR);
            foreArmL = Capsule("ForeArmL", Vector3.zero, Vector3.up * 0.1f, ForeArmR);
            foreArmR = Capsule("ForeArmR", Vector3.zero, Vector3.up * 0.1f, ForeArmR);

            sync.ReapplyVisibility();   // 1인칭(소유자)이면 방금 만든 부품도 숨김 처리
        }

        // 참가자색 헬멧 — 머리(sync.Head) 자식으로 붙여 머리 회전을 그대로 따라간다. 머리 로컬 구 반경 0.5 기준.
        void BuildHelmet()
        {
            if (sync.Head == null) return;
            // [임시] 헬멧도 전부 빨강(요청). 참가자별 식별색으로 되돌리려면 sync.OwnerColor로 교체.
            var capCol = Color.red;
            var capMat = new Material(mat.shader);
            capMat.color = capCol;
            if (capMat.HasProperty("_BaseColor")) capMat.SetColor("_BaseColor", capCol);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Helmet";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.SetParent(sync.Head, false);
            go.transform.localPosition = new Vector3(0f, 0.16f, 0f);   // 정수리 위
            go.transform.localScale = new Vector3(1.08f, 0.72f, 1.08f); // 살짝 크고 납작하게(안전모)
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) { r.sharedMaterial = capMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        }

        // 프리팹 기본 부품(머리·목·손)의 렌더러를 마네킹 머티리얼(빨강)로 교체 — 자식(목 등) 포함.
        void TintExisting(Transform t)
        {
            if (t == null) return;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                r.sharedMaterial = mat;
        }

        void BuildLeg(float side)
        {
            string s = side < 0 ? "L" : "R";
            Vector3 hip = new Vector3(side * HipHalf, -PelvisDrop, 0);
            Vector3 knee = new Vector3(side * 0.10f, -KneeDrop, KneeFwd);
            Vector3 ankle = new Vector3(side * 0.10f, -AnkleDrop, AnkleFwd);
            Vector3 toe = new Vector3(side * 0.10f, -AnkleDrop + 0.02f, FootFwd);
            Capsule("Thigh" + s, hip, knee, ThighR);
            Sphere("Knee" + s, knee, ShinR * 1.05f);
            Capsule("Shin" + s, knee, ankle, ShinR);
            Capsule("Foot" + s, ankle, toe, ShinR * 0.8f);
        }

        Transform Sphere(string name, Vector3 localPos, float radius)
        {
            var t = MakePart(name, PrimitiveType.Sphere);
            t.localPosition = localPos;
            t.localScale = Vector3.one * (radius * 2f);
            return t;
        }

        Transform Capsule(string name, Vector3 a, Vector3 b, float r)
        {
            var t = MakePart(name, PrimitiveType.Capsule);
            Span(t, a, b, r);
            return t;
        }

        Transform MakePart(string name, PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // 순수 시각 부품 — 물리 충돌 없음
            var t = go.transform;
            t.SetParent(bodyRoot, false);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) { r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return t;
        }
    }
}
