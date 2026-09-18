using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>Quest 고정 포비티드 렌더링(FFR) — 시야 주변부 셰이딩 해상도를 낮춰 GPU 부하를 줄인다(화질 손실 거의 없음).
    /// Quest 3는 시선추적 미사용이라 고정 FFR 사용. GPU 바운드일 때만 효과 있음. 끄려면 파일 삭제 또는 level=0.</summary>
    [DisallowMultipleComponent]
    public sealed class FoveatedRenderingEnabler : MonoBehaviour
    {
        [Tooltip("0=꺼짐 ~ 1=가장 강함(GPU 절감 최대, 주변부 가장 거침). 버벅이면 1, 주변 거슬리면 0.6~0.8.")]
        [SerializeField, Range(0f, 1f)] float level = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<FoveatedRenderingEnabler>("FFR");

        IEnumerator Start()
        {
            var displays = new List<XRDisplaySubsystem>();
            XRDisplaySubsystem disp = null;

            // XR 디스플레이가 뜰 때까지 최대 10초 대기(씬 로드 직후엔 아직 running 아닐 수 있음).
            for (float t = 0f; t < 10f && disp == null; t += Time.unscaledDeltaTime)
            {
                SubsystemManager.GetSubsystems(displays);
                foreach (var d in displays)
                    if (d != null && d.running) { disp = d; break; }
                if (disp != null) break;
                yield return null;
            }

            if (disp == null)
            {
                Debug.LogWarning("[FFR] XRDisplaySubsystem(running) 없음 — 에디터/비VR이면 정상. 적용 생략.");
                yield break;
            }

            disp.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;   // 고정(시선추적 X)
            disp.foveatedRenderingLevel = Mathf.Clamp01(level);
            Debug.Log($"[FFR] 고정 포비티드 렌더링 ON — level={disp.foveatedRenderingLevel:0.00} " +
                      "(주변부 셰이딩 해상도 ↓로 GPU 절감)");
        }
    }
}
