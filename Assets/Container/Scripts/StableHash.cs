namespace ContainerProject
{
    /// <summary>
    /// 결정적(deterministic) FNV-1a 해시 단일 유틸 — 같은 입력 = 같은 출력(비트 단위 재현).
    /// string.GetHashCode는 런타임마다 달라져 재현성이 깨지므로 사용하지 않는다.
    /// (이전엔 ContainerLoad/ContainerInstance/ContainerIdGenerator/VRTestMenu 4곳에 복제돼 있던 것을 통합.
    ///  알고리즘·상수·믹싱 순서는 한 글자도 바꾸지 않고 그대로 이식 — '구현 단일화'이지 '값 변경'이 아님.)
    ///
    /// 시그니처가 셋인 이유(서로 출력 계약이 다름 — 통합 불가, 각 사본을 그대로 보존):
    ///   · Hash01(s)         : 무염(無salt). 빈 문자열은 avalanche 전에 0f로 조기 반환.
    ///   · Hash01(s, salt)   : salt를 offset basis에 XOR. 빈 문자열도 avalanche를 거침(조기 반환 없음).
    ///   · Seed(s)           : finalizer(avalanche) 없는 순수 FNV-1a 32bit, System.Random 시드용 int 반환.
    /// </summary>
    public static class StableHash
    {
        /// <summary>결정적 해시 → 0~1. (string.GetHashCode는 런타임마다 달라져 재현성 깨짐 → FNV-1a + avalanche)</summary>
        public static float Hash01(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            uint h = 2166136261u;                       // FNV-1a offset basis
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            // avalanche 믹싱(Murmur3 finalizer 계열). FNV 하위 비트는 품질이 나빠
            // 끝 글자(_A/_B)만 다른 이름의 무게가 거의 같아지는 문제 → 전 비트로 확산해 보정.
            h ^= h >> 16; h *= 0x7feb352du;
            h ^= h >> 15; h *= 0x846ca68bu;
            h ^= h >> 16;
            return h / 4294967296f;                     // 2^32 → 0~1 (상위까지 고르게 분산)
        }

        /// <summary>결정적 0~1 (FNV-1a + avalanche, salt로 채널 분리). salt를 offset basis에 XOR.</summary>
        public static float Hash01(string s, uint salt)
        {
            uint h = 2166136261u ^ salt;
            if (!string.IsNullOrEmpty(s))
                foreach (char c in s) { h ^= c; h *= 16777619u; }
            h ^= h >> 16; h *= 0x7feb352du;
            h ^= h >> 15; h *= 0x846ca68bu;
            h ^= h >> 16;
            return h / 4294967296f;
        }

        /// <summary>문자열 → 결정적 int 시드 (FNV-1a 32bit, finalizer 없음). System.Random 시드용.</summary>
        public static int Seed(string s)
        {
            uint h = 2166136261u;
            if (!string.IsNullOrEmpty(s))
                foreach (char c in s) { h ^= c; h *= 16777619u; }
            return unchecked((int)h);
        }
    }
}
