using UnityEngine;

public class PerformanceDisplay : MonoBehaviour
{
    float deltaTime = 0.0f;

    void Update()
    {
        // Az FPS számításához szükséges simított időeltolódás
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;
        float fps = 1.0f / deltaTime;

        GUIStyle style = new GUIStyle();
        Rect rect = new Rect(20, 20, w, h * 2 / 100);
        
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = h * 2 / 50; // Dinamikus betűméret a felbontáshoz
        style.normal.textColor = Color.green;

        string text = string.Format("Felbontás: {0}x{1} | FPS: {2:0.}", w, h, fps);
        GUI.Label(rect, text, style);
    }
}