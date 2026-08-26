using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One entry in the right hand part list: a rendered thumbnail of the model plus its name.
// Hovering ghosts the model into the socket, clicking bolts it on.
public class PartListItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image background;
    public RawImage thumbnail;
    public Text label;

    ShipBuilder builder;
    ShipPartDefinition definition;

    Color normalColor;
    Color hoverColor;
    Color selectedColor;
    bool isSelected;
    bool isHovered;

    public ShipPartDefinition Definition => definition;

    public void Bind(ShipBuilder owner, ShipPartDefinition part, Color normal, Color hover, Color selected)
    {
        builder = owner;
        definition = part;
        normalColor = normal;
        hoverColor = hover;
        selectedColor = selected;

        if (label != null) label.text = part.displayName;
        ApplyColor();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        ApplyColor();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        ApplyColor();
        if (builder != null) builder.HoverPart(definition);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyColor();
        if (builder != null) builder.ClearHover();
    }

    public void OnClicked()
    {
        if (builder != null) builder.ChoosePart(definition);
    }

    void ApplyColor()
    {
        if (background == null) return;

        if (isSelected) background.color = selectedColor;
        else if (isHovered) background.color = hoverColor;
        else background.color = normalColor;
    }
}
