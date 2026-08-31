using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One heading in the strip above the part list. Clicking it swaps the grid over to that category.
//
// A tab is dimmed rather than hidden when there is nowhere to put its parts - before a core is
// chosen, or once every socket of that category is filled - so the shape of the whole library stays
// visible and the player can see what a different hull would open up.
public class CategoryTab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image background;
    public Text label;

    Action<string> onSelected;

    Color normalColor;
    Color hoverColor;
    Color activeColor;
    Color disabledColor;

    bool isActive;
    bool isHovered;
    bool isReachable = true;

    public string Category { get; private set; }

    public void Bind(string category, string caption, Action<string> selected,
                     Color normal, Color hover, Color active, Color disabled)
    {
        Category = category;
        onSelected = selected;
        normalColor = normal;
        hoverColor = hover;
        activeColor = active;
        disabledColor = disabled;

        if (label != null) label.text = caption;
        ApplyColor();
    }

    public void SetSelected(bool active)
    {
        isActive = active;
        ApplyColor();
    }

    // Whether anything on this tab could be placed right now. The tab stays clickable either way:
    // browsing a category you cannot use yet is harmless and tells you what the hull is missing.
    public void SetReachable(bool reachable)
    {
        isReachable = reachable;
        ApplyColor();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        ApplyColor();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyColor();
    }

    public void OnClicked()
    {
        onSelected?.Invoke(Category);
    }

    void ApplyColor()
    {
        if (background != null)
        {
            if (isActive) background.color = activeColor;
            else if (isHovered) background.color = hoverColor;
            else if (!isReachable) background.color = disabledColor;
            else background.color = normalColor;
        }

        if (label != null)
        {
            Color text = label.color;
            text.a = isActive || isReachable ? 1f : 0.45f;
            label.color = text;
        }
    }
}
