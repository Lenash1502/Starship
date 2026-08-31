using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The right hand part list for the second builder.
//
// The library is far too big for one flat grid, so it is split the way the prefab folders are: a
// strip of category tabs above the cells, one grid below. Core comes first and is the only tab that
// does anything on an empty stand - everything else hangs off a hull, so the rest stay dimmed until
// one is chosen.
//
// Under the list sits the random ship block: a button and a depth slider, where depth is how many
// rounds of filling to run. 1 fills the core's own sockets, 2 also fills the sockets those parts
// brought with them, and so on.
//
// The panel can be built into the scene by the setup tool and then restyled in the inspector; if
// the references below are empty it puts the same hierarchy together at runtime instead, which also
// means a scene authored before the tabs existed grows them on its own.
[RequireComponent(typeof(ShipBuilder2))]
public class ShipBuilder2UI : MonoBehaviour
{
    [Header("References")]
    public ShipBuilder2 builder;
    public PartThumbnailRenderer thumbnails;

    [Header("Scene Objects (leave empty to build the panel at runtime)")]
    public Canvas canvas;
    public RectTransform panelRect;
    public RectTransform tabBar;
    public RectTransform content;
    [Tooltip("Inactive tab cloned once per category. Restyle this one and the whole strip follows.")]
    public CategoryTab tabTemplate;
    [Tooltip("Inactive entry cloned once per part. Restyle this one and the whole list follows.")]
    public ModuleListItem itemTemplate;
    public RectTransform partToolsRoot;
    public Button turnButton;
    public Button tiltButton;
    public Button rollButton;
    public Button mirrorButton;
    public RectTransform randomRoot;
    public Button generateButton;
    public Slider depthSlider;
    public Text depthLabel;
    public Text titleText;
    public Text subtitleText;
    public Text footerText;
    public Button removeButton;

    [Header("Panel Style")]
    public float panelWidth = 340f;
    public Color panelColor = new Color(0.05f, 0.06f, 0.09f, 0.92f);
    public Color headerColor = new Color(0.09f, 0.11f, 0.16f, 1f);

    [Header("Tab Style")]
    public int tabColumns = 2;
    public Vector2 tabSize = new Vector2(146f, 28f);
    public Vector2 tabSpacing = new Vector2(4f, 4f);
    public Color tabColor = new Color(0.13f, 0.15f, 0.2f, 1f);
    public Color tabHoverColor = new Color(0.2f, 0.28f, 0.38f, 1f);
    public Color tabActiveColor = new Color(0.2f, 0.5f, 0.75f, 1f);
    [Tooltip("A category with nowhere to put its parts yet.")]
    public Color tabDisabledColor = new Color(0.09f, 0.1f, 0.13f, 1f);

    [Header("List Style")]
    public int columns = 2;
    public Vector2 cellSize = new Vector2(148f, 176f);
    public Vector2 cellSpacing = new Vector2(8f, 8f);
    public Color itemColor = new Color(0.13f, 0.15f, 0.2f, 1f);
    public Color itemHoverColor = new Color(0.2f, 0.28f, 0.38f, 1f);
    public Color itemHeldColor = new Color(0.85f, 0.5f, 0.12f, 1f);

    [Header("Text Style")]
    public Color titleColor = Color.white;
    public Color subtitleColor = new Color(0.65f, 0.72f, 0.82f, 1f);
    [Tooltip("Subtitle colour while the socket under the pointer is refusing the part.")]
    public Color blockedColor = new Color(1f, 0.45f, 0.35f, 1f);
    public int titleFontSize = 26;
    public int subtitleFontSize = 15;
    public int labelFontSize = 14;
    public int tabFontSize = 13;

    readonly List<ModuleListItem> items = new List<ModuleListItem>();
    readonly List<CategoryTab> tabs = new List<CategoryTab>();

    string activeCategory;
    bool hasLeftCoreTab;

    // Set when a clicked hologram names a category the library cannot fill, cleared as soon as the
    // player does anything else.
    string missingCategoryNotice;

    float lastPanelFraction = -1f;

