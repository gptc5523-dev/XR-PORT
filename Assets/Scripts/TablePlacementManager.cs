using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class TablePlacementManager : MonoBehaviour
{
    public static TablePlacementManager Instance { get; private set; }

    [SerializeField] ARPlaneManager planeManager;
    [SerializeField] float minPlaneArea = 0.1f;
    [SerializeField] float heightAboveSurface = 0.03f;
    [Tooltip("이 높이(m) 이하 수평면은 바닥으로 간주하고 무시")]
    [SerializeField] float minPlaneHeight = 0.3f;
    [Tooltip("이 높이(m) 이상 수평면은 천장으로 간주하고 무시")]
    [SerializeField] float maxPlaneHeight = 2.0f;

    bool hasSurface;
    Vector3 surfacePos;
    Quaternion surfaceRot;
    Vector2 surfaceSize;
    GameObject surfaceCollider;

    public bool HasSurface => hasSurface;

    void Awake()
    {
        Instance = this;
        if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (planeManager == null) return;

        ARPlane best = null;
        float bestArea = minPlaneArea;

        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != PlaneAlignment.HorizontalUp) continue;

            float planeY = plane.transform.position.y;
            if (planeY < minPlaneHeight) continue;
            if (planeY > maxPlaneHeight) continue;

            float area = plane.size.x * plane.size.y;
            if (area > bestArea)
            {
                bestArea = area;
                best = plane;
            }
        }

        if (best != null)
        {
            surfacePos = best.transform.position;
            surfaceRot = best.transform.rotation;
            surfaceSize = best.size;
            hasSurface = true;
            UpdateSurfaceCollider();
        }
    }

    void UpdateSurfaceCollider()
    {
        if (surfaceCollider == null)
        {
            surfaceCollider = new GameObject("VirtualTableCollider");
            surfaceCollider.AddComponent<BoxCollider>();
        }
        surfaceCollider.transform.position = surfacePos;
        surfaceCollider.transform.rotation = Quaternion.Euler(0f, surfaceRot.eulerAngles.y, 0f);
        var col = surfaceCollider.GetComponent<BoxCollider>();
        col.size = new Vector3(Mathf.Max(surfaceSize.x, 0.3f), 0.02f, Mathf.Max(surfaceSize.y, 0.3f));
        col.center = Vector3.zero;
    }

    public bool TryGetSurfacePose(float horizontalOffset, out Vector3 position, out Quaternion rotation)
    {
        if (!hasSurface)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        Vector3 right = surfaceRot * Vector3.right;
        position = surfacePos + right * horizontalOffset + Vector3.up * heightAboveSurface;
        rotation = Quaternion.Euler(0f, surfaceRot.eulerAngles.y, 0f);
        return true;
    }
}
