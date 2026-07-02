using System.IO;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    // [임시 도구] 트리거 파일이 있으면 도메인 리로드 시 자동으로 트럭 PNG를 렌더한다.
    //   외부(클로드)가 트리거 파일을 만들고 에디터가 컴파일되면 클릭 없이 렌더 → 스크린샷 확인용.
    //   확인이 끝나면 이 파일은 삭제해도 무방.
    [InitializeOnLoad]
    static class TruckShotAuto
    {
        const string Trigger = "/private/tmp/claude-501/-Users-seoyeonsoft-Container/a70aec1c-b7b6-4e16-9ff1-5f58abd065d6/scratchpad/.render_truck_trigger";

        static TruckShotAuto()
        {
            if (!File.Exists(Trigger)) return;
            try { File.Delete(Trigger); } catch { }
            EditorApplication.delayCall += () =>
            {
                try { StsCraneCreator.RenderTruckPng(); }
                catch (System.Exception e) { Debug.LogError("[TruckShotAuto] " + e); }
            };
        }
    }
}

// bump 1782452456
// bump 1782452761
// bump 1782893000
