using UnityEngine;
using UnityEngine.InputSystem;

// Testing-folder variant of Scripts/VirtualJoystickMath.cs, copied instead of referenced so this
// folder stays fully self-contained. Turns the mouse's offset from the screen center into a
// deadzone-adjusted (x = yaw, y = pitch) intent in the range [-1, 1].
public static class TestVirtualJoystickMath
{
    public static Vector2 CalculateRawIntent(float deadzoneRadius)
    {
        if (Mouse.current == null) return Vector2.zero;

        Vector2 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 mousePos = Mouse.current.position.ReadValue();

        Vector2 offset = mousePos - screenCenter;
        Vector2 normalizedOffset = new(offset.x / screenCenter.x, offset.y / screenCenter.y);

        float distance = normalizedOffset.magnitude;
        if (distance <= deadzoneRadius) return Vector2.zero;

        float activeAmount = (distance - deadzoneRadius) / (1f - deadzoneRadius);
        Vector2 direction = normalizedOffset.normalized;

        return new Vector2(
            Mathf.Clamp(direction.x * activeAmount, -1f, 1f),
            Mathf.Clamp(direction.y * activeAmount, -1f, 1f)
        );
    }
}
