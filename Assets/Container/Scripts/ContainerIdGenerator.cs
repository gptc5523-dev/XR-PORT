using System;
using System.Collections.Generic;
using System.Text;

namespace ContainerProject
{
    /// <summary>
    /// ISO 6346 표준 컨테이너 식별 번호 생성기.
    /// 형식: AAAA NNNNNN C  (4자 소유자 코드 + 6자리 일련번호 + 1자리 체크디지트)
    /// 예) MRBU 200125 8
    /// </summary>
    public static class ContainerIdGenerator
    {
        // 실재하는 주요 선사/리스사 소유자 코드 (Category Identifier 'U' 포함)
        static readonly string[] OwnerCodes = new[]
        {
            "MAEU", "MSKU", "MRKU",       // Maersk
            "MSCU", "MEDU",                // MSC
            "CMAU", "CGMU",                // CMA CGM
            "HLBU", "HLXU",                // Hapag-Lloyd
            "OOLU", "OOCU",                // OOCL
            "EGHU", "EISU", "EMCU",        // Evergreen
            "TCLU", "TCNU", "TGHU",        // Triton
            "BEAU", "BMOU",                // Beacon
            "GESU", "GLDU",                // Genstar
            "TEMU", "FCIU",                // Textainer
            "MRBU"                         // Mercury (이미지 예시)
        };

        // ISO 6346 체크디지트 산출용 문자→숫자 매핑 (11의 배수는 건너뜀)
        static readonly Dictionary<char, int> LetterValues = new Dictionary<char, int>
        {
            {'A',10},{'B',12},{'C',13},{'D',14},{'E',15},{'F',16},{'G',17},{'H',18},{'I',19},
            {'J',20},{'K',21},{'L',23},{'M',24},{'N',25},{'O',26},{'P',27},{'Q',28},{'R',29},
            {'S',30},{'T',31},{'U',32},{'V',34},{'W',35},{'X',36},{'Y',37},{'Z',38}
        };

        /// <summary>
        /// 키 문자열에서 '결정적으로' 컨테이너 번호 생성 — 같은 키면 항상 같은 번호(재현 가능).
        /// 컨테이너 GameObject 이름 등 고정 식별자를 키로 주면, 그 컨테이너는 늘 동일 ISO 6346 번호를 갖는다.
        /// </summary>
        public static string GenerateDeterministic(string key)
        {
            return Generate(new System.Random(StableHash.Seed(key)));
        }

        /// <summary>랜덤 컨테이너 번호 생성. 시드 없이 호출하면 매번 다른 번호.</summary>
        public static string Generate(System.Random rng = null)
        {
            rng ??= new System.Random();
            string owner = OwnerCodes[rng.Next(OwnerCodes.Length)];
            int serial = rng.Next(0, 1000000);
            string serialStr = serial.ToString("D6");
            int check = CalculateCheckDigit(owner, serialStr);
            return $"{owner}{serialStr}{check}";
        }

        /// <summary>표시용 포맷 (공백 + 체크디지트 박스). 예: "MRBU 200125 [8]"</summary>
        public static string FormatForDisplay(string rawId)
        {
            if (string.IsNullOrEmpty(rawId) || rawId.Length != 11) return rawId;
            return $"{rawId.Substring(0, 4)} {rawId.Substring(4, 6)} [{rawId[10]}]";
        }

        /// <summary>ISO 6346 체크디지트 계산. 결과 10은 0으로 치환.</summary>
        public static int CalculateCheckDigit(string ownerCode, string serial)
        {
            string combined = ownerCode + serial;
            int sum = 0;
            for (int i = 0; i < 10; i++)
            {
                char c = combined[i];
                int value = char.IsDigit(c) ? c - '0' : LetterValues[c];
                sum += value * (1 << i); // 2^i
            }
            int check = sum % 11;
            return check == 10 ? 0 : check;
        }

        /// <summary>주어진 번호가 ISO 6346 체크디지트를 만족하는지 검증.</summary>
        public static bool Validate(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 11) return false;
            string owner = id.Substring(0, 4);
            string serial = id.Substring(4, 6);
            if (!int.TryParse(id.Substring(10, 1), out int actual)) return false;
            return CalculateCheckDigit(owner, serial) == actual;
        }
    }
}
