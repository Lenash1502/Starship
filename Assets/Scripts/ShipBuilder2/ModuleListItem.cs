using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One module in the right hand list: a rendered thumbnail plus its name. Clicking picks the module
// up; the sockets that can take it then light up on the ship, and the entry stays highlighted for
// as long as it is in hand.
public class ModuleListItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image background;
    public RawImage thumbnail;
    public Text label;

    ShipBuilder2 builder;
    ShipPartDefinition definition;

    Color normalColor;
    Color hoverColor;
    Color heldColor;
    bool isHeld;
    bool isHovered;

    public ShipPartDefinition Definition => definition;

    public void Bind(ShipBuilder2 owner, ShipPartDefinition module, Color normal, Color hover, Color held)
    {
        builder = owner;
        definition = module;
        normalColor = normal;
        hoverColor = hover;
        heldColor = held;

        if (label != null) label.text = module.displayName;
        ApplyColor();
    }

    public void SetHeld(bool held)
    {
        isHeld = held;
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
        if (builder != null) builder.HoldModule(definition);
    }

    void ApplyColor()
    {
        if (background == null) return;

        if (isHeld) background.color = heldColor;
        else if (isHovered) background.color = hoverColor;
        else background.color = normalColor;
    }
}