    void Awake()
    {
        if (builder == null) builder = GetComponent<ShipBuilder2>();
        if (thumbnails == null) thumbnails = GetComponent<PartThumbnailRenderer>();
        if (thumbnails == null) thumbnails = gameObject.AddComponent<PartThumbnailRenderer>();

        if (canvas == null) BuildHierarchy();
        else AddMissingSections();

        if (removeButton != null)
        {
            removeButton.onClick.RemoveAllListeners();
            removeButton.onClick.AddListener(() => builder.RemoveSelectedPart());
        }

        BindFlipButton(turnButton, PartFlip.Turn);
        BindFlipButton(tiltButton, PartFlip.Tilt);
        BindFlipButton(rollButton, PartFlip.Roll);

        if (mirrorButton != null)
        {
            mirrorButton.onClick.RemoveAllListeners();
            mirrorButton.onClick.AddListener(() => builder.MirrorSelectedPart());
        }

        if (generateButton != null)
        {
            generateButton.onClick.RemoveAllListeners();
            generateButton.onClick.AddListener(GenerateRandomShip);
        }

        if (depthSlider != null)
        {
            depthSlider.onValueChanged.RemoveAllListeners();
            depthSlider.value = builder != null ? builder.randomDepth : 2;
            depthSlider.onValueChanged.AddListener(OnDepthChanged);
        }
    }

