using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The right hand module list for the second builder.
//
// Unlike the first builder's panel, this list never changes what it offers: the same seven modules
// are always there, and clicking one picks it up. What changes is the subtitle, which tells the
// player what to do next, and which entry is highlighted as the one in hand.
//
// The panel can be built into the scene by the setup tool and then restyled in the inspector; if the
// references below are empty it puts the same hierarchy together at runtime instead.
[RequireComponent(typeof(ShipBuilder2))]
public class ShipBuilder2UI : MonoBehaviour
{
    [Header("References")]
    public ShipBuilder2 builder;
    public PartThumbnailRenderer thumbnails;

    [Header("Scene Objects (leave empty to build the panel at runtime)")]
    public Canvas canvas;
    public RectTransform panelRect;
    public RectTransform content;
    [Tooltip("Inactive entry cloned once per module. Restyle this one and the whole list follows.")]
    public ModuleListItem itemTemplate;
    public Text titleText;
    public Text subtitleText;
    public Text footerText;
    public Button removeButton;

    [Header("Panel Style")]
    public float panelWidth = 320f;
    public Color panelColor = new Color(0.05f, 0.06f, 0.09f, 0.92f);
    public Color headerColor = new Color(0.09f, 0.11f, 0.16f, 1f);

    [Header("List Style")]
    public int columns = 2;
    public Vector2 cellSize = new Vector2(138f, 166f);
    public Vector2 cellSpacing = new Vector2(8f, 8f);
    public Color itemColor = new Color(0.13f, 0.15f, 0.2f, 1f);
    public Color itemHoverColor = new Color(0.2f, 0.28f, 0.38f, 1f);
    public Color itemHeldColor = new Color(0.85f, 0.5f, 0.12f, 1f);

    [Header("Text Style")]
    public Color titleColor = Color.white;
    public Color subtitleColor = new Color(0.65f, 0.72f, 0.82f, 1f);
    [Tooltip("Subtitle colour while the socket under the pointer is refusing the module.")]
    public Color blockedColor = new Color(1f, 0.45f, 0.35f, 1f);
    public int titleFontSize = 26;
    public int subtitleFontSize = 15;
    public int labelFontSize = 14;

    readonly List<ModuleListItem> items = new List<ModuleListItem>();
    float lastPanelFraction = -1f;

    void Awake()
    {
        if (builder == null) builder = GetComponent<ShipBuilder2>();
        if (thumbnails == null) thumbnails = GetComponent<PartThumbnailRenderer>();
        if (thumbnails == null) thumbnails = gameObject.AddComponent<PartThumbnailRenderer>();

        if (canvas == null) BuildHierarchy();

        if (removeButton != null)
        {
            removeButton.onClick.RemoveAllListeners();
            removeButton.onClick.AddListener(() => builder.RemoveSelectedPart());
        }
    }

    void OnEnable()
    {
        if (builder == null) return;
        builder.HeldModuleChanged += RefreshSelection;
        builder.AssemblyChanged += RefreshStatus;
        builder.PreviewChanged += RefreshStatus;
    }

    void OnDisable()
    {
        if (builder == null) return;
        builder.HeldModuleChanged -= RefreshSelection;
        builder.AssemblyChanged -= RefreshStatus;
        builder.PreviewChanged -= RefreshStatus;
    }

    void Start()
    {
        BuildList();
        RefreshSelection();
    }

    void LateUpdate()
    {
        if (builder == null || canvas == null || panelRect == null || Screen.width <= 0) return;
        if (builder.builderCamera == null) return;

        float fraction = Mathf.Clamp01(panelRect.rect.width * canvas.scaleFactor / Screen.width);
        if (Mathf.Abs(fraction - lastPanelFraction) < 0.002f) return;

        lastPanelFraction = fraction;
        builder.builderCamera.uiPanelFraction = fraction;
    }

    // ---------------------------------------------------------------- population

