using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class MpvWrapper : MonoBehaviour
{
    private const string MpvLibName = "mpv";

    // --- C STRUKTÚRA A PARAMÉTEREKHEZ ---
    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_render_param
    {
        public int type;
        public IntPtr data;
    }

    // --- ALAP FÜGGVÉNYEK ---
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

    // --- RENDERELŐ FÜGGVÉNYEK (Itt már a saját struktúránkat használjuk) ---
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, mpv_render_param[] param);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_update(IntPtr ctx);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr ctx, mpv_render_param[] param);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr ctx);

    [Header("Unity Megjelenítés")]
    public RawImage videoScreen; 
    
    [Header("4K Memóriakezelés")]
    private const int VideoWidth = 3840;
    private const int VideoHeight = 2160;
    
    private Texture2D videoTexture;
    private IntPtr mpvHandle;
    private IntPtr renderContext;
    private byte[] frameBuffer;

    void Start()
    {
        mpvHandle = mpv_create();
        if (mpvHandle == IntPtr.Zero) return;

        // 1. MEGTILTJUK A SAJÁT ABLAKOT! (Átirányítjuk a Render API-ba)
        mpv_set_option_string(mpvHandle, "vo", "libmpv");

        mpv_initialize(mpvHandle);
        
        videoTexture = new Texture2D(VideoWidth, VideoHeight, TextureFormat.BGRA32, false);
        frameBuffer = new byte[VideoWidth * VideoHeight * 4];

        if (videoScreen != null) videoScreen.texture = videoTexture;

        // 2. Szoftveres renderelő környezet kérése ("sw")
        IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("sw");
        mpv_render_param[] createParams = new mpv_render_param[]
        {
            new mpv_render_param { type = 1, data = apiTypePtr }, // 1 = MPV_RENDER_PARAM_API_TYPE
            new mpv_render_param { type = 0, data = IntPtr.Zero } // C-szabvány lezáró nulla
        };
        
        mpv_render_context_create(out renderContext, mpvHandle, createParams);
        Marshal.FreeHGlobal(apiTypePtr); // Memóriatakarítás

        // 3. A videó elindítása
        string videoPath = "/home/hollowreaper/Letöltések/welcome.mp4";
        mpv_command_string(mpvHandle, $"loadfile {videoPath}");
    }

    void Update()
    {
        if (mpvHandle == IntPtr.Zero || renderContext == IntPtr.Zero) return;

        if (mpv_render_context_update(renderContext) != 0)
        {
            GetFramePixelsFromMpv();
            videoTexture.LoadRawTextureData(frameBuffer);
            videoTexture.Apply();
        }
    }

    private void GetFramePixelsFromMpv()
    {
        GCHandle bufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
        IntPtr bufferPtr = bufferHandle.AddrOfPinnedObject();

        // C paraméterek memóriaterületének előkészítése
        int[] size = new int[] { VideoWidth, VideoHeight };
        IntPtr sizePtr = Marshal.AllocHGlobal(sizeof(int) * 2);
        Marshal.Copy(size, 0, sizePtr, 2);

        IntPtr formatPtr = Marshal.StringToHGlobalAnsi("bgra");

        IntPtr stridePtr = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(stridePtr, (IntPtr)(VideoWidth * 4));

        try
        {
            // A pontos utasítás a C motornak: Mekkora, milyen formátumú, és hova kérjük!
            mpv_render_param[] renderParams = new mpv_render_param[]
            {
                new mpv_render_param { type = 5, data = sizePtr },    // 5 = SW_SIZE
                new mpv_render_param { type = 6, data = formatPtr },  // 6 = SW_FORMAT
                new mpv_render_param { type = 7, data = stridePtr },  // 7 = SW_STRIDE
                new mpv_render_param { type = 8, data = bufferPtr },  // 8 = SW_POINTER (ide folyik be a kép!)
                new mpv_render_param { type = 0, data = IntPtr.Zero }
            };

            mpv_render_context_render(renderContext, renderParams);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Hiba a képkocka másolásakor: {ex.Message}");
        }
        finally
        {
            // Horgonyok és memóriaszemét eltakarítása
            bufferHandle.Free();
            Marshal.FreeHGlobal(sizePtr);
            Marshal.FreeHGlobal(formatPtr);
            Marshal.FreeHGlobal(stridePtr);
        }
    }

    void OnDestroy()
    {
        if (renderContext != IntPtr.Zero) mpv_render_context_free(renderContext);
        if (mpvHandle != IntPtr.Zero) mpv_terminate_destroy(mpvHandle);
    }
}