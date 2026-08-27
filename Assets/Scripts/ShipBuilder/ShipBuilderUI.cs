using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Drives the right hand parts panel.
//
// The panel can live in the scene as ordinary uGUI objects, built once by
// Tools > Ship Builder > Create UI In Scene and then restyled in the inspector like anything else.
// If the references below are empty it falls back to building the same hierarchy at runtime, so the
// menu still works in a scene nobody has dressed yet.
//
// Everything the list needs is driven through these references, so moving, recolouring or
// re-fonting any of it is safe: only the wiring matters, not the layout.
[RequireComponent(typeof(ShipBuilder))]
public class ShipBuilderUI : MonoBehaviour
{
    [Header("References")]
    public ShipBuilder builder;
    public PartThumbnailRenderer thumbnails;

    [Header("Scene Objects (leave empty to build the panel at runtime)")]
    [Tooltip("Root canvas of the panel. Its presence is what tells the component the panel was " +
             "authored in the scene rather than generated on play.")]
    public Canvas canvas;
    [Tooltip("The panel itself. Its width decides how much of the view the ship has to fit into.")]
    public RectTransform panelRect;
    [Tooltip("Parent the part entries are spawned under, normally carrying a GridLayoutGroup.")]
    public RectTransform content;
    [Tooltip("Inactive entry cloned once per offered part. Restyle this one and the whole list follows.")]
    public PartListItem itemTemplate;
    public Text titleText;
    public Text subtitleText;
    public Text footerText;
    [Tooltip("Shown when a mount has no parts to offer.")]
    public Text emptyText;
    public Button coreButton;
    public Button removeButton;

    [Header("Panel Style")]
    public float panelWidth = 360f;
    public Color panelColor = new Color(0.05f, 0.06f, 0.09f, 0.92f);
    public Color headerColor = new Color(0.09f, 0.11f, 0.16f, 1f);

    [Header("List Style")]
    public int columns = 2;
    public Vector2 cellSize = new Vector2(158f, 186f);
    public Vector2 cellSpacing = new Vector2(8f, 8f);
    public Color itemColor = new Color(0.13f, 0.15f, 0.2f, 1f);
    public Color itemHoverColor = new Color(0.2f, 0.28f, 0.38f, 1f);
    public Color itemSelectedColor = new Color(0.85f, 0.5f, 0.12f, 1f);

    [Header("Text Style")]
    public Color titleColor = Color.white;
    public Color subtitleColor = new Color(0.65f, 0.72f, 0.82f, 1f);
    public int titleFontSize = 26;
    public int subtitleFontSize = 15;
    public int labelFontSize = 14;

    readonly List<PartListItem> items = new List<PartListItem>();
    float lastPanelFraction = -1f;

    void Awake()
    {
        if (builder == null) builder = GetComponent<ShipBuilder>();
        if (thumbnails == null) thumbnails = GetComponent<PartThumbnailRenderer>();
        if (thumbnails == null) thumbnails = gameObject.AddComponent<PartThumbnailRenderer>();

        // Nothing authored in the scene, so put the same panel together on the fly.
        if (canvas == null) BuildHierarchy();

        WarnAboutMissingReferences();
        WireButtons();
    }

    // A panel that was restyled by hand can lose a reference to a deleted object, and the symptom -
    // a list that simply never fills - gives no hint why. Say so instead.
    void WarnAboutMissingReferences()
    {
        if (content == null) Debug.LogWarning("[Ship Builder] Parts panel has no Content assigned, so no part entries can be spawned.", this);
        if (itemTemplate == null) Debug.LogWarning("[Ship Builder] Parts panel has no Item Template assigned, so the list will stay empty.", this);
        if (panelRect == null) Debug.LogWarning("[Ship Builder] Parts panel has no Panel Rect assigned; the camera cannot allow for the space it covers.", this);
    }

    void OnEnable()
    {
        if (builder == null) return;
        builder.OfferChanged += RebuildList;
        builder.AssemblyChanged += RefreshStats;
    }

    void OnDisable()
    {
        if (builder == null) return;
        builder.OfferChanged -= RebuildList;
        builder.AssemblyChanged -= RefreshStats;
    }

    void Start()
    {
        RebuildList();
        RefreshStats();
    }

    // How much of the screen the panel eats changes with resolution and with the canvas scaler, so
    // the builder is told about it rather than guessing - it needs the number to centre the ship in
    // the part of the view that is actually visible.
    void LateUpdate()
    {
        if (builder == null || canvas == null || panelRect == null || Screen.width <= 0) return;

        float fraction = Mathf.Clamp01(panelRect.rect.width * canvas.scaleFactor / Screen.width);
        if (Mathf.Abs(fraction - lastPanelFraction) < 0.002f) return;

        lastPanelFraction = fraction;
        builder.uiPanelFraction = fraction;
        builder.RequestFraming();
    }

