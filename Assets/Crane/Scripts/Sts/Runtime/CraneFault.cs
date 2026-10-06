using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>알람 심각도 — ㈜엠비이 코드북: 0=Info, 1=Warning, 2=Critical, 3=Fatal.</summary>
    public enum FaultSeverity { Info = 0, Warning = 1, Critical = 2, Fatal = 3 }

    /// <summary>단일 알람 정의(코드/심각도/자동정지/한글 메시지).</summary>
    public readonly struct FaultDef
    {
        public readonly int Code;
        public readonly FaultSeverity Sev;
        public readonly bool AutoStop;
        public readonly string Message;
        public FaultDef(int code, FaultSeverity sev, bool autoStop, string message)
        { Code = code; Sev = sev; AutoStop = autoStop; Message = message; }
        public bool IsValid => Code != 0;
    }

    /// <summary>STS 크레인 알람 코드 — ㈜엠비이 알람 코드북(MBE-DOC-2026-XR-002 #65) 기준, 임의 정의 금지.
    /// 1차년도: 우리 데이터로 자연 발생하는 8개만 활성(전체 170개·시나리오 주입은 2차년도).</summary>
    public static class CraneFault
    {
        // 1차년도 활성 알람 코드(코드북 실제 코드만 상수로 보관).
        // 심각도/AutoStop/메시지는 코드에 두지 않고 항상 AlarmCodebook(SSOT)에서 떠 쓴다.
        public const int CodeOverload     = 3012;   // HO 과부하 (정격 초과)
        public const int CodeCollision    = 1012;   // GT 충돌방지 센서 작동
        public const int CodeHoistUpper   = 3009;   // HO 상한 위치 도달
        public const int CodeHoistLower   = 3011;   // HO 하한 위치 도달
        public const int CodeTrolleyLand  = 2009;   // TR 안벽측 한계 도달
        public const int CodeTrolleySea   = 2010;   // TR 선박측 한계 도달
        public const int CodeTrolleyColl  = 2012;   // TR 트롤리 충돌방지 작동
        public const int CodeGantryFwd    = 1009;   // GT 전방 한계 도달
        public const int CodeGantryRev    = 1010;   // GT 후방 한계 도달
        public const int CodeLoadSnag     = 3013;   // HO Snag 발생(걸림 감지) — 적재물이 옆 컨테이너와 충돌
        public const int CodeGantryAccel  = 1021;   // GT 가속도 한계 초과
        public const int CodeTrolleyAccel = 2021;   // TR 가속도 한계 초과
        public const int CodeHoistAccel   = 3021;   // HO 가속도 한계 초과

        // FaultDef 지연 조회 프로퍼티 — 정적 초기화 시점에 Resources.Load 를 부르면 순서 위험이 있어
        // static readonly 즉시 초기화를 금지한다. 각 프로퍼티가 호출 시점에 AlarmCodebook.Get 으로 구성.
        public static FaultDef Overload     => FromCodebook(CodeOverload);
        public static FaultDef Collision    => FromCodebook(CodeCollision);
        public static FaultDef HoistUpper   => FromCodebook(CodeHoistUpper);
        public static FaultDef HoistLower   => FromCodebook(CodeHoistLower);
        public static FaultDef TrolleyLand  => FromCodebook(CodeTrolleyLand);
        public static FaultDef TrolleySea   => FromCodebook(CodeTrolleySea);
        public static FaultDef TrolleyColl  => FromCodebook(CodeTrolleyColl);
        public static FaultDef GantryFwd    => FromCodebook(CodeGantryFwd);
        public static FaultDef GantryRev    => FromCodebook(CodeGantryRev);
        public static FaultDef LoadSnag     => FromCodebook(CodeLoadSnag);
        public static FaultDef GantryAccel  => FromCodebook(CodeGantryAccel);
        public static FaultDef TrolleyAccel => FromCodebook(CodeTrolleyAccel);
        public static FaultDef HoistAccel   => FromCodebook(CodeHoistAccel);

        // 유일하게 허용된 하드코딩 안전 디폴트(SSOT 예외, fail-to-safe) — 코드북 로드 실패 시 알람이
        // 조용히 무효화(알람 0 → 정상인 척)되지 않도록 하는 비상 정의뿐이다. IsLoaded=true 면 절대 안 쓰인다.
        public const int CodeAlarmSystemOffline = 9001;
        static readonly FaultDef AlarmSystemOffline =
            new FaultDef(CodeAlarmSystemOffline, FaultSeverity.Fatal, true,
                         "알람 시스템 오프라인 — 안전정지 (코드북 로드 실패)");

        /// <summary>코드북(SSOT)에서 코드 1건을 떠 FaultDef 로 구성 — 심각도/AutoStop/메시지는 코드북 값 그대로.
        /// 코드북에 없으면 에러 로그 + 무효(IsValid=false). 임의 날조 금지.</summary>
        public static FaultDef FromCodebook(int code)
        {
            var e = AlarmCodebook.Get(code);
            if (e == null)
            {
                Debug.LogError($"[CraneFault] 코드 {code} 가 코드북(AlarmCodebook)에 없음 — 무효 알람 반환. 코드북 JSON 확인.");
                return default;
            }
            return new FaultDef(code, e.Severity, e.autoStop, e.ko);
        }

        /// <summary>주입 결함 — 설정되면 자연 발생 알람보다 우선 반환(데모/검증용). 기본 무효, 사용 후 default 로 클리어.</summary>
        public static FaultDef Injected;

        /// <summary>심각도 → 표시색. 코드북 섹션 7(Fatal 적색 / Critical 주황 / Warning 노랑 / Info 회색).</summary>
        public static Color SevColor(FaultSeverity s) => s switch
        {
            FaultSeverity.Fatal    => new Color(0.92f, 0.20f, 0.18f),
            FaultSeverity.Critical => new Color(0.95f, 0.55f, 0.15f),
            FaultSeverity.Warning  => new Color(0.95f, 0.80f, 0.20f),
            _                      => new Color(0.65f, 0.65f, 0.68f),
        };

        /// <summary>HUD 한 줄 표기: "[3012] HO 과부하 (정격 초과)".</summary>
        public static string Format(FaultDef f) => $"[{f.Code}] {f.Message}";

        /// <summary>스프레더(권상) 알람 — 과부하/호이스트 끝단.</summary>
        public static FaultDef EvaluateSpreader(StsCrane crane)
        {
            if (crane == null) return default;
            var attach = crane.Attach;
            // 과부하(3012)는 크레인 정격(SWL) 초과 — 컨테이너 ISO 과적이 아니라 SWL %임계로 판정.
            if (attach != null && attach.HasContainer && ContainerLoad.CraneLoadGrade(attach.AttachedLoadTons) == LoadGrade.Over)
                return Overload;                          // 과부하 (Fatal)
            var grabber = crane.GetComponent<SpreaderGrabber>();
            if (grabber != null && grabber.LoadCollision)
                return LoadSnag;                          // 적재물이 다른 컨테이너와 충돌(걸림, Critical)
            if (crane.Spreader is AxisMoverBase h)
            {
                if (h.AtUpperLimit) return HoistUpper;    // 상한 (Critical)
                if (h.AtLowerLimit) return HoistLower;    // 하한
            }
            var op = crane.OpMode;
            if (op != null && op.PlcDriven && op.HoistAccelTripped) return HoistAccel;   // 권상 급가속 (PLC 실데이터·디바운스 트립만)
            return default;
        }

        /// <summary>트롤리 알람 — 안벽/선박측 끝단.</summary>
        public static FaultDef EvaluateTrolley(StsCrane crane)
        {
            if (crane?.Trolley is AxisMoverBase t)
            {
                if (t.IsBlocked) return TrolleyColl;      // 충돌방지 (Fatal)
                if (t.AtUpperLimit) return TrolleySea;    // Max = 바다(선박)측 — Play에서 방향 확인
                if (t.AtLowerLimit) return TrolleyLand;   // Min = 안벽(육지)측
            }
            var op = crane?.OpMode;
            if (op != null && op.PlcDriven && op.TrolleyAccelTripped) return TrolleyAccel;   // 횡행 급가속 (PLC 실데이터·디바운스 트립만)
            return default;
        }

        /// <summary>갠트리 알람 — 충돌 정지/주행 끝단.</summary>
        public static FaultDef EvaluateGantry(StsCrane crane)
        {
            if (crane?.Gantry is AxisMoverBase g)
            {
                if (g.IsBlocked) return Collision;        // 충돌 (Fatal)
                if (g.AtUpperLimit) return GantryFwd;     // 전방 (Critical)
                if (g.AtLowerLimit) return GantryRev;     // 후방
            }
            var op = crane?.OpMode;
            if (op != null && op.PlcDriven && op.GantryAccelTripped) return GantryAccel;   // 주행 급가속 (PLC 실데이터·디바운스 트립만)
            return default;
        }

        /// <summary>전체 — HUD 요약용. 활성 알람 중 심각도 최고 1건(동급은 스프레더&gt;갠트리&gt;트롤리). 코드북 §7.</summary>
        public static FaultDef Evaluate(StsCrane crane)
        {
            // fail-to-safe 최우선: 코드북(SSOT) 미로드면 모든 알람 정의를 신뢰할 수 없음 → '알람 시스템 오프라인' 비상.
            // 조용히 "알람 0"으로 정상 운전하는 것을 막는다. 정상 시(IsLoaded=true) 통과.
            if (!AlarmCodebook.IsLoaded) return AlarmSystemOffline;
            if (Injected.IsValid) return Injected;   // 시나리오 주입 결함 최우선(데모) — HUD·상태판·OpMode 일괄 반응
            // 심각도 비교로 우선순위를 일원화 — 트롤리 Fatal 이 다른 축 Critical 에 가려지지 않게.
            var best = EvaluateSpreader(crane);
            best = Higher(best, EvaluateGantry(crane));
            best = Higher(best, EvaluateTrolley(crane));
            return best;   // IsValid=false → "이상 없음"
        }

        // 더 심각한 알람을 고름. 동급이면 a 유지(호출 순서=스프레더>갠트리>트롤리 우선).
        static FaultDef Higher(FaultDef a, FaultDef b)
        {
            if (!b.IsValid) return a;
            if (!a.IsValid) return b;
            return (int)b.Sev > (int)a.Sev ? b : a;
        }

        /// <summary>활성 알람 전부 수집(축별 1건씩) → 심각도 내림차순 정렬. 반환=개수. buf 는 호출부가 재사용(무할당).
        /// 동급 내 최근 발생 우선은 미구현 — 현재는 축 순서(스프레더&gt;갠트리&gt;트롤리)로 안정 정렬.</summary>
        public static int EvaluateAll(StsCrane crane, FaultDef[] buf)
        {
            if (buf == null || buf.Length == 0) return 0;
            int n = 0;
            void Add(FaultDef f) { if (f.IsValid && n < buf.Length) buf[n++] = f; }
            // fail-to-safe: 코드북 미로드면 비상 1건만 — 다른 알람 정의는 신뢰 불가이므로 평가하지 않는다.
            if (!AlarmCodebook.IsLoaded) { buf[0] = AlarmSystemOffline; return 1; }
            Add(Injected);                            // 주입 결함도 리스트에 포함(심각도 정렬로 자동 상단)
            Add(EvaluateSpreader(crane));
            Add(EvaluateGantry(crane));
            Add(EvaluateTrolley(crane));
            // 작은 n — 삽입정렬로 심각도 내림차순(안정: 동급은 추가 순서 유지).
            for (int i = 1; i < n; i++)
            {
                var key = buf[i];
                int j = i - 1;
                while (j >= 0 && (int)buf[j].Sev < (int)key.Sev) { buf[j + 1] = buf[j]; j--; }
                buf[j + 1] = key;
            }
            return n;
        }
    }
}
