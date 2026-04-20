using UnityEngine;
using Unity.Profiling;
using System.Diagnostics;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class VideoDebugger : MonoBehaviour
{
    private ProfilerRecorder videoUpdateRecorder;
    
    [Header("Teljesítmény beállítások")]
    [Tooltip("Ide írd be a maximális FPS korlátot (pl. 30 vagy 60)")]
    public int targetFPS = 120;

    [Header("Megjelenítési beállítások")]
    public float updateInterval = 1.0f; 
    public int fontSize = 35;

    private float timer = 0f;
    private int frameCount = 0;
    private float currentFps = 0f;

    private string activeHardwareInfo = "";
    private string allGpuInfo = "";
    private string displayText = "";
    private string finalOutputString = "Adatgyűjtés folyamatban..."; 
    private GUIStyle customStyle;

    private static VideoDebugger instance;

    void Awake()
    {
        // DontDestroyOnLoad: Így a script és a mérés nem szakad meg jelenetváltáskor
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // FPS korlát beállítása a Unity motorban
        Application.targetFrameRate = targetFPS;

        activeHardwareInfo = $"--- AKTÍV RENDERELŐ KÖRNYEZET ---\n" +
                             $"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount} mag)\n" +
                             $"Rendszer RAM: {SystemInfo.systemMemorySize} MB\n" +
                             $"Használt GPU: {SystemInfo.graphicsDeviceName}\n" +
                             $"Grafikus API: {SystemInfo.graphicsDeviceType}\n" +
                             $"-----------------------------------\n\n";

        allGpuInfo = $"--- GÉPBEN TALÁLHATÓ ÖSSZES GPU (Linux lspci) ---\n" + 
                     GetAllGPUsLinux() + 
                     $"-------------------------------------------------\n\n";
    }

    void OnEnable()
    {
        videoUpdateRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Video, "VideoPlayer.Update");
    }

    void OnDisable()
    {
        if (videoUpdateRecorder.Valid)
            videoUpdateRecorder.Dispose();
    }

    void Update()
    {
        SwitchScene();
        timer += Time.unscaledDeltaTime;
        frameCount++;
        
        if (timer >= updateInterval)
        {
            currentFps = frameCount / timer;
            
            // Biztonsági ellenőrzés a Profilerre
            float timeMs = videoUpdateRecorder.Valid ? (videoUpdateRecorder.LastValue / 1000000f) : 0f;
            string status = timeMs > 1.5f ? "SZOFTVERES (CPU)" : "HARDVERES (GPU)";
            
            // AKTUÁLIS JELENET LEKÉRÉSE
            string currentSceneName = SceneManager.GetActiveScene().name;

            // --- KIJELZŐ ADATOK LEKÉRÉSE ---
            int width = Screen.width;
            int height = Screen.height;
            string aspectRatio = GetAspectRatio(width, height);
            
            // Monitor Hz és DPI
            int refreshRate = Screen.currentResolution.refreshRate; 
            float dpi = Screen.dpi;
            string dpiText = dpi == 0 ? "Ismeretlen" : dpi.ToString("F1");
            
            // Ablakmód és Monitorok száma
            string windowMode = Screen.fullScreen ? $"Teljes képernyő ({Screen.fullScreenMode})" : "Ablakos mód";
            int displayCount = Display.displays.Length;

            displayText = $"--- MEGJELENÍTÉS ÉS KÉPERNYŐ ---\n" +
                          $"AKTUÁLIS JELENET: {currentSceneName}\n" +
                          $"FELBONTÁS ÉS ARÁNY: {width}x{height} ({aspectRatio})\n" +
                          $"KÉPFRISSÍTÉS: {refreshRate} Hz\n" +
                          $"ABLAK MÓD: {windowMode}\n" +
                          $"MONITOR DPI: {dpiText}\n" +
                          $"CSATLAKOZTATOTT MONITOROK: {displayCount}\n\n" +
                          $"--- TELJESÍTMÉNY ÉS DEKÓDOLÁS ---\n" +
                          $"Cél FPS (Korlát): {targetFPS}\n" +
                          $"Jelenlegi FPS: {Mathf.RoundToInt(currentFps)}\n" +
                          $"Videó CPU feldolgozási idő: {timeMs:F2} ms\n" +
                          $"Valószínűsített mód: {status}";
            
            finalOutputString = activeHardwareInfo + allGpuInfo + displayText;

            timer = 0f; 
            frameCount = 0;
        }
    }

    void OnGUI()
    {
        if (customStyle == null)
        {
            customStyle = new GUIStyle(GUI.skin.label);
            customStyle.fontSize = fontSize;
            customStyle.normal.textColor = Color.yellow;
        }

        GUILayout.Label(finalOutputString, customStyle);
    }

    private void SwitchScene()
    {
        if (Keyboard.current == null) return;
        
        if (Keyboard.current.digit1Key.wasPressedThisFrame) SceneManager.LoadScene("Water");
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SceneManager.LoadScene("Welcome");
        if (Keyboard.current.digit3Key.wasPressedThisFrame) SceneManager.LoadScene("Original Re");
        if (Keyboard.current.digit4Key.wasPressedThisFrame) SceneManager.LoadScene("Sprite Teszt");
        if (Keyboard.current.digit5Key.wasPressedThisFrame) SceneManager.LoadScene("Szimpla Welcome");
        if (Keyboard.current.digit6Key.wasPressedThisFrame) SceneManager.LoadScene("Full Sprite");
    }

    private string GetAllGPUsLinux()
    {
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "/bin/bash";
            process.StartInfo.Arguments = "-c \"lspci | grep -iE 'vga|3d'\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (string.IsNullOrEmpty(output)) return "Nem található egyetlen GPU sem.\n";
            
            string cleanOutput = "";
            foreach(string line in output.Split('\n')) 
            {
                if(!string.IsNullOrEmpty(line)) 
                {
                    int index = line.IndexOf(": ");
                    cleanOutput += "- " + (index > 0 ? line.Substring(index + 2) : line) + "\n";
                }
            }
            return cleanOutput;
        }
        catch (System.Exception e)
        {
            return "Hiba a lekérdezéskor: " + e.Message + "\n";
        }
    }

    // --- KÉPARÁNY SZÁMÍTÓ SZEKCIÓ ---

    private string GetAspectRatio(int width, int height)
    {
        // 1. Megpróbáljuk a leggyakoribb szabványokhoz illeszteni (hibatűréssel)
        float ratio = (float)width / height;
        if (Mathf.Abs(ratio - 1.777f) < 0.05f) return "16:9";
        if (Mathf.Abs(ratio - 1.6f) < 0.05f) return "16:10";
        if (Mathf.Abs(ratio - 2.333f) < 0.05f) return "21:9";
        if (Mathf.Abs(ratio - 1.333f) < 0.05f) return "4:3";

        // 2. Ha nem szabványos, kiszámoljuk a pontos matematikai arányt
        int gcd = CalculateGCD(width, height);
        return $"{width / gcd}:{height / gcd}";
    }

    private int CalculateGCD(int a, int b)
    {
        while (a != 0 && b != 0)
        {
            if (a > b) a %= b;
            else b %= a;
        }
        return a | b;
    }
}