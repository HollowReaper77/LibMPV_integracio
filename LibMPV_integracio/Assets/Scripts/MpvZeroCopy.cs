using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class MpvZeroCopy : MonoBehaviour
{
    private const string MpvLibName = "libmpv.so.2";
    private const string GlLibName = "libGL.so.1";

    // --- LIBMPV STRUKTÚRÁK ÉS DllImport ---
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

    // --- NYERS OPENGL PARANCSOK A DEBIANBÓL ---
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr GetProcAddressDelegate(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

    // IL2CPP Biztos OpenGL Betöltő
    [AOT.MonoPInvokeCallback(typeof(GetProcAddressDelegate))]
    private static IntPtr GetProcAddress(IntPtr ctx, string name) {
        return glXGetProcAddress(name);
    }

    [Header("Beállítások")]
    public RawImage videoScreen; 
    public string videoPath = "/godzie/assets/default/backgrounds/welcome.mp4";

    private const int VideoWidth = 3840;
    private const int VideoHeight = 2160;
    
    private Texture2D videoTexture;
    private IntPtr mpvHandle, renderContext;
    private int glFboId;
    private IntPtr renderParamsPtr;
    private GetProcAddressDelegate glDelegate;

    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        // 1. Textúra létrehozása (Nincs többé byte[] RAM buffer!)
        videoTexture = new Texture2D(VideoWidth, VideoHeight, TextureFormat.RGBA32, false);
        videoTexture.Apply(false, true); // Feltöltjük a GPU-ra azonnal
        if (videoScreen != null) videoScreen.texture = videoTexture;

        // 2. Nyers OpenGL FBO létrehozása és összekötése a Unity textúrával
        int glTextureId = (int)videoTexture.GetNativeTexturePtr();
        glGenFramebuffers(1, out glFboId);
        glBindFramebuffer(0x8D40, glFboId); // GL_FRAMEBUFFER
        glFramebufferTexture2D(0x8D40, 0x8CE0, 0x0DE1, glTextureId, 0); // Fűzzük a textúrát az FBO-hoz
        glBindFramebuffer(0x8D40, 0); // Kötés feloldása

        // 3. MPV Inicializálás Hardveres Gyorsítással
        mpvHandle = mpv_create();
        mpv_set_option_string(mpvHandle, "vo", "libmpv");
        mpv_set_option_string(mpvHandle, "hwdec", "vaapi"); // Nem kell a "-copy", mert VRAM-ban maradunk!
        mpv_initialize(mpvHandle);

        // 4. OpenGL Környezet átadása a libmpv-nek
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

        mpv_render_context_create(out renderContext, mpvHandle, createParamsPtr);

        Marshal.FreeHGlobal(createParamsPtr);
        Marshal.FreeHGlobal(apiTypePtr);
        Marshal.FreeHGlobal(glInitPtr);

        // 5. Render FBO paraméterek előkészítése
        mpv_opengl_fbo fboParam = new mpv_opengl_fbo {
            fbo = glFboId,
            w = VideoWidth,
            h = VideoHeight,
            internal_format = 32856 // GL_RGBA8
        };
        IntPtr fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<mpv_opengl_fbo>());
        Marshal.StructureToPtr(fboParam, fboPtr, false);

        renderParamsPtr = Marshal.AllocHGlobal(paramSize * 2);
        Marshal.StructureToPtr(new mpv_render_param { type = 3, data = fboPtr }, renderParamsPtr, false); // MPV_RENDER_PARAM_OPENGL_FBO
        Marshal.StructureToPtr(new mpv_render_param { type = 0, data = IntPtr.Zero }, new IntPtr(renderParamsPtr.ToInt64() + paramSize), false);

        // 6. Indítás
        mpv_command_string(mpvHandle, "set loop-file yes");
        mpv_command_string(mpvHandle, $"loadfile {videoPath}");

        // Elindítjuk az aszinkron renderelést az Update helyett!
        StartCoroutine(RenderLoop());
    }

    // Nincs több Update() függvény! Ehelyett a GPU saját idővonalán (Render Thread) rajzolunk.
    IEnumerator RenderLoop()
    {
        while (true)
        {
            // Megvárjuk, amíg a Unity befejezi a logikát, és készen áll a képernyő rajzolására
            yield return new WaitForEndOfFrame();

            if (mpvHandle != IntPtr.Zero && renderContext != IntPtr.Zero)
            {
                if (mpv_render_context_update(renderContext) != 0)
                {
                    // A GPU közvetlenül a videókártya textúrájára rajzol, nulla memóriahatással!
                    mpv_render_context_render(renderContext, renderParamsPtr);
                }
            }
        }
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (renderContext != IntPtr.Zero) mpv_render_context_free(renderContext);
        if (mpvHandle != IntPtr.Zero) mpv_terminate_destroy(mpvHandle);
        
        // Töröljük a nyers OpenGL FBO-t
        if (glFboId != 0) glDeleteFramebuffers(1, ref glFboId);
        if (renderParamsPtr != IntPtr.Zero) Marshal.FreeHGlobal(renderParamsPtr);
    }
}