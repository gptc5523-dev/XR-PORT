using UnityEngine;
using UnityEngine.XR;            // InputDevices — Quest 컨트롤러 직접 읽기(OpenXR 액션 에셋 비의존)

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class StsCraneVRController
    {
        // 운전실 시점 (카메라를 운전실 좌석 눈높이 앵커로 이동, 크기 변경 없음)
        void EnterCabView()
        {
            var cam = Camera.main;
            var trolleyT = (crane.Trolley as Component)?.transform;
            if (cam == null || trolleyT == null)
            {
                if (debugLog) Debug.LogWarning("[Crane] 운전실 시점 실패 — Main 카메라 또는 트롤리를 못 찾음");
                return;
            }
            cabAnchor = FindCabAnchor(trolleyT, out bool dedicated);
            rig = cam.transform.root;          // XR Origin 루트 이동(카메라+컨트롤러 함께)
            savedRigPos = rig.position;
            savedRigRot = rig.rotation;
            rigSaved = true;

            // 리그가 크레인/컨테이너 속으로 들어가 물리적으로 밀어버리지 않도록, 리그 콜라이더(손·몸 충돌체)를 잠시 끔.
            //   (CharacterController도 Collider 하위라 함께 꺼짐. 시점 나갈 때 복구.)
            rigColliders.Clear();
            foreach (var c in rig.GetComponentsInChildren<Collider>(true))
                if (c.enabled) { c.enabled = false; rigColliders.Add(c); }

            // 카메라를 운전실 시점으로 평행 이동. 전용 앵커(Cab_Viewpoint)면 바닥 패널 아래로 눈높이를 내려
            // 발밑 화물을 보이게 하고, 레거시 앵커면 기준부품+오프셋. 바닥은 앵커 유무와 무관하게 찾는다.
            Transform cabFloor = FindCabFloor(trolleyT, cabFloorAnchorName);
            Transform eyeAnchor = cabFloor != null ? cabFloor : cabAnchor;
            Vector3 target = cabFloor != null ? BelowCabFloor(cabFloor, cabFloorDropDown)             // 바닥 패널 '아래'
                           : dedicated         ? cabAnchor.position                                    // 바닥 폴백 → 좌석 눈높이
                                               : cabAnchor.position + trolleyT.rotation * cabLocalOffset;
            // 시선(요) 정렬 — 전용 앵커면 운전실 전방, 앵커 없이 바닥만 있으면(RTG) 스프레더 쪽. 상하 피치는 머리에 맡김(고개 숙여 내려다봄).
            Vector3 fwd = dedicated ? cabAnchor.forward
                        : cabFloor != null && crane.Spreader is Component sp ? sp.transform.position - target
                        : Vector3.zero;
            fwd.y = 0f;
            Vector3 camFwd = cam.transform.forward; camFwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f && camFwd.sqrMagnitude > 1e-4f)
                rig.rotation = Quaternion.FromToRotation(camFwd.normalized, fwd.normalized) * rig.rotation;
            rig.position += target - cam.transform.position;   // (회전 후) 카메라를 시점으로 정렬
            lastTrolleyPos = trolleyT.position;

            cabView = true;
            Haptic(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), 0.4f, 0.06f);
            if (debugLog) Debug.Log($"[Crane] A → 운전실 시점 ON — 시선기준 '{cabAnchor.name}', 눈위치 '{eyeAnchor.name}'" +
                (cabFloor != null ? $" (바닥 아래 {cabFloorDropDown:0.###} 드롭·시선 스프레더 정렬)"
                 : dedicated       ? " (좌석 눈높이 폴백·시선 스프레더 정렬)"
                                   : $", 오프셋 {cabLocalOffset}") + " (고개 숙여 아래를 보세요)");
        }

        // 시점 기준 부품 찾기 — 트롤리 하위에서 이름으로(재귀). 우선순위: Cab_Viewpoint → cabAnchorName → 트롤리 본체.
        // 전용 앵커면 dedicated=true → EnterCabView 가 오프셋 0 + 전방(스프레더) 시선정렬 적용.
        Transform FindCabAnchor(Transform trolleyT, out bool dedicated)
        {
            dedicated = false;
            foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == StsPartNames.CabViewpoint) { dedicated = true; return t; }
            if (!string.IsNullOrEmpty(cabAnchorName))
                foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
                    if (CraneHud.BaseName(t.name) == cabAnchorName) return t;
            return trolleyT;
        }

        // 운전실 바닥 부품 찾기 — 눈 위치를 이 바닥 아래에 둬 발밑 화물을 보이게 한다.
        // 우선순위: 직렬화 이름 → Cab_Fb_FloorRear → RTG 바닥. 못 찾으면 null(좌석 눈높이로 폴백).
        static Transform FindCabFloor(Transform trolleyT, string preferredName)
        {
            Transform byName = null, byRear = null, byRtg = null;
            foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
            {
                string bn = CraneHud.BaseName(t.name);
                if (byName == null && !string.IsNullOrEmpty(preferredName) && bn == preferredName) byName = t;
                if (byRear == null && bn == StsPartNames.CabFloorRear) byRear = t;
                if (byRtg == null && bn == StsPartNames.RtgCabFloor) byRtg = t;
            }
            return byName != null ? byName : byRear != null ? byRear : byRtg;
        }

        /// <summary>바닥 패널 중심에서 drop 만큼 아래 — 운전실 시점 눈 위치.
        /// FBX RTG 는 피벗이 트롤리 원점이라 렌더러 바운즈 중심을 쓴다(STS 는 피벗=중심이라 값이 같다).</summary>
        static Vector3 BelowCabFloor(Transform floor, float drop)
        {
            var r = floor.GetComponent<Renderer>();
            return (r != null ? r.bounds.center : floor.position) + Vector3.down * drop;
        }

        // 트롤리가 움직인 만큼 시점도 같이 이동 — 운전실이 트롤리에 붙어 따라가게
        void FollowTrolley()
        {
            var trolleyT = (crane.Trolley as Component)?.transform;
            if (rig == null || trolleyT == null) return;
            rig.position += trolleyT.position - lastTrolleyPos;
            lastTrolleyPos = trolleyT.position;
        }

        void ExitCabView()
        {
            if (rigSaved && rig != null) { rig.position = savedRigPos; rig.rotation = savedRigRot; }
            foreach (var c in rigColliders) if (c != null) c.enabled = true;
            rigColliders.Clear();
            cabView = false;
            rigSaved = false;
            if (debugLog) Debug.Log("[Crane] A → 운전실 시점 OFF (원위치 복귀)");
        }
    }
}