    // The list is built once: the seven modules never change, only which one is in hand.
    public void BuildList()
    {
        if (content == null || itemTemplate == null || builder == null) return;

        foreach (ModuleListItem item in items)
        {
            if (item != null) Destroy(item.gameObject);
        }
        items.Clear();

        foreach (ShipPartDefinition module in builder.Modules)
        {
            ModuleListItem item = Instantiate(itemTemplate, content);
            item.name = "Module " + module.displayName;

            if (item.thumbnail != null)
            {
                item.thumbnail.texture = thumbnails != null ? thumbnails.GetThumbnail(module.prefab) : null;
            }

            item.Bind(builder, module, itemColor, itemHoverColor, itemHeldColor);
            item.gameObject.SetActive(true);

            var button = item.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    item.OnClicked();
                    RefreshSelection();
                });
            }

            items.Add(item);
        }

        RefreshStatus();
    }

    void RefreshSelection()
    {
        ShipPartDefinition held = builder != null ? builder.HeldModule : null;

        foreach (ModuleListItem item in items)
        {
            if (item != null) item.SetHeld(item.Definition == held);
        }

        RefreshStatus();
    }

    // The subtitle carries the whole instruction, because this screen has a mode and the player
    // needs to know which one they are in.
    void RefreshStatus()
    {
        if (builder == null) return;

        if (subtitleText != null)
        {
            // A refused placement does nothing when clicked, so the reason has to be readable
            // somewhere other than the colour of the hologram.
            if (builder.HeldModule != null && builder.PreviewBlocked)
            {
                subtitleText.text = "Blocked - " + builder.HeldModule.displayName +
                                    " would sit inside what is already there";
                subtitleText.color = blockedColor;
            }
            else if (builder.HeldModule != null)
            {
                subtitleText.text = "Placing " + builder.HeldModule.displayName +
                                    " - hover a highlighted socket, right click to put it back";
                subtitleText.color = subtitleColor;
            }
            else if (builder.SelectedPart != null)
            {
                subtitleText.text = "Selected " + builder.SelectedPart.definition.displayName;
                subtitleText.color = subtitleColor;
            }
            else
            {
                subtitleText.text = "Pick a module, then hover a socket on the ship";
                subtitleText.color = subtitleColor;
            }
        }

        if (removeButton != null)
        {
            removeButton.gameObject.SetActive(builder.HeldModule == null && builder.SelectedPart != null);
        }

        if (footerText != null)
        {
            float mass = builder.TotalMass;
            footerText.text = mass > 0f
                ? string.Format("Modules: {0}    Mass: {1:0} kg", builder.PartCount, mass)
                : string.Format("Modules: {0}", builder.PartCount);
        }
    }

    // ---------------------------------------------------------------- construction

    // Builds the panel and fills in every reference above. Public because the setup tool runs it at
    // edit time, which is what leaves a real hierarchy behind to select and restyle.
    public void BuildHierarchy()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var canvasObject = new GameObject("Ship Builder 2 UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        panelRect = CreateRect("Modules Panel", canvasObject.transform);
        panelRect.anchorMin = new Vector2(1f, 0f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.sizeDelta = new Vector2(panelWidth, 0f);
        panelRect.anchoredPosition = Vector2.zero;
        AddImage(panelRect.gameObject, panelColor);

        var panelLayout = panelRect.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 10, 10, 10);
        panelLayout.spacing = 8f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        BuildHeader(panelRect, font);
        BuildScrollView(panelRect, font);

        footerText = CreateText(panelRect, "Footer", string.Empty, subtitleFontSize, subtitleColor, FontStyle.Normal, font);
        AddLayoutElement(footerText.gameObject, 22f, 0f);
    }

    void BuildHeader(RectTransform panel, Font font)
    {
        RectTransform header = CreateRect("Header", panel);
        AddImage(header.gameObject, headerColor);

        var headerLayout = header.gameObject.AddComponent<VerticalLayoutGroup>();
        headerLayout.padding = new RectOffset(12, 12, 10, 10);
        headerLayout.spacing = 4f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = true;
        headerLayout.childForceExpandHeight = false;

        titleText = CreateText(header, "Title", "Modules", titleFontSize, titleColor, FontStyle.Bold, font);
        AddLayoutElement(titleText.gameObject, titleFontSize + 10f, 0f);

        subtitleText = CreateText(header, "Subtitle", "Pick a module, then hover a socket on the ship",
                                  subtitleFontSize, subtitleColor, FontStyle.Normal, font);
        AddLayoutElement(subtitleText.gameObject, subtitleFontSize * 2f + 12f, 0f);

        removeButton = CreateButton(header, "Remove Button", "Remove Selected", font);
        AddLayoutElement(removeButton.gameObject, 30f, 0f);
        removeButton.gameObject.SetActive(false);
    }

    void BuildScrollView(RectTransform panel, Font font)
    {
        RectTransform scroll = CreateRect("Scroll View", panel);
        AddLayoutElement(scroll.gameObject, 0f, 1f);

        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 32f;

        RectTransform viewport = CreateRect("Viewport", scroll);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.sizeDelta = Vector2.zero;
        viewport.pivot = new Vector2(0f, 1f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;

        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = cellSpacing;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);
        grid.padding = new RectOffset(2, 2, 2, 8);

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport;
        scrollRect.content = content;

        itemTemplate = BuildItemTemplate(content, font);
    }

    ModuleListItem BuildItemTemplate(RectTransform parent, Font font)
    {
        RectTransform itemRect = CreateRect("Item Template", parent);
        Image background = AddImage(itemRect.gameObject, itemColor);

        RectTransform thumbRect = CreateRect("Thumbnail", itemRect);
        thumbRect.anchorMin = new Vector2(0f, 1f);
        thumbRect.anchorMax = new Vector2(1f, 1f);
        thumbRect.pivot = new Vector2(0.5f, 1f);
        thumbRect.sizeDelta = new Vector2(-12f, cellSize.y - 40f);
        thumbRect.anchoredPosition = new Vector2(0f, -6f);

        var raw = thumbRect.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;

        Text label = CreateText(itemRect, "Label", "Module", labelFontSize, titleColor, FontStyle.Normal, font);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.sizeDelta = new Vector2(-8f, 30f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;

        var button = itemRect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = background;

        var item = itemRect.gameObject.AddComponent<ModuleListItem>();
        item.background = background;
        item.thumbnail = raw;
        item.label = label;

        itemRect.gameObject.SetActive(false);
        return item;
    }

    // ---------------------------------------------------------------- uGUI helpers

    static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static Image AddImage(GameObject target, Color color)
    {
        var image = target.AddComponent<Image>();
        image.color = color;
        return image;
    }

    static LayoutElement AddLayoutElement(GameObject target, float minHeight, float flexibleHeight)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.AddComponent<LayoutElement>();

        element.minHeight = minHeight;
        element.preferredHeight = minHeight;
        element.flexibleHeight = flexibleHeight;
        return element;
    }

    static Text CreateText(Transform parent, string name, string value, int size, Color color, FontStyle style, Font font)
    {
        RectTransform rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    Button CreateButton(Transform parent, string name, string caption, Font font)
    {
        RectTransform rect = CreateRect(name, parent);
        Image background = AddImage(rect.gameObject, itemColor);

        Text label = CreateText(rect, "Label", caption, labelFontSize, titleColor, FontStyle.Bold, font);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;
        labelRect.anchoredPosition = Vector2.zero;
        label.alignment = TextAnchor.MiddleCenter;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;

        return button;
    }
}