    // Listeners are added in code rather than saved on the buttons, so an authored panel does not
    // need its events wired up by hand in the inspector.
    void WireButtons()
    {
        if (coreButton != null)
        {
            coreButton.onClick.RemoveAllListeners();
            coreButton.onClick.AddListener(() => builder.ShowCoreOffer());
        }

        if (removeButton != null)
        {
            removeButton.onClick.RemoveAllListeners();
            removeButton.onClick.AddListener(() =>
            {
                builder.RemoveSelectedPart();
                RebuildList();
            });
        }
    }

    // ---------------------------------------------------------------- population

    public void RebuildList()
    {
        if (content == null || builder == null) return;

        foreach (PartListItem item in items)
        {
            if (item != null) Destroy(item.gameObject);
        }
        items.Clear();

        if (titleText != null) titleText.text = builder.OfferTitle;
        if (subtitleText != null) subtitleText.text = builder.OfferSubtitle;

        if (removeButton != null)
        {
            bool canRemove = !builder.IsChoosingCore && builder.SelectedHardPoint != null && builder.SelectedHardPoint.IsOccupied;
            removeButton.gameObject.SetActive(canRemove);
        }

        List<ShipPartDefinition> offer = builder.CurrentOffer;
        if (emptyText != null) emptyText.gameObject.SetActive(offer.Count == 0);

        if (itemTemplate != null)
        {
            ShipPartDefinition current = CurrentlyPlacedDefinition();
            foreach (ShipPartDefinition definition in offer)
            {
                PartListItem item = CreateItem(definition);
                item.SetSelected(definition == current);
                items.Add(item);
            }
        }

        RefreshStats();
    }

    ShipPartDefinition CurrentlyPlacedDefinition()
    {
        if (builder.IsChoosingCore) return builder.Core != null ? builder.Core.definition : null;
        if (builder.SelectedHardPoint == null || builder.SelectedHardPoint.occupant == null) return null;
        return builder.SelectedHardPoint.occupant.definition;
    }

    PartListItem CreateItem(ShipPartDefinition definition)
    {
        // Cloned from an inactive template, so it is fully set up before it can receive any pointer
        // events.
        PartListItem item = Instantiate(itemTemplate, content);
        item.name = "Item " + definition.displayName;

        if (item.thumbnail != null)
        {
            item.thumbnail.texture = thumbnails != null ? thumbnails.GetThumbnail(definition.prefab) : null;
        }

        item.Bind(builder, definition, itemColor, itemHoverColor, itemSelectedColor);
        item.gameObject.SetActive(true);

        var button = item.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                item.OnClicked();
                RebuildList();
            });
        }

        return item;
    }

    void RefreshStats()
    {
        if (footerText == null || builder == null) return;

        float mass = builder.TotalMass;
        footerText.text = mass > 0f
            ? string.Format("Parts: {0}    Mass: {1:0} kg", builder.PartCount, mass)
            : string.Format("Parts: {0}", builder.PartCount);
    }

    // ---------------------------------------------------------------- construction

    // Builds the panel and fills in every reference above. Public because the editor menu item runs
    // exactly this at edit time, which is what makes the result inspectable rather than conjured on
    // play. Safe to run outside play mode: nothing here touches runtime-only API.
    public void BuildHierarchy()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var canvasObject = new GameObject("Ship Builder UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Panel pinned to the right edge, full height.
        panelRect = CreateRect("Parts Panel", canvasObject.transform);
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

        titleText = CreateText(header, "Title", "Core", titleFontSize, titleColor, FontStyle.Bold, font);
        AddLayoutElement(titleText.gameObject, titleFontSize + 10f, 0f);

        subtitleText = CreateText(header, "Subtitle", "Choose a hull to start from", subtitleFontSize, subtitleColor, FontStyle.Normal, font);
        AddLayoutElement(subtitleText.gameObject, subtitleFontSize + 10f, 0f);

        RectTransform buttons = CreateRect("Buttons", header);
        var buttonLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 6f;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = false;
        AddLayoutElement(buttons.gameObject, 30f, 0f);

        coreButton = CreateButton(buttons, "Core Button", "Core", font);
        removeButton = CreateButton(buttons, "Remove Button", "Remove", font);
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

        emptyText = CreateText(scroll, "Empty Notice", "No parts available for this mount yet.", subtitleFontSize, subtitleColor, FontStyle.Italic, font);
        RectTransform emptyRect = emptyText.rectTransform;
        emptyRect.anchorMin = new Vector2(0f, 0.5f);
        emptyRect.anchorMax = new Vector2(1f, 0.5f);
        emptyRect.sizeDelta = new Vector2(-20f, 60f);
        emptyRect.anchoredPosition = Vector2.zero;
        emptyText.alignment = TextAnchor.MiddleCenter;
        emptyText.gameObject.SetActive(false);
    }

    // The one entry every list item is cloned from. It lives in the scene, inactive, so it can be
    // restyled once instead of the look being buried in code.
    PartListItem BuildItemTemplate(RectTransform parent, Font font)
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

        Text label = CreateText(itemRect, "Label", "Part", labelFontSize, titleColor, FontStyle.Normal, font);
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

        var item = itemRect.gameObject.AddComponent<PartListItem>();
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
