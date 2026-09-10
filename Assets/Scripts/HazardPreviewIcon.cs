using UnityEngine;
using UnityEngine.UI;

public sealed class HazardPreviewIcon : MonoBehaviour
{
    private const int PreviewLayer = 31;

    [SerializeField] private GameObject sourcePrefab;
    [SerializeField] private int previewIndex;
    private GameObject previewRoot;
    private RenderTexture renderTexture;

    public void Configure(GameObject prefab, int previewIndex)
    {
        if (prefab == null || sourcePrefab == prefab && this.previewIndex == previewIndex && previewRoot != null)
            return;

        Cleanup();
        sourcePrefab = prefab;
        this.previewIndex = previewIndex;
        if (Application.isPlaying)
            BuildPreview();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && sourcePrefab != null && previewRoot == null)
            BuildPreview();
    }

    private void BuildPreview()
    {
        renderTexture = new RenderTexture(160, 160, 16, RenderTextureFormat.ARGB32)
        {
            name = $"{sourcePrefab.name} HUD Preview",
            antiAliasing = 2,
            hideFlags = HideFlags.HideAndDontSave
        };

        RawImage image = GetComponent<RawImage>();
        if (image == null)
            image = gameObject.AddComponent<RawImage>();
        image.texture = renderTexture;
        image.color = Color.white;
        image.raycastTarget = false;

        previewRoot = new GameObject($"{sourcePrefab.name} HUD Preview Root")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        previewRoot.transform.position = new Vector3(10000f + previewIndex * 12f, 10000f, 10000f);

        GameObject model = Instantiate(sourcePrefab, previewRoot.transform);
        SetLayer(model.transform, PreviewLayer);
        CenterAndScale(model, previewRoot.transform.position);

        GameObject cameraObject = new("Preview Camera", typeof(Camera));
        cameraObject.transform.SetParent(previewRoot.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.15f, -3.5f);
        cameraObject.transform.localRotation = Quaternion.identity;
        Camera previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = 1.15f;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 10f;
        previewCamera.targetTexture = renderTexture;

        GameObject lightObject = new("Preview Light", typeof(Light));
        lightObject.transform.SetParent(previewRoot.transform, false);
        lightObject.transform.localRotation = Quaternion.Euler(35f, -35f, 0f);
        Light previewLight = lightObject.GetComponent<Light>();
        previewLight.type = LightType.Directional;
        previewLight.intensity = 1.4f;
        previewLight.cullingMask = 1 << PreviewLayer;
    }

    private static void CenterAndScale(GameObject model, Vector3 center)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (size > 0.001f)
            model.transform.localScale *= 1.65f / size;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        model.transform.position += center - bounds.center;
    }

    private static void SetLayer(Transform target, int layer)
    {
        target.gameObject.layer = layer;
        foreach (Transform child in target)
            SetLayer(child, layer);
    }

    private void OnDisable() => Cleanup();

    private void OnDestroy() => Cleanup();

    private void Cleanup()
    {
        if (previewRoot != null)
            DestroyPreviewObject(previewRoot);
        if (renderTexture != null)
        {
            renderTexture.Release();
            DestroyPreviewObject(renderTexture);
        }
        previewRoot = null;
        renderTexture = null;
    }

    private static void DestroyPreviewObject(Object target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
