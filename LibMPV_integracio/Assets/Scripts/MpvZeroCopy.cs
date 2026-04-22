using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class MpvZeroCopy : MonoBehaviour
{
    private const string MpvLibName = "libmpv.so.2";
    private const string GlLibName = "libGL.so.1";
    private static string logFilePath;

    // --- LIBMPV STRUKTÚRÁK ---
    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_render_param { public int type; public IntPtr data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_opengl_init_params {
        public IntPtr get_proc_address;
        public IntPtr get_proc_address_ctx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_opengl_fbo {
        public int fbo;
        public int w;
        public int h;
        public int internal_format;
    }

    // --- DLL IMPORT PÉLDÁNYOK ---
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx, string name, string data);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command_string(IntPtr ctx, string args);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, IntPtr param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_update(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr ctx, IntPtr param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr ctx);

    [DllImport(GlLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr glXGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(GlLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void glGenFramebuffers(int n, out int framebuffers);
    [DllImport(GlLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void glBindFramebuffer(int target, int framebuffer);
    [DllImport(GlLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void glFramebufferTexture2D(int target, int attachment, int textarget, int texture, int level);
    [DllImport(GlLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void glDeleteFramebuffers(int n, ref int framebuffers);
    [DllImport(GlLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int glCheckFramebufferStatus(int target);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr GetProcAddressDelegate(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

    [AOT.MonoPInvokeCallback(typeof(GetProcAddressDelegate))]
    private static IntPtr GetProcAddress(IntPtr ctx, string name) {
        return glXGetProcAddress(name);
    }

    [Header("Beállítások")]
    public RawImage videoScreen; 
    public string videoPath = "/godzie/assets/default/backgrounds/welcome.mp4";

    private const int VideoWidth = 3840;
    private const int VideoHeight = 2160;
    
    // Ezeknek a változóknak statikusnak kell lenniük, hogy a Unity natív render Callbackje lássa őket
    private static RenderTexture videoTexture;
    private static IntPtr mpvHandle = IntPtr.Zero;
    private static IntPtr renderContext = IntPtr.Zero;
    private static int glFboId = 0;
    private static IntPtr renderParamsPtr = IntPtr.Zero;
    private static GetProcAddressDelegate glDelegate;
    private static bool isOpenGLInitialized = false;
    private static bool isFirstFrameRendered = false;

    // Delegate a Unity IssuePluginEvent-hez
    private delegate void UnityRenderEventDelegate(int eventID);
    private static UnityRenderEventDelegate renderEventCallback;
    private static IntPtr renderEventCallbackPtr;

    private const int MPV_INIT_EVENT = 1;
    private const int MPV_RENDER_EVENT = 2;

    private static void Log(string message)
    {
        string formattedMsg = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.Log(formattedMsg);
        try { File.AppendAllText(logFilePath, formattedMsg + "\n"); } catch { } 
    }

    void Start()
    {
        logFilePath = Path.Combine(Application.dataPath, "../mpv_debug.txt");
        File.WriteAllText(logFilePath, "=== MPV ZERO COPY LOG (NATIVE PLUGIN EVENT) ===\n");
        
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        Log("1. Csatlakozás előkészítése...");
        
        videoTexture = new RenderTexture(VideoWidth, VideoHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        videoTexture.Create();
        if (videoScreen != null) videoScreen.texture = videoTexture;

        mpvHandle = mpv_create();
        if (mpvHandle == IntPtr.Zero) return;
        
        mpv_set_option_string(mpvHandle, "vo", "libmpv");
        mpv_set_option_string(mpvHandle, "hwdec", "vaapi");
        mpv_set_option_string(mpvHandle, "vf", "format=colorlevels=pc");
        mpv_initialize(mpvHandle);
        
        mpv_command_string(mpvHandle, "set loop-file yes");
        mpv_command_string(mpvHandle, "set pause no");
        
        // --- JAVÍTOTT LOGOLÁS A FÁJLBETÖLTÉSHEZ ---
        int loadRes = mpv_command_string(mpvHandle, $"loadfile {videoPath}");
        Log($"loadfile parancs lefutott, eredmény (0 a jó): {loadRes} | Útvonal: {videoPath}");
        // ------------------------------------------

        // Előkészítjük a natív Callback-et a Unity Render Szálhoz
        renderEventCallback = new UnityRenderEventDelegate(OnNativeRenderEvent);
        renderEventCallbackPtr = Marshal.GetFunctionPointerForDelegate(renderEventCallback);

        Log("2. Kérjük a Unity-t, hogy hívja meg az inicializálást a Render Szálon!");
        GL.IssuePluginEvent(renderEventCallbackPtr, MPV_INIT_EVENT);

        StartCoroutine(RenderRoutine());
    }

    // EZ A FÜGGVÉNY KÖZVETLENÜL A VIDEÓKÁRTYA SZÁLÁN FUT!
    [AOT.MonoPInvokeCallback(typeof(UnityRenderEventDelegate))]
    private static void OnNativeRenderEvent(int eventID)
    {
        if (eventID == MPV_INIT_EVENT && !isOpenGLInitialized)
        {
            try
            {
                int glTextureId = (int)videoTexture.GetNativeTexturePtr();
                
                glGenFramebuffers(1, out glFboId);
                if (glFboId == 0)
                {
                    Log("KRITIKUS HIBA: A glGenFramebuffers Még mindig 0! (Native szálon is)");
                    return;
                }

                glBindFramebuffer(0x8D40, glFboId);
                glFramebufferTexture2D(0x8D40, 0x8CE0, 0x0DE1, glTextureId, 0);
                
                int status = glCheckFramebufferStatus(0x8D40);
                Log($"FBO Generálva a Native Szálon! Státusz: {status}");
                glBindFramebuffer(0x8D40, 0);

                glDelegate = new GetProcAddressDelegate(GetProcAddress);
                mpv_opengl_init_params glInitParams = new mpv_opengl_init_params {
                    get_proc_address = Marshal.GetFunctionPointerForDelegate(glDelegate),
                    get_proc_address_ctx = IntPtr.Zero
                };

                IntPtr glInitPtr = Marshal.AllocHGlobal(Marshal.SizeOf<mpv_opengl_init_params>());
                Marshal.StructureToPtr(glInitParams, glInitPtr, false);

                int paramSize = Marshal.SizeOf<mpv_render_param>();
                IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("opengl");
                
                IntPtr createParamsPtr = Marshal.AllocHGlobal(paramSize * 3);
                Marshal.StructureToPtr(new mpv_render_param { type = 1, data = apiTypePtr }, createParamsPtr, false);
                Marshal.StructureToPtr(new mpv_render_param { type = 2, data = glInitPtr }, new IntPtr(createParamsPtr.ToInt64() + paramSize), false);
                Marshal.StructureToPtr(new mpv_render_param { type = 0, data = IntPtr.Zero }, new IntPtr(createParamsPtr.ToInt64() + (paramSize * 2)), false);

                int ctxRes = mpv_render_context_create(out renderContext, mpvHandle, createParamsPtr);
                Log($"Render Context Létrehozva (0 jó): {ctxRes}");

                Marshal.FreeHGlobal(createParamsPtr);
                Marshal.FreeHGlobal(apiTypePtr);
                Marshal.FreeHGlobal(glInitPtr);

                mpv_opengl_fbo fboParam = new mpv_opengl_fbo {
                    fbo = glFboId,
                    w = VideoWidth,
                    h = VideoHeight,
                    internal_format = 32856
                };
                IntPtr fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<mpv_opengl_fbo>());
                Marshal.StructureToPtr(fboParam, fboPtr, false);

                renderParamsPtr = Marshal.AllocHGlobal(paramSize * 2);
                Marshal.StructureToPtr(new mpv_render_param { type = 3, data = fboPtr }, renderParamsPtr, false);
                Marshal.StructureToPtr(new mpv_render_param { type = 0, data = IntPtr.Zero }, new IntPtr(renderParamsPtr.ToInt64() + paramSize), false);

                isOpenGLInitialized = true;
            }
            catch (Exception ex)
            {
                Log($"HIBA AZ INIT KÖZBEN (Native szál): {ex.Message}");
            }
        }
        else if (eventID == MPV_RENDER_EVENT && isOpenGLInitialized && renderContext != IntPtr.Zero)
        {
            if (mpv_render_context_update(renderContext) != 0)
            {
                mpv_render_context_render(renderContext, renderParamsPtr);
                
                if (!isFirstFrameRendered)
                {
                    Log("SIKER! Az első képkocka ráfestve a képernyőre (Native szálon)!");
                    isFirstFrameRendered = true;
                }
            }
        }
    }

    private IEnumerator RenderRoutine()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();

            if (isOpenGLInitialized)
            {
                // Szólunk a Unity-nek, hogy hívja meg a rajzoló függvényt a videókártyán
                GL.IssuePluginEvent(renderEventCallbackPtr, MPV_RENDER_EVENT);
                
                if (videoScreen != null) 
                {
                    videoScreen.SetMaterialDirty();
                }
            }
        }
    }

    void OnDestroy()
    {
        Log("Rendszer leállítása...");
        StopAllCoroutines();
        
        // Memória felszabadítás (Célszerű lenne ezeket is Native szálon csinálni, de kilépéskor már kevésbé kritikus)
        if (renderContext != IntPtr.Zero) mpv_render_context_free(renderContext);
        if (mpvHandle != IntPtr.Zero) mpv_terminate_destroy(mpvHandle);
        if (glFboId != 0) glDeleteFramebuffers(1, ref glFboId);
        if (renderParamsPtr != IntPtr.Zero) Marshal.FreeHGlobal(renderParamsPtr);
        
        if (videoTexture != null) videoTexture.Release();
    }
}