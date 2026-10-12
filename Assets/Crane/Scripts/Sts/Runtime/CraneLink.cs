using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>세션(Net) 이 호스트 권위 크레인 상태를 내주는 창구 — CraneNetSync 가 스폰 때 등록한다.</summary>
    public interface ICraneNetState
    {
        /// <summary>이 크레인이 네트워크로 동기화되는가.</summary>
        bool Syncs(StsCrane c);
        /// <summary>접속 중이고 이 크레인 상태가 있으면 호스트 권위 알람·운영상태.</summary>
        bool TryState(StsCrane c, out int alarm, out OpMode op);
    }

    /// <summary>세션의 다른 참가자 — 그 사람이 고른 것·가리키는 곳을 내 화면에 그 사람 색으로 그린다(PlayerAvatarSync 가 등록).</summary>
    public interface IRemotePeer
    {
        Color Tint { get; }
        string SelectionKey { get; }
        bool PointerOn { get; }
        Vector3 PointerFrom { get; }
        Vector3 PointerTo { get; }
    }

    /// <summary>런타임(Crane) 이 상위 모듈(Net·Flat)에 묻는 창구 — 상위가 값을 채우고 런타임은 이것만 읽는다(어셈블리 순환 방지).</summary>
    public static class CraneLink
    {
        /// <summary>평면 모드(= VR 이 아닌 모든 실행 = 모바일 관전 화면)로 부팅됐는가 — FlatModeBootstrap 이 쓴다.</summary>
        public static bool FlatMode;

        /// <summary>시작·접속 메뉴가 떠 있는가 — CraneNetMenuHUD 가 쓰고, 다른 시야 고정 HUD 는 겹치지 않게 비켜 준다.</summary>
        public static bool MenuOpen;

        /// <summary>XREAL 안경 머리 추적이 붙어 있는가 — XrealHeadTracker 가 자기 Connected 를 건다.</summary>
        public static System.Func<bool> GlassesConnected = () => false;

        /// <summary>모바일 탭 판정 — 이보다 짧게 누르면 탭(선택), 길게 누르면 비행(FlatPlayerRig).</summary>
        public const float TapMaxS = 0.25f;

        /// <summary>세션 크레인 상태 — CraneNetSync 가 스폰 때 걸고 디스폰 때 뗀다. null = 단독/미접속.</summary>
        public static ICraneNetState NetState;

        /// <summary>세션의 남(자기 제외) — PlayerAvatarSync 가 스폰 때 넣고 디스폰 때 뺀다.</summary>
        public static readonly List<IRemotePeer> Peers = new List<IRemotePeer>();

        /// <summary>이 크레인이 네트워크로 동기화되는 크레인인가 — 관전자 말풍선이 호스트 알람을 붙일지·PLC 를 직접 돌릴지 판단.</summary>
        public static bool Syncs(StsCrane c) => NetState != null && NetState.Syncs(c);

        /// <summary>활성 알람 코드 SSOT — 접속 중이면 그 크레인의 호스트 권위값, 아니면 로컬 판정. 0=이상 없음.</summary>
        public static int ActiveAlarmCode(StsCrane crane)
        {
            if (NetState != null && NetState.TryState(crane, out int alarm, out _)) return alarm;
            var f = CraneFault.Evaluate(crane);   // 단독/미접속 — 로컬 판정(호스트 시점과 동일)
            return f.IsValid ? f.Code : 0;
        }

        /// <summary>운영상태 SSOT — 접속 중이면 그 크레인의 호스트 권위값, 아니면 로컬 판정.</summary>
        public static OpMode ActiveOpMode(StsCrane crane)
        {
            if (NetState != null && NetState.TryState(crane, out _, out var op)) return op;
            return crane != null ? crane.OpMode.Current : OpMode.Stopped;   // 단독/미접속 — 로컬 판정
        }
    }
}
