using UnityEngine;

namespace AIXRCrane
{
    /// <summary>컨테이너 프리팹의 표시 번호·하중. 비어 있으면 읽는 쪽(SpreaderAttach·QuayContainerScanner·PlcLiveMenu)이 이름 시드로 만든다.</summary>
    public class ContainerInstance : MonoBehaviour
    {
        [SerializeField] string displayId;
        [Tooltip("스프레더가 잡으면 이 값이 권상 하중이 된다.")]
        [SerializeField] float loadTons;

        public string DisplayId => displayId;
        public float LoadTons => loadTons;
    }
}