    void BindFlipButton(Button button, PartFlip half)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => builder.FlipSelectedPart(half));
    }

    void OnEnable()
    {
        if (builder == null) return;
        builder.HeldPartChanged += RefreshSelection;
        builder.AssemblyChanged += OnAssemblyChanged;
        builder.PreviewChanged += RefreshStatus;
        builder.CategoryRequested += OnCategoryRequested;
    }

    void OnDisable()
    {
        if (builder == null) return;
        builder.HeldPartChanged -= RefreshSelection;
        builder.AssemblyChanged -= OnAssemblyChanged;
        builder.PreviewChanged -= RefreshStatus;
        builder.CategoryRequested -= OnCategoryRequested;
    }

    void Start()
    {
        BuildTabs();
        ShowCategory(activeCategory);
        RefreshDepthLabel();
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

    // ---------------------------------------------------------------- tabs

    // One tab per category the catalog actually has parts for, in the order PartNaming lays down.
    public void BuildTabs()
    {
        if (tabBar == null || tabTemplate == null || builder == null) return;

        foreach (CategoryTab tab in tabs)
        {
            if (tab != null) Destroy(tab.gameObject);
        }
        tabs.Clear();

        foreach (string category in builder.Categories)
        {
            CategoryTab tab = Instantiate(tabTemplate, tabBar);
            tab.name = "Tab " + category;

            string caption = PartNaming.PrettyCategory(category) + "  " + builder.FamiliesInCategory(category).Count;
            tab.Bind(category, caption, ShowCategory, tabColor, tabHoverColor, tabActiveColor, tabDisabledColor);
            tab.gameObject.SetActive(true);

            var button = tab.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(tab.OnClicked);
            }

            tabs.Add(tab);
        }

        // The strip is a grid rather than a row - a dozen categories never fit across the panel -
        // so the bar has to be told how tall the rows it ended up with make it.
        int rows = Mathf.CeilToInt(tabs.Count / (float)Mathf.Max(1, tabColumns));
        float height = rows * tabSize.y + Mathf.Max(0, rows - 1) * tabSpacing.y + 4f;
        AddLayoutElement(tabBar.gameObject, height, 0f);

        if (string.IsNullOrEmpty(activeCategory) || !builder.Categories.Contains(activeCategory))
        {
            activeCategory = builder.Categories.Count > 0 ? builder.Categories[0] : null;
        }
    }

    public void ShowCategory(string category)
    {
        activeCategory = category;
        missingCategoryNotice = null;

        if (category != ShipBuilder2.CoreCategory) hasLeftCoreTab = true;

        BuildList();
        RefreshTabs();
    }

    // The player clicked the hologram in an empty socket, which is a request to see what fits there.
    //
    // Most of the mounts on these hulls are for categories the library has no prefabs for yet, so
    // there is often no tab to open. Saying so is the whole point of honouring the click: the socket
    // is asking a question, and "nothing has been built for this yet" is a real answer.
    void OnCategoryRequested(string category)
    {
        if (builder != null && builder.Categories.Contains(category))
        {
            ShowCategory(category);
            return;
        }

        missingCategoryNotice = "No " + PartNaming.PrettyCategory(category) + " parts in the library yet";
        RefreshStatus();
    }

    void RefreshTabs()
    {
        foreach (CategoryTab tab in tabs)
        {
            if (tab == null) continue;

            tab.SetSelected(tab.Category == activeCategory);
            tab.SetReachable(builder != null && builder.CategoryIsReachable(tab.Category));
        }
    }

    // ---------------------------------------------------------------- population

    // Fills the grid with whatever the active tab holds. Rebuilt on every tab change rather than
    // kept around: only one category is ever on screen, and the whole library at once would be a
    // hundred and fifty live cells all asking for a rendered thumbnail.
    public void BuildList()
    {
        if (content == null || itemTemplate == null || builder == null) return;

        foreach (ModuleListItem item in items)
        {
            if (item != null) Destroy(item.gameObject);
        }
        items.Clear();

        foreach (ShipPartFamily part in builder.FamiliesInCategory(activeCategory))
        {
            ModuleListItem item = Instantiate(itemTemplate, content);
            item.name = "Part " + part.displayName;

            if (item.thumbnail != null)
            {
                item.thumbnail.texture = thumbnails != null ? thumbnails.GetThumbnail(part.IconPrefab) : null;
            }

            item.Bind(builder, part, itemColor, itemHoverColor, itemHeldColor);
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

        RefreshSelection();
    }

    void OnAssemblyChanged()
    {
        // The first hull to land opens up the rest of the library, so move off the Core tab rather
        // than leaving the player looking at thirty hulls they have already chosen from. Only ever
        // once: coming back to browse cores after that is a deliberate choice.
        if (!hasLeftCoreTab && builder != null && builder.Core != null && activeCategory == ShipBuilder2.CoreCategory)
        {
            foreach (string category in builder.Categories)
            {
                if (category == ShipBuilder2.CoreCategory) continue;
                if (!builder.CategoryIsReachable(category)) continue;

                ShowCategory(category);
                RefreshStatus();
                return;
            }
        }

        RefreshTabs();
        RefreshSelection();
    }

    void RefreshSelection()
    {
        if (builder == null) return;

        // Any move the player makes answers the question the notice was raising.
        missingCategoryNotice = null;

        // With nothing in hand the Core tab still shows which hull is on the stand, so the grid is
        // never left without an answer to "which one is this ship using".
        ShipPartFamily highlighted = builder.HeldPart != null ? builder.HeldPart : builder.CoreFamily;

        foreach (ModuleListItem item in items)
        {
            if (item != null) item.SetHeld(item.Family == highlighted);
        }

        RefreshStatus();
    }

    // The subtitle carries the whole instruction, because this screen has a mode and the player
    // needs to know which one they are in.
    void RefreshStatus()
    {
        if (builder == null) return;

        bool hasCore = builder.Core != null;

        if (titleText != null) titleText.text = hasCore ? "Parts" : "Choose a Core";

        if (subtitleText != null)
        {
            // A refused placement does nothing when clicked, so the reason has to be readable
            // somewhere other than the colour of the hologram.
            if (!hasCore)
            {
                subtitleText.text = "Every part hangs off the hull - pick one to start, or roll a whole ship below";
                subtitleText.color = subtitleColor;
            }
            else if (missingCategoryNotice != null)
            {
                subtitleText.text = missingCategoryNotice;
                subtitleText.color = blockedColor;
            }
            else if (builder.HeldPart != null && builder.PreviewBlocked)
            {
                subtitleText.text = "Blocked - " + builder.HeldPart.displayName +
                                    " would sit inside what is already there";
                subtitleText.color = blockedColor;
            }
            else if (builder.HeldPart != null)
            {
                subtitleText.text = "Placing " + builder.HeldPart.displayName +
                                    " - hover a highlighted socket, right click to put it back";
                subtitleText.color = subtitleColor;
            }
            else if (builder.SelectedPart != null)
            {
                PlacedPart selected = builder.SelectedPart;

                // A socket the builder turned round on its own is worth saying out loud: the part
                // is not sitting the way its mount was authored, and that is deliberate.
                string orientation = selected.autoOriented
                    ? " (straightened to face with the hull)"
                    : selected.flip == PartFlip.None
                        ? string.Empty
                        : " (" + PartFlips.Describe(selected.flip) + ")";

                subtitleText.text = "Selected " + selected.definition.displayName + orientation +
                                    " - F turn, T tilt, R roll" +
                                    (builder.CanMirror(selected) ? ", M mirror" : string.Empty);
                subtitleText.color = subtitleColor;
            }
            else
            {
                subtitleText.text = "Pick a part, then hover a socket on the ship";
                subtitleText.color = subtitleColor;
            }
        }

        // The reorienting tools and Remove all act on the selected part, so they come and go
        // together with the selection.
        bool hasSelection = builder.HeldPart == null && builder.SelectedPart != null;

        if (removeButton != null) removeButton.gameObject.SetActive(hasSelection);

        if (partToolsRoot != null)
        {
            partToolsRoot.gameObject.SetActive(hasSelection);

            // Greyed rather than hidden for a model with no other half: the row would otherwise
            // change width from one selection to the next.
            if (mirrorButton != null) mirrorButton.interactable = hasSelection && builder.CanMirror(builder.SelectedPart);
        }

        if (footerText != null)
        {
            if (!hasCore)
            {
                footerText.text = "No hull on the stand";
            }
            else
            {
                float mass = builder.TotalMass;
                footerText.text = mass > 0f
                    ? string.Format("Parts: {0}    Open sockets: {1}    Mass: {2:0} kg",
                                    builder.PartCount, builder.FreeSocketCount, mass)
                    : string.Format("Parts: {0}    Open sockets: {1}", builder.PartCount, builder.FreeSocketCount);
            }
        }
    }

    // ---------------------------------------------------------------- random ships

    void GenerateRandomShip()
    {
        // The ship it leaves behind raises AssemblyChanged, which is what brings the tabs and the
        // footer back in line - the grid itself only depends on which tab is open, so it stands.
        if (builder != null) builder.GenerateRandomShip();
    }

    void OnDepthChanged(float value)
    {
        if (builder != null) builder.randomDepth = Mathf.RoundToInt(value);
        RefreshDepthLabel();
    }

    void RefreshDepthLabel()
    {
        if (depthLabel == null) return;

        int depth = builder != null ? builder.randomDepth : 1;
        depthLabel.text = "Depth " + depth;
    }

    // ---------------------------------------------------------------- construction

    // Builds the panel and fills in every reference above. Public because the setup tool runs it at
    // edit time, which is what leaves a real hierarchy behind to select and restyle.
    public void BuildHierarchy()
    {
        Font font = GetFont();

        var canvasObject = new GameObject("Ship Builder 2 UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

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
        BuildTabBar(panelRect);
        BuildScrollView(panelRect, font);
        BuildRandomSection(panelRect, font);

        footerText = CreateText(panelRect, "Footer", string.Empty, subtitleFontSize, subtitleColor, FontStyle.Normal, font);
        AddLayoutElement(footerText.gameObject, 22f, 0f);
    }

    // Grows the pieces this version of the panel needs onto a hierarchy that was authored before
    // they existed, keeping whatever styling the scene already carries. Each piece is slotted into
    // the layout by sibling index, which is what decides the order in a vertical layout group.
    void AddMissingSections()
    {
        if (panelRect == null) return;

        Font font = GetFont();

        if (tabBar == null)
        {
            BuildTabBar(panelRect);

            Transform scrollRoot = FindScrollRoot();
            if (scrollRoot != null) tabBar.SetSiblingIndex(scrollRoot.GetSiblingIndex());
        }

        if (randomRoot == null)
        {
            BuildRandomSection(panelRect, font);

            if (footerText != null) randomRoot.SetSiblingIndex(footerText.transform.GetSiblingIndex());
        }

        // Slotted in above Remove, since both act on the selected part and appear together.
        if (partToolsRoot == null && removeButton != null)
        {
            BuildPartTools((RectTransform)removeButton.transform.parent, font);
            partToolsRoot.SetSiblingIndex(removeButton.transform.GetSiblingIndex());
        }
    }

    // Content sits under Viewport, which sits under the Scroll View that the panel lays out.
    Transform FindScrollRoot()
    {
        if (content == null || content.parent == null) return null;
        return content.parent.parent;
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

        titleText = CreateText(header, "Title", "Choose a Core", titleFontSize, titleColor, FontStyle.Bold, font);
        AddLayoutElement(titleText.gameObject, titleFontSize + 10f, 0f);

        subtitleText = CreateText(header, "Subtitle", "Pick a part, then hover a socket on the ship",
                                  subtitleFontSize, subtitleColor, FontStyle.Normal, font);
        AddLayoutElement(subtitleText.gameObject, subtitleFontSize * 2f + 12f, 0f);

        BuildPartTools(header, font);

        removeButton = CreateButton(header, "Remove Button", "Remove Selected", font);
        AddLayoutElement(removeButton.gameObject, 30f, 0f);
        removeButton.gameObject.SetActive(false);
    }

    // The row of reorienting tools for whichever part is selected.
    //
    // Turn, Tilt and Roll are half turns about the part's three axes, which between them reach every
    // orientation a half turn can. Mirror is the different fix: it swaps the part for the opposite
    // half of its own model, for when the side was read wrong rather than the facing.
    void BuildPartTools(RectTransform parent, Font font)
    {
        partToolsRoot = CreateRect("Part Tools", parent);
        AddLayoutElement(partToolsRoot.gameObject, 28f, 0f);

        var layout = partToolsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        turnButton = CreateButton(partToolsRoot, "Turn Button", "Turn", font);
        tiltButton = CreateButton(partToolsRoot, "Tilt Button", "Tilt", font);
        rollButton = CreateButton(partToolsRoot, "Roll Button", "Roll", font);
        mirrorButton = CreateButton(partToolsRoot, "Mirror Button", "Mirror", font);

        partToolsRoot.gameObject.SetActive(false);
    }

    void BuildTabBar(RectTransform panel)
    {
        tabBar = CreateRect("Category Tabs", panel);
        AddLayoutElement(tabBar.gameObject, tabSize.y + 4f, 0f);

        var grid = tabBar.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = tabSize;
        grid.spacing = tabSpacing;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, tabColumns);
        grid.padding = new RectOffset(0, 0, 0, 0);

        tabTemplate = BuildTabTemplate(tabBar);
    }

    CategoryTab BuildTabTemplate(RectTransform parent)
    {
        RectTransform tabRect = CreateRect("Tab Template", parent);
        Image background = AddImage(tabRect.gameObject, tabColor);

        Text label = CreateText(tabRect, "Label", "Category", tabFontSize, titleColor, FontStyle.Normal, GetFont());
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;
        labelRect.anchoredPosition = Vector2.zero;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;

        var button = tabRect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = background;

        var tab = tabRect.gameObject.AddComponent<CategoryTab>();
        tab.background = background;
        tab.label = label;

        tabRect.gameObject.SetActive(false);
        return tab;
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

    // The random ship controls, parked in the bottom corner of the panel under the list.
    void BuildRandomSection(RectTransform panel, Font font)
    {
        randomRoot = CreateRect("Random Ship", panel);
        AddImage(randomRoot.gameObject, headerColor);
        AddLayoutElement(randomRoot.gameObject, 96f, 0f);

        var layout = randomRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        generateButton = CreateButton(randomRoot, "Generate Button", "Generate Random Ship", font);
        AddLayoutElement(generateButton.gameObject, 32f, 0f);

        RectTransform depthRow = CreateRect("Depth Row", randomRoot);
        AddLayoutElement(depthRow.gameObject, 26f, 0f);

        var rowLayout = depthRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;

        depthLabel = CreateText(depthRow, "Depth Label", "Depth 2", labelFontSize, subtitleColor, FontStyle.Normal, font);
        AddLayoutWidth(depthLabel.gameObject, 66f, 0f);

        depthSlider = BuildSlider(depthRow);
        AddLayoutWidth(depthSlider.gameObject, 0f, 1f);
    }

    Slider BuildSlider(RectTransform parent)
    {
        RectTransform sliderRect = CreateRect("Depth Slider", parent);

        RectTransform background = CreateRect("Background", sliderRect);
        background.anchorMin = new Vector2(0f, 0.35f);
        background.anchorMax = new Vector2(1f, 0.65f);
        background.sizeDelta = Vector2.zero;
        AddImage(background.gameObject, itemColor);

        RectTransform fillArea = CreateRect("Fill Area", sliderRect);
        fillArea.anchorMin = new Vector2(0f, 0.35f);
        fillArea.anchorMax = new Vector2(1f, 0.65f);
        fillArea.sizeDelta = new Vector2(-14f, 0f);

        RectTransform fill = CreateRect("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.sizeDelta = new Vector2(14f, 0f);
        Image fillImage = AddImage(fill.gameObject, tabActiveColor);

        RectTransform handleArea = CreateRect("Handle Slide Area", sliderRect);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.sizeDelta = new Vector2(-14f, 0f);

        RectTransform handle = CreateRect("Handle", handleArea);
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = new Vector2(0f, 1f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(14f, 0f);
        Image handleImage = AddImage(handle.gameObject, titleColor);

        var slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.wholeNumbers = true;
        slider.minValue = 1f;
        slider.maxValue = 6f;
        slider.value = 2f;

        // Unused beyond keeping the reference alive for anyone restyling in the inspector.
        fillImage.raycastTarget = false;

        return slider;
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

        var item = itemRect.gameObject.AddComponent<ModuleListItem>();
        item.background = background;
        item.thumbnail = raw;
        item.label = label;

        itemRect.gameObject.SetActive(false);
        return item;
    }

    // ---------------------------------------------------------------- uGUI helpers

    static Font GetFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

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

    static LayoutElement AddLayoutWidth(GameObject target, float minWidth, float flexibleWidth)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.AddComponent<LayoutElement>();

        element.minWidth = minWidth;
        element.preferredWidth = minWidth;
        element.flexibleWidth = flexibleWidth;
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
