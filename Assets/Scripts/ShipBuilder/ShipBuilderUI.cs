using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Builds and drives the right hand parts panel.
//
// The whole hierarchy is created in code so the menu works in any scene that has a ShipBuilder in
// it - drop the component on, press play, and the list is there. Everything that affects how it
// looks is exposed below, and the layout can still be reskinned afterwards because the objects it
// creates are ordinary uGUI objects.
[RequireComponent(typeof(ShipBuilder))]
public class ShipBuilderUI : MonoBehaviour
{
    [Header("References")]
    public ShipBuilder builder;
    public PartThumbnailRenderer thumbnails;

    [Header("Panel")]
    public float panelWidth = 360f;
    public Color panelColor = new Color(0.05f, 0.06f, 0.09f, 0.92f);
    public Color headerColor = new Color(0.09f, 0.11f, 0.16f, 1f);

    [Header("List")]
    public int columns = 2;
    public Vector2 cellSize = new Vector2(158f, 186f);
    public Vector2 cellSpacing = new Vector2(8f, 8f);
    public Color itemColor = new Color(0.13f, 0.15f, 0.2f, 1f);
    public Color itemHoverColor = new Color(0.2f, 0.28f, 0.38f, 1f);
    public Color itemSelectedColor = new Color(0.85f, 0.5f, 0.12f, 1f);

    [Header("Text")]
    public Color titleColor = Color.white;
    public Color subtitleColor = new Color(0.65f, 0.72f, 0.82f, 1f);
    public int titleFontSize = 26;
    public int subtitleFontSize = 15;
    public int labelFontSize = 14;

    readonly List<PartListItem> items = new List<PartListItem>();

    Font font;
    Canvas canvas;
    RectTransform panelRect;
    float lastPanelFraction = -1f;
    Text titleText;
    Text subtitleText;
    Text footerText;
    Text emptyText;
    RectTransform content;
    GameObject removeButton;

    void Awake()
    {
        if (builder == null) builder = GetComponent<ShipBuilder>();
        if (thumbnails == null) thumbnails = GetComponent<PartThumbnailRenderer>();
        if (thumbnails == null) thumbnails = gameObject.AddComponent<PartThumbnailRenderer>();

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        BuildHierarchy();
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

    // ---------------------------------------------------------------- construction

    void BuildHierarchy()
    {
        var canvasObject = new GameObject("Ship Builder UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Panel pinned to the right edge, full height.
        RectTransform panel = CreateRect("Parts Panel", canvasObject.transform);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(panelWidth, 0f);
        panel.anchoredPosition = Vector2.zero;
        panelRect = panel;
        AddImage(panel.gameObject, panelColor);

        var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 10, 10, 10);
        panelLayout.spacing = 8f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        BuildHeader(panel);
        BuildScrollView(panel);
        BuildFooter(panel);
    }

    void BuildHeader(RectTransform panel)
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

        titleText = CreateText(header, "Title", "Core", titleFontSize, titleColor, FontStyle.Bold);
        subtitleText = CreateText(header, "Subtitle", "Choose a hull to start from", subtitleFontSize, subtitleColor, FontStyle.Normal);

        RectTransform buttons = CreateRect("Buttons", header);
        var buttonLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 6f;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = false;
        AddLayoutElement(buttons.gameObject, 30f, 0f);

        CreateButton(buttons, "Core Button", "Core", () => builder.ShowCoreOffer());
        removeButton = CreateButton(buttons, "Remove Button", "Remove", () =>
        {
            builder.RemoveSelectedPart();
            RebuildList();
        });
    }

    void BuildScrollView(RectTransform panel)
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

        emptyText = CreateText(scroll, "Empty Notice", "No parts available for this mount yet.", subtitleFontSize, subtitleColor, FontStyle.Italic);
        RectTransform emptyRect = emptyText.rectTransform;
        emptyRect.anchorMin = new Vector2(0f, 0.5f);
        emptyRect.anchorMax = new Vector2(1f, 0.5f);
        emptyRect.sizeDelta = new Vector2(-20f, 60f);
        emptyRect.anchoredPosition = Vector2.zero;
        emptyText.alignment = TextAnchor.MiddleCenter;
        emptyText.gameObject.SetActive(false);
    }

    void BuildFooter(RectTransform panel)
    {
        footerText = CreateText(panel, "Footer", string.Empty, subtitleFontSize, subtitleColor, FontStyle.Normal);
        AddLayoutElement(footerText.gameObject, 22f, 0f);
    }

    // ---------------------------------------------------------------- population

    public void RebuildList()
    {
        if (content == null) return;

        foreach (PartListItem item in items)
        {
            if (item != null) Destroy(item.gameObject);
        }
        items.Clear();

        titleText.text = builder.OfferTitle;
        subtitleText.text = builder.OfferSubtitle;
        removeButton.SetActive(!builder.IsChoosingCore && builder.SelectedHardPoint != null && builder.SelectedHardPoint.IsOccupied);

        List<ShipPartDefinition> offer = builder.CurrentOffer;
        emptyText.gameObject.SetActive(offer.Count == 0);

        ShipPartDefinition current = CurrentlyPlacedDefinition();

        foreach (ShipPartDefinition definition in offer)
        {
            PartListItem item = CreateItem(definition);
            item.SetSelected(definition == current);
            items.Add(item);
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
        RectTransform itemRect = CreateRect("Item " + definition.displayName, content);
        Image background = AddImage(itemRect.gameObject, itemColor);

        RectTransform thumbRect = CreateRect("Thumbnail", itemRect);
        thumbRect.anchorMin = new Vector2(0f, 1f);
        thumbRect.anchorMax = new Vector2(1f, 1f);
        thumbRect.pivot = new Vector2(0.5f, 1f);
        thumbRect.sizeDelta = new Vector2(-12f, cellSize.y - 40f);
        thumbRect.anchoredPosition = new Vector2(0f, -6f);

        var raw = thumbRect.gameObject.AddComponent<RawImage>();
        raw.texture = thumbnails != null ? thumbnails.GetThumbnail(definition.prefab) : null;
        raw.raycastTarget = false;

        Text label = CreateText(itemRect, "Label", definition.displayName, labelFontSize, titleColor, FontStyle.Normal);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.sizeDelta = new Vector2(-8f, 30f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.raycastTarget = false;

        var item = itemRect.gameObject.AddComponent<PartListItem>();
        item.background = background;
        item.thumbnail = raw;
        item.label = label;
        item.Bind(builder, definition, itemColor, itemHoverColor, itemSelectedColor);

        var button = itemRect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = background;
        button.onClick.AddListener(() =>
        {
            item.OnClicked();
            RebuildList();
        });

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

    Text CreateText(Transform parent, string name, string value, int size, Color color, FontStyle style)
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

        AddLayoutElement(rect.gameObject, size + 10f, 0f);
        return text;
    }

    GameObject CreateButton(Transform parent, string name, string caption, UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = CreateRect(name, parent);
        Image background = AddImage(rect.gameObject, itemColor);

        Text label = CreateText(rect, "Label", caption, labelFontSize, titleColor, FontStyle.Bold);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;
        labelRect.anchoredPosition = Vector2.zero;
        label.alignment = TextAnchor.MiddleCenter;
        Destroy(label.GetComponent<LayoutElement>());

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;
        button.onClick.AddListener(action);

        return rect.gameObject;
    }
}
