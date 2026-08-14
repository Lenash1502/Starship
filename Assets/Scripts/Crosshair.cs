using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(RectTransform))]
public class Crosshair : MonoBehaviour
{
    private RectTransform rectTransform;
    private Canvas canvas;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    void Update()
    {
        if (Mouse.current == null || canvas == null) return;

        // Grab the absolute pixel position of the mouse on the monitor
        Vector2 mousePos = Mouse.current.position.ReadValue();

        // Safely convert that pixel position into the scaled Canvas coordinates
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            mousePos,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            out Vector2 localPoint
        );

        // Snap the UI image to the mouse position
        rectTransform.anchoredPosition = localPoint;
    }
}