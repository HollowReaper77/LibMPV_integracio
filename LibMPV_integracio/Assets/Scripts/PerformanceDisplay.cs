using UnityEngine;

public class PerformanceDisplay : MonoBehaviour
{
    private float deltaTime = 0.0f;
    private string hardwareInfo;
    private string gpuAPI;

    void Start()
    {
        // Fix adatok lekérése egyszer az elején
        string cpu = SystemInfo.processorType;
        string gpu = SystemInfo.graphicsDeviceName;
        gpuAPI = SystemInfo.graphicsDeviceType.ToString(); // Pl. Direct3D11, Vulkan, Metal
        int ram = SystemInfo.systemMemorySize;

        hardwareInfo = $"{cpu}\n{gpu} ({gpuAPI})\nRAM: {ram} MB";
    }

    void Update()
    {
        // FPS simítás
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;
        float fps = 1.0f / deltaTime;
        string quality = QualitySettings.names[QualitySettings.GetQualityLevel()];

        GUIStyle style = new GUIStyle();

        // Bal felső sarokba pozicionálás, kis margóval
        Rect rect = new Rect(20, 20, w - 40, h - 40);
        
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = h * 2 / 60; // Kicsit finomított méret, hogy több sor kiférjen
        style.normal.textColor = Color.green;
        style.richText = true; // Engedélyezi a félkövér/színes formázást

        // Mindent egybefoglaló szöveg
        string text = string.Format(
            "<b>FPS:</b> {0:0.} ({1}x{2})\n" +
            "<b>Minőség:</b> {3}\n" +
            "<b>Hardver:</b> {4}",
            fps, w, h, quality, hardwareInfo
        );

        // Árnyék a jobb olvashatóságért (opcionális)
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
        
        // Valódi szöveg
        style.normal.textColor = Color.green;
        GUI.Label(rect, text, style);
    }
}