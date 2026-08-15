using UnityEngine;
using UnityEngine.InputSystem;

public class BannerCapture : MonoBehaviour
{
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            ScreenCapture.CaptureScreenshot("ItchBanner_960x300.png");
            Debug.Log("Banner captured to project root folder!");
        }
    }
}
