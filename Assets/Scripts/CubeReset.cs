using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class CubeReset : MonoBehaviour
{
    [Header("초기 배치")]
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
    bool initialPlaced;

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

        // 축소 리그면 거리(1:1 가정값)를 rigScale 로 맞춘다. offset 들은 이미 모델 치수라 그대로.
        float rigScale = cam.lossyScale.y;
        Vector3 pos = cam.position + forward * (distanceFromCamera * rigScale) + right * horizontalOffset;
        if (useAbsoluteHeight)
            // 실척이면 절대높이, 축소 리그면 절대높이가 무의미해 눈높이 기준.
            pos.y = (rigScale > 0.99f ? absoluteHeight : cam.position.y) + verticalOffset;
        else
            pos.y = cam.position.y + verticalOffset;

        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(right, Vector3.up);

        startPos = transform.position;
        startRot = transform.rotation;
    }

    void ResetNow()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(startPos, startRot);
        releasedAt = -1f;
    }
}
