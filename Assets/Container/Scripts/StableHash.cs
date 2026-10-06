namespace AIXRCrane
{
    /// <summary>결정적 FNV-1a 해시 유틸 — ★ string.GetHashCode 는 런타임마다 달라 금지.
    /// Hash01(s[,salt]) 0~1(salt=채널 분리), Seed(s)는 avalanche 없는 System.Random 시드.</summary>
    public static class StableHash
    {
        /// <summary>결정적 해시 → 0~1 (FNV-1a + avalanche).</summary>
        public static float Hash01(string s) => string.IsNullOrEmpty(s) ? 0f : Hash01(s, 0u);

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
