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
    
    // IL2CPP JAVÍTÁS: IntPtr a tömbök helyett
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, IntPtr param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_update(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr ctx, IntPtr param);
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

    private GCHandle bufferHandle;
    private IntPtr sizePtr, formatPtr, stridePtr, renderParamsPtr;

    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        mpvHandle = mpv_create();
        if (mpvHandle == IntPtr.Zero) return;

        mpv_set_option_string(mpvHandle, "vo", "libmpv");
        mpv_set_option_string(mpvHandle, "hwdec", "vulkan-copy"); 
        
        mpv_initialize(mpvHandle);
        
        // JAVÍTÁS 1: Textúra RGBA32 formátumban (4 bájt / pixel)
        videoTexture = new Texture2D(VideoWidth, VideoHeight, TextureFormat.RGBA32, false);
        
        // JAVÍTÁS 2: Buffer mérete 4-es szorzóval
        frameBuffer = new byte[VideoWidth * VideoHeight * 4];
        
        if (videoScreen != null) videoScreen.texture = videoTexture;

        bufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
        IntPtr bufferPtr = bufferHandle.AddrOfPinnedObject();

        sizePtr = Marshal.AllocHGlobal(8); 
        Marshal.Copy(new int[] { VideoWidth, VideoHeight }, 0, sizePtr, 2);
        
        // JAVÍTÁS 3: Az mpv formátuma rgba-ra állítva
        formatPtr = Marshal.StringToHGlobalAnsi("rgba");
        
        stridePtr = Marshal.AllocHGlobal(IntPtr.Size); 
        
        // JAVÍTÁS 4: Sorhossz (stride) 4-es szorzóval
        Marshal.WriteIntPtr(stridePtr, (IntPtr)(VideoWidth * 4));

        int paramSize = Marshal.SizeOf<mpv_render_param>();

        // IL2CPP JAVÍTÁS: Create paraméterek kézi másolása
        IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("sw");
        IntPtr createParamsPtr = Marshal.AllocHGlobal(paramSize * 2);
        Marshal.StructureToPtr(new mpv_render_param { type = 1, data = apiTypePtr }, createParamsPtr, false);
        Marshal.StructureToPtr(new mpv_render_param { type = 0, data = IntPtr.Zero }, new IntPtr(createParamsPtr.ToInt64() + paramSize), false);

        mpv_render_context_create(out renderContext, mpvHandle, createParamsPtr);
        
        Marshal.FreeHGlobal(createParamsPtr);
        Marshal.FreeHGlobal(apiTypePtr);

        // IL2CPP JAVÍTÁS: Render paraméterek előre lefoglalása a gyorsabb Update-hez
        renderParamsPtr = Marshal.AllocHGlobal(paramSize * 5);
        mpv_render_param[] rParams = new mpv_render_param[] {
            new mpv_render_param { type = 17, data = sizePtr },
            new mpv_render_param { type = 18, data = formatPtr },
            new mpv_render_param { type = 19, data = stridePtr },
            new mpv_render_param { type = 20, data = bufferPtr },
            new mpv_render_param { type = 0, data = IntPtr.Zero }
        };
        for (int i = 0; i < 5; i++)
        {
            Marshal.StructureToPtr(rParams[i], new IntPtr(renderParamsPtr.ToInt64() + (i * paramSize)), false);
        }

        mpv_command_string(mpvHandle, "set loop-file yes");
        mpv_command_string(mpvHandle, $"loadfile {videoPath}");
    }

    void Update()
    {
        if (mpvHandle == IntPtr.Zero || renderContext == IntPtr.Zero) return;

        if (mpv_render_context_update(renderContext) != 0)
        {
            // Nulla memóriafoglalás az Update-ben
            mpv_render_context_render(renderContext, renderParamsPtr);
            videoTexture.LoadRawTextureData(frameBuffer);
            videoTexture.Apply();
        }
    }

    void OnDestroy()
    {
        if (renderContext != IntPtr.Zero) mpv_render_context_free(renderContext);
        if (mpvHandle != IntPtr.Zero) mpv_terminate_destroy(mpvHandle);

        if (bufferHandle.IsAllocated) bufferHandle.Free();
        if (sizePtr != IntPtr.Zero) Marshal.FreeHGlobal(sizePtr);
        if (formatPtr != IntPtr.Zero) Marshal.FreeHGlobal(formatPtr);
        if (stridePtr != IntPtr.Zero) Marshal.FreeHGlobal(stridePtr);
        if (renderParamsPtr != IntPtr.Zero) Marshal.FreeHGlobal(renderParamsPtr);
    }
}