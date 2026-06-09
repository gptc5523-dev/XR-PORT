using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class CubeReset : MonoBehaviour
{
    public enum PlacementMode { CameraFront, TableSurface, Auto }

    [Header("초기 배치")]
    [SerializeField] PlacementMode placementMode = PlacementMode.Auto;
    [SerializeField] float distanceFromCamera = 0.5f;
    [SerializeField] float verticalOffset = 0f;
    [SerializeField] float horizontalOffset = 0f;
    [Tooltip("true면 카메라 Y 무시하고 absoluteHeight 사용 (Tracking Origin 모드 무관 — 가장 견고)")]
    [SerializeField] bool useAbsoluteHeight = true;
    [SerializeField] float absoluteHeight = 1.4f;

    [Header("복귀 동작")]
    [SerializeField] float resetDelayAfterRelease = 60f;
    [SerializeField] float distanceLimit = 50f;

    Vector3 startPos;
    Quaternion startRot;
    Rigidbody rb;
    XRGrabInteractable grab;
    float releasedAt = -1f;
    bool held;
    bool placedOnTable;
    bool initialPlaced;

    public void SetHorizontalOffset(float offset) => horizontalOffset = offset;
    public void SetVerticalOffset(float offset) => verticalOffset = offset;
    public void SetPlacementMode(PlacementMode mode) => placementMode = mode;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();

        PlaceInFrontOfCamera();
        initialPlaced = true;

        grab.selectEntered.AddListener(_ => { held = true; releasedAt = -1f; });
        grab.selectExited.AddListener(_ =>
        {
            held = false;
            releasedAt = Time.time;
            rb.useGravity = true;
            rb.isKinematic = false;
        });
    }

    void Update()
    {
        if (!initialPlaced) return;

        if (placementMode != PlacementMode.CameraFront && !placedOnTable && !held)
        {
            TryPlaceOnTable();
        }

        if (Vector3.Distance(transform.position, startPos) > distanceLimit)
        {
            ResetNow();
            return;
        }

        if (!held && releasedAt > 0f && Time.time - releasedAt > resetDelayAfterRelease)
        {
            ResetNow();
        }
    }

    void PlaceInFrontOfCamera()
    {
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;
        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        // 리그가 1/24로 축소되면 카메라(플레이어)도 축소된다. '플레이어 앞 거리'와 '절대 배치 높이'는
        //   1:1 가정값(0.5m/1.4m)이라 그대로 쓰면 작아진 플레이어 기준 컨테이너가 크레인 높이로 떠버린다.
        //   → 플레이어 기준 거리·높이를 리그 스케일에 맞춘다. (스케일 1이면 기존과 동일 — 하위호환.)
        //   단 horizontalOffset/verticalOffset(2x2 그리드 간격)은 이미 모델(1/24) 치수라 스케일하지 않는다.
        float rigScale = cam.lossyScale.y;
        Vector3 pos = cam.position + forward * (distanceFromCamera * rigScale) + right * horizontalOffset;
        if (useAbsoluteHeight)
            // 실척(스케일≈1): 절대높이 1.4m. 축소 리그: 절대높이는 거인 기준이라 무의미 → 눈높이 기준으로 둔다.
            pos.y = (rigScale > 0.99f ? absoluteHeight : cam.position.y) + verticalOffset;
        else
            pos.y = cam.position.y + verticalOffset;

        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(right, Vector3.up);

        startPos = transform.position;
        startRot = transform.rotation;
    }

    void TryPlaceOnTable()
    {
        var mgr = TablePlacementManager.Instance;
        if (mgr == null || !mgr.HasSurface) return;

        if (!mgr.TryGetSurfacePose(horizontalOffset, out var pos, out _)) return;

        // 2x2 스택용 — 위 row 는 verticalOffset 만큼 더 높이.
        pos.y += verticalOffset;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(pos, startRot);

        startPos = pos;
        placedOnTable = true;
    }

    void ResetNow()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(startPos, startRot);
        releasedAt = -1f;
    }
}
