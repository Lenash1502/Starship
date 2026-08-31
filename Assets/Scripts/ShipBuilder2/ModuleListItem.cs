using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One model in the part list: a rendered thumbnail plus its name. Clicking picks the model up; the
// sockets that can take it then light up on the ship, and the entry stays highlighted for as long
// as it is in hand.
//
// One cell per model, not per prefab. A mirrored wing is a single entry here and the side is settled
// by whichever socket it is dropped onto, so the list never asks the player to pick a hand.
public class ModuleListItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image background;
    public RawImage thumbnail;
    public Text label;

    ShipBuilder2 builder;
    ShipPartFamily family;

    Color normalColor;
    Color hoverColor;
    Color heldColor;
    bool isHeld;
    bool isHovered;

    public ShipPartFamily Family => family;

    public void Bind(ShipBuilder2 owner, ShipPartFamily model, Color normal, Color hover, Color held)
    {
        builder = owner;
        family = model;
        normalColor = normal;
        hoverColor = hover;
        heldColor = held;

        if (label != null) label.text = model.displayName;
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
        if (builder != null) builder.HoldPart(family);
    }

    void ApplyColor()
    {
        if (background == null) return;

        if (isHeld) background.color = heldColor;
        else if (isHovered) background.color = hoverColor;
        else background.color = normalColor;
    }
}
