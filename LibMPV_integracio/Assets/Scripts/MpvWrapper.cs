using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class MpvWrapper : MonoBehaviour
{
    private const string MpvLibName = "libmpv.so.2";

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_render_param { public int type; public IntPtr data; }

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string args);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, mpv_render_param[] param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_update(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr ctx, mpv_render_param[] param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr ctx);

    [Header("Beállítások")]
    public RawImage videoScreen; 
    public string videoPath = "/godzie/assets/default/backgrounds/welcome.mp4";

    private const int VideoWidth = 3840;
    private const int VideoHeight = 2160;
    private Texture2D videoTexture;
    private IntPtr mpvHandle;
    private IntPtr renderContext;
    private byte[] frameBuffer;

    // OPTIMALIZÁCIÓ: Előre lefoglalt mutatók a 60 FPS-hez
    private IntPtr sizePtr;
    private IntPtr formatPtr;
    private IntPtr stridePtr;

    void Start()
    {
        // JAVÍTÁS 1: Unity sebesség kényszerítése
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        mpvHandle = mpv_create();
        if (mpvHandle == IntPtr.Zero) return;

        mpv_set_option_string(mpvHandle, "vo", "libmpv");

        // JAVÍTÁS 2: Fekete képernyő megszüntetése (Vulkan + Memória visszamásolás)
        mpv_set_option_string(mpvHandle, "hwdec", "vaapi-copy"); 
        
        mpv_initialize(mpvHandle);
        
        videoTexture = new Texture2D(VideoWidth, VideoHeight, TextureFormat.RGB24, false);
        frameBuffer = new byte[VideoWidth * VideoHeight * 3];
        
        if (videoScreen != null) videoScreen.texture = videoTexture;

        // JAVÍTÁS 3: Memória lefoglalása csak EGYSZER, a Start-ban
        sizePtr = Marshal.AllocHGlobal(8); 
        Marshal.Copy(new int[] { VideoWidth, VideoHeight }, 0, sizePtr, 2);
        formatPtr = Marshal.StringToHGlobalAnsi("rgb24");
        stridePtr = Marshal.AllocHGlobal(IntPtr.Size); 
        Marshal.WriteIntPtr(stridePtr, (IntPtr)(VideoWidth * 3));

        IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("sw");
        mpv_render_param[] createParams = new mpv_render_param[] {
            new mpv_render_param { type = 1, data = apiTypePtr },
            new mpv_render_param { type = 0, data = IntPtr.Zero }
        };
        mpv_render_context_create(out renderContext, mpvHandle, createParams);
        Marshal.FreeHGlobal(apiTypePtr);

        mpv_command_string(mpvHandle, $"loadfile {videoPath}");
    }

    void Update()
    {
        if (mpvHandle == IntPtr.Zero || renderContext == IntPtr.Zero) return;

        if (mpv_render_context_update(renderContext) != 0)
        {
            UpdateVideoTexture();
        }
    }

    private void UpdateVideoTexture()
    {
        GCHandle bufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
        IntPtr bufferPtr = bufferHandle.AddrOfPinnedObject();

        try {
            mpv_render_param[] renderParams = new mpv_render_param[] {
                new mpv_render_param { type = 17, data = sizePtr },
                new mpv_render_param { type = 18, data = formatPtr },
                new mpv_render_param { type = 19, data = stridePtr },
                new mpv_render_param { type = 20, data = bufferPtr },
                new mpv_render_param { type = 0, data = IntPtr.Zero }
            };
            mpv_render_context_render(renderContext, renderParams);
            
            videoTexture.LoadRawTextureData(frameBuffer);
            videoTexture.Apply();
        } finally {
            bufferHandle.Free();
            // Itt kivettük a FreeHGlobal parancsokat, mert azokat most az OnDestroy kezeli!
        }
    }

    void OnDestroy()
    {
        if (renderContext != IntPtr.Zero) mpv_render_context_free(renderContext);
        if (mpvHandle != IntPtr.Zero) mpv_terminate_destroy(mpvHandle);

        // Memóriaszemét eltakarítása a kilépéskor
        if (sizePtr != IntPtr.Zero) Marshal.FreeHGlobal(sizePtr);
        if (formatPtr != IntPtr.Zero) Marshal.FreeHGlobal(formatPtr);
        if (stridePtr != IntPtr.Zero) Marshal.FreeHGlobal(stridePtr);
    }
}