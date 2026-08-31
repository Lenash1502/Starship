using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Draws the little model previews for the part list.
//
// Rather than baking icon assets that would rot every time a prefab changes, this renders the real
// prefab into a RenderTexture on demand and caches it. The rig sits far away from the build stand
// and lights itself with short range point lights, so it cannot leak into (or be lit by) the scene
// the player is actually looking at. One part is rendered per frame, which keeps opening a category
// of thirty wings off the frame budget.
public class PartThumbnailRenderer : MonoBehaviour
{
    [Header("Output")]
    public int resolution = 160;
    public Color background = new Color(0.08f, 0.09f, 0.12f, 1f);

    [Header("Framing")]
    [Tooltip("Rotation applied to the part before it is photographed.")]
    public Vector3 viewAngles = new Vector3(15f, 145f, 0f);
    [Tooltip("Padding around the part inside the icon. 1 is a tight fit.")]
    public float zoom = 1.15f;

    [Header("Lighting")]
    public float keyLightIntensity = 6f;
    public float fillLightIntensity = 2f;
    public Color keyLightColor = new Color(1f, 0.97f, 0.9f);
    public Color fillLightColor = new Color(0.55f, 0.7f, 1f);

    [Header("Rig")]
    [Tooltip("Where the photo booth is parked, well away from anything else in the scene.")]
    public Vector3 rigOrigin = new Vector3(0f, 10000f, 0f);

    readonly Dictionary<GameObject, RenderTexture> cache = new Dictionary<GameObject, RenderTexture>();
    readonly Queue<GameObject> pending = new Queue<GameObject>();

    Transform rig;
    Camera rigCamera;
    Light keyLight;
    Light fillLight;

    void Awake()
    {
        BuildRig();
        StartCoroutine(RenderPending());
    }

    void OnDestroy()
    {
        foreach (RenderTexture texture in cache.Values)
        {
            if (texture != null) texture.Release();
        }
        cache.Clear();
    }

    // Hands back the texture for this prefab straight away so a RawImage can bind to it. The first
    // request comes back blank and fills in within a frame or two.
    public RenderTexture GetThumbnail(GameObject prefab)
    {
        if (prefab == null) return null;

        if (cache.TryGetValue(prefab, out RenderTexture existing) && existing != null) return existing;

        var texture = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32)
        {
            name = "Thumb_" + prefab.name,
            useMipMap = false,
            filterMode = FilterMode.Bilinear
        };
        texture.Create();

        cache[prefab] = texture;
        pending.Enqueue(prefab);
        return texture;
    }

    void BuildRig()
    {
        var rigObject = new GameObject("Thumbnail Rig");
        rigObject.transform.SetParent(transform, false);
        rigObject.transform.position = rigOrigin;
        rig = rigObject.transform;

        var cameraObject = new GameObject("Thumbnail Camera");
        cameraObject.transform.SetParent(rig, false);
        rigCamera = cameraObject.AddComponent<Camera>();
        rigCamera.clearFlags = CameraClearFlags.SolidColor;
        rigCamera.backgroundColor = background;
        rigCamera.orthographic = true;
        rigCamera.nearClipPlane = 0.01f;
        rigCamera.enabled = false;

        UniversalAdditionalCameraData cameraData = rigCamera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.requiresColorOption = CameraOverrideOption.Off;
        cameraData.requiresDepthOption = CameraOverrideOption.Off;

        keyLight = CreateLight("Key Light", keyLightColor, keyLightIntensity);
        fillLight = CreateLight("Fill Light", fillLightColor, fillLightIntensity);
    }

    Light CreateLight(string lightName, Color color, float intensity)
    {
        var lightObject = new GameObject(lightName);
        lightObject.transform.SetParent(rigCamera.transform, false);

        Light light = lightObject.AddComponent<Light>();
        // Point lights, not directional: a directional light would happily light the whole scene,
        // while a point light with a small range physically cannot reach the build stand.
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        return light;
    }

    IEnumerator RenderPending()
    {
        var endOfFrame = new WaitForEndOfFrame();

        while (true)
        {
            if (pending.Count == 0)
            {
                yield return null;
                continue;
            }

            GameObject prefab = pending.Dequeue();
            if (prefab == null || !cache.TryGetValue(prefab, out RenderTexture target) || target == null)
            {
                continue;
            }

            GameObject instance = Instantiate(prefab, rig);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(viewAngles);
            instance.hideFlags = HideFlags.HideInHierarchy;

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

            // The blockout models parked in the part's own sockets are not the part. Left in, every
            // wing icon would be a wing wearing ghost guns, and the framing below would zoom out to
            // hold them all.
            SocketPlaceholder.SuppressAllIn(instance);

            FrameInstance(instance);
            rigCamera.targetTexture = target;
            rigCamera.enabled = true;

            yield return endOfFrame;

            rigCamera.enabled = false;
            rigCamera.targetTexture = null;
            Destroy(instance);
        }
    }

    // Points the camera at the part and zooms the orthographic box to just contain it, so a tail fin
    // and a full hull both fill their icon.
    void FrameInstance(GameObject instance)
    {
        var renderers = new List<Renderer>();
        instance.GetComponentsInChildren(true, renderers);

        Bounds bounds = new Bounds(rig.position, Vector3.one);
        bool initialised = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!initialised)
            {
                bounds = renderer.bounds;
                initialised = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        float radius = Mathf.Max(0.05f, bounds.extents.magnitude);

        rigCamera.transform.position = bounds.center - Vector3.forward * (radius * 4f);
        rigCamera.transform.rotation = Quaternion.identity;
        rigCamera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * zoom + radius * 0.05f;
        rigCamera.farClipPlane = radius * 10f;
        rigCamera.backgroundColor = background;

        keyLight.transform.localPosition = new Vector3(1.5f, 1.8f, 0.5f) * radius;
        keyLight.range = radius * 12f;
        keyLight.intensity = keyLightIntensity;
        keyLight.color = keyLightColor;

        fillLight.transform.localPosition = new Vector3(-1.8f, -0.6f, 0.8f) * radius;
        fillLight.range = radius * 12f;
        fillLight.intensity = fillLightIntensity;
        fillLight.color = fillLightColor;
    }
}
