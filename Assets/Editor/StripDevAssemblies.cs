#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AIXRCrane.EditorTools
{
    /// <summary>납품 빌드(WBS 8.2)에서 개발 도구 런타임(Unity MCP, MCPForUnity.*)을 뺀다 — 에디터 조작용이라 플레이어엔 쓸모없다. Development 빌드엔 그대로 둔다.</summary>
    sealed class StripDevAssemblies : IFilterBuildAssemblies
    {
        const string Prefix = "MCPForUnity.";

        public int callbackOrder => 0;

        public string[] OnFilterAssemblies(BuildOptions options, string[] assemblies)
        {
            if ((options & BuildOptions.Development) != 0) return assemblies;
            var kept = assemblies.Where(a => !Path.GetFileName(a).StartsWith(Prefix)).ToArray();
            if (kept.Length != assemblies.Length)
                Debug.Log($"[StripDev] 납품 빌드 — 개발 도구 어셈블리 {assemblies.Length - kept.Length}개 뺌({Prefix}*)");
            return kept;
        }
    }
}
#endif
