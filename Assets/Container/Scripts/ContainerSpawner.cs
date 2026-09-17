using UnityEngine;

namespace AIXRCrane
{
    /// <summary>
    /// 컨테이너 프리팹을 일정 패턴으로 스폰. 각 인스턴스는 ISO 6346 랜덤 번호와 랜덤 색상이 적용된다.
    /// 1차년도 단일 LOD·고정 사이즈(20ft) 기준.
    /// </summary>
    public class ContainerSpawner : MonoBehaviour
    {
        [Header("프리팹·팔레트")]
        [SerializeField] ContainerInstance containerPrefab;
        [SerializeField] ContainerColorPalette palette;

        [Header("스폰 설정")]
        [SerializeField] int spawnCount = 8;
        [SerializeField] Vector3 cellSize = new Vector3(2.5f, 2.6f, 6.1f); // 20ft ISO 외경
        [SerializeField] int columnsPerRow = 4;
        [SerializeField] bool spawnOnStart = true;

        [Header("랜덤 시드 (0이면 시간 기반)")]
        [SerializeField] int seed = 0;

        System.Random rng;

        void Start()
        {
            rng = seed == 0 ? new System.Random() : new System.Random(seed);
            if (spawnOnStart) SpawnAll();
        }

        public void SpawnAll()
        {
            if (containerPrefab == null)
            {
                Debug.LogWarning("[ContainerSpawner] containerPrefab이 비어 있습니다. 메시가 준비되면 프리팹을 연결하세요.");
                return;
            }

            for (int i = 0; i < spawnCount; i++)
            {
                int col = i % columnsPerRow;
                int row = i / columnsPerRow;
                Vector3 localPos = new Vector3(col * cellSize.x, 0f, row * cellSize.z);

                var instance = Instantiate(containerPrefab, transform);
                instance.transform.localPosition = localPos;
                instance.transform.localRotation = Quaternion.identity;
                instance.ApplyRandom(palette, rng);
            }
        }
    }
}
