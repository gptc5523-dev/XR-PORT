using UnityEngine;
using ContainerProject;

namespace Container.Crane.Sts
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

    /// <summary>
    /// STS 크레인 알람 코드 — ㈜엠비이 알람 코드북(MBE-DOC-2026-XR-002 #65) 기준.
    /// ※ 코드·심각도는 임의 정의 금지. 항상 코드북을 따른다.
    ///
    /// 1차년도: 우리 데이터(하중·축위치·충돌)로 '자연 발생'하는 8개만 활성.
    /// 전체 170개·시나리오 주입·AI 학습 데이터 생성은 2차년도.
    /// </summary>
    public static class CraneFault
    {
        // 1차년도 활성 알람 코드(코드북 실제 코드만 상수로 보관)
        //   ※ 심각도/AutoStop/메시지는 코드에 두지 않는다. 항상 AlarmCodebook(SSOT)에서 떠 쓴다(H4).
        //     코드만 명명 상수로 두어 Evaluate*가 읽기 쉽게 하고, FaultDef는 아래 getter가 지연 조회로 구성.
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

        // FaultDef 지연 조회 프로퍼티(API 표면 유지)
        //   정적 초기화 시점에 Resources.Load를 호출하면 순서 위험이 있으므로 static readonly 즉시 초기화 금지.
        //   각 프로퍼티가 호출 시점에 AlarmCodebook.Get으로 구성(코드북은 1회 지연 로드 후 캐시).
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

        // 유일하게 허용된 하드코딩 안전 디폴트 — SSOT 예외, fail-to-safe용
        //   재감사 신규결함 #1 시정: 코드북 로드 실패 시 알람 전체가 '조용히 무효화'(알람 0 → 정상인 척 운전)되는
        //   단일 실패점을 차단한다. 개별 170개 알람을 날조(H4 금지)하는 게 아니라, "알람 시스템 자체가 불가"라는
        //   단 하나의 명시적 비상 정의만 코드북 없이 허용한다. AutoStop=true(안전측 실패).
        //   코드 9001 = SYS 계열(천단위 5=SYS와 구분되는 '시스템 자체 장애' 영역). Fatal.
        //   ※ AlarmCodebook.IsLoaded==true(정상)이면 이 정의는 절대 반환되지 않는다(거동 불변).
        public const int CodeAlarmSystemOffline = 9001;
        static readonly FaultDef AlarmSystemOffline =
            new FaultDef(CodeAlarmSystemOffline, FaultSeverity.Fatal, true,
                         "알람 시스템 오프라인 — 안전정지 (코드북 로드 실패)");

        /// <summary>
        /// 코드북(SSOT)에서 코드 1건을 떠 FaultDef로 구성. 심각도/AutoStop/메시지는 코드북 값을 그대로 따른다.
        /// 코드북에 없으면 에러 로그 + 무효(default, IsValid=false) — 임의 심각도/정지 날조 금지(H4).
        /// </summary>
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

        /// <summary>
        /// 주입 결함 — 설정되면 모든 자연 발생 알람보다 <b>우선</b> 반환(결함 데모/검증용).
        /// 외부에서 AlarmCodebook 코드를 떠 채운다(현재 자동 주입 시나리오는 없음). 기본 무효(IsValid=false). 사용 후 반드시 default로 클리어.
        /// </summary>
        public static FaultDef Injected;

        /// <summary>심각도 → AI 라벨(정상/주의/이상). 코드북 섹션 8 매핑.</summary>
        public static LoadGrade ToGrade(FaultSeverity s) => s switch
        {
            FaultSeverity.Info    => LoadGrade.Normal,   // 정상
            FaultSeverity.Warning => LoadGrade.Caution,  // 주의
            _                     => LoadGrade.Over,     // Critical/Fatal → 이상
        };

        /// <summary>심각도 → 표시색. 코드북 섹션 7(Fatal 적색 / Critical 주황 / Warning 노랑 / Info 회색).</summary>
        public static Color SevColor(FaultSeverity s) => s switch
        {
            FaultSeverity.Fatal    => new Color(0.92f, 0.20f, 0.18f),
            FaultSeverity.Critical => new Color(0.95f, 0.55f, 0.15f),
            FaultSeverity.Warning  => new Color(0.95f, 0.80f, 0.20f),
            _                      => new Color(0.65f, 0.65f, 0.68f),
        };

        /// <summary>심각도 한글 표기.</summary>
        public static string SevLabel(FaultSeverity s) => s switch
        {
            FaultSeverity.Fatal    => "심각",
            FaultSeverity.Critical => "위험",
            FaultSeverity.Warning  => "주의",
            _                      => "정보",
        };

        /// <summary>HUD 한 줄 표기: "[3012] HO 과부하 (정격 초과)".</summary>
        public static string Format(FaultDef f) => $"[{f.Code}] {f.Message}";

        /// <summary>
        /// 현재 크레인 상태에서 활성 알람 1건 판정(코드북 섹션 7 우선순위: Fatal &gt; Critical, 없으면 무효).
        /// 1차년도 자연 발생 8종만. 재료: 하중 등급·갠트리 충돌·축 끝단.
        /// </summary>
        /// <summary>스프레더(권상) 알람 — 과부하/호이스트 끝단.</summary>
        public static FaultDef EvaluateSpreader(StsCrane crane)
        {
            if (crane == null) return default;
            var attach = crane.Attach;
            // 과부하(3012)는 크레인 정격(SWL) 초과 — 컨테이너 ISO 과적(AttachedLoadGrade)이 아니라 SWL %임계로 판정(H1).
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
            // 조용히 "알람 0"으로 정상 운전하는 것을 막는다(재감사 신규결함 #1). 정상 시(IsLoaded=true) 통과 → 거동 불변.
            if (!AlarmCodebook.IsLoaded) return AlarmSystemOffline;
            if (Injected.IsValid) return Injected;   // 시나리오 주입 결함 최우선(데모) — HUD·상태판·OpMode 일괄 반응
            // ※ 기존 구현은 트롤리 Fatal(2012)을 Fatal 우선 티어에서 누락해, 스프레더/갠트리 Critical에
            //    트롤리 충돌(Fatal)이 가려지는 버그가 있었다. 심각도 비교로 일원화해 수정.
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

        /// <summary>
        /// 활성 알람 전부 수집(축별 1건씩, 최대 buf.Length) → 심각도 내림차순 정렬. 반환=개수.
        /// 코드북 §7 우선순위(Sev 높은 순). 동시 다발을 상태판이 리스트로 보일 때 사용.
        /// ※ 동급 내 '최근 발생 우선'은 onset 시각 추적이 필요해 미구현 — 현재는 축 순서(스프레더&gt;갠트리&gt;트롤리)로 안정 정렬.
        ///   buf는 호출부가 재사용(매 프레임 무할당).
        /// </summary>
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
