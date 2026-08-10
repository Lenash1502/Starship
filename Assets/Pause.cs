using UnityEngine;
using UnityEngine.InputSystem;

public class Pause : MonoBehaviour
{
    void Update()
    {
        // Check if the 'T' key was pressed this frame using the New Input System
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            // Pauses the Unity Editor play mode
            Debug.Break();
            Debug.Log("Editor paused via 'T' key. You can now inspect values in the Inspector.");
        }
    }
}