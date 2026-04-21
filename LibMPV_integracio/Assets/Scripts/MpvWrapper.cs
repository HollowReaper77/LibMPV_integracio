using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class MpvManager : MonoBehaviour
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
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, IntPtr param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_update(IntPtr ctx);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr ctx, IntPtr param);
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr ctx);

    [Header("Maximum 3 Videó Slot")]
    public RawImage[] videoScreens = new RawImage[3];
    
    // BEÉGETETT ÚTVONALAK MINDHÁROM SLOTHOZ
    public string[] videoPaths = new string[3] 
    { 
        "/godzie/assets/default/backgrounds/welcome.mp4", 
        "/godzie/assets/default/backgrounds/welcome.mp4", 
        "/godzie/assets/default/backgrounds/welcome.mp4" 
    };

    private const int VideoWidth = 3840;
    private const int VideoHeight = 2160;

    private class MpvStream
    {
        public bool isInitialized = false;
        public IntPtr handle;
        public IntPtr renderContext;
        public Texture2D texture;
        public byte[] frameBuffer;
        public GCHandle bufferHandle;
        public IntPtr sizePtr, formatPtr, stridePtr, apiTypePtr;
        public IntPtr createParamsPtr, renderParamsPtr;
    }

    private MpvStream[] streams = new MpvStream[3];

    void Start()
    {
        if (Application.targetFrameRate != 60) {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }

        int paramSize = Marshal.SizeOf<mpv_render_param>();

        for (int i = 0; i < 3; i++)
        {
            streams[i] = new MpvStream();
            MpvStream s = streams[i];

            if (string.IsNullOrEmpty(videoPaths[i])) 
            {
                if (videoScreens[i] != null) videoScreens[i].gameObject.SetActive(false);
                continue;
            }

            if (videoScreens[i] != null) videoScreens[i].gameObject.SetActive(true);

            s.handle = mpv_create();
            if (s.handle == IntPtr.Zero) continue;

            mpv_set_option_string(s.handle, "vo", "libmpv");
            mpv_set_option_string(s.handle, "gpu-api", "opengl");
            mpv_set_option_string(s.handle, "hwdec", "vaapi-copy");

            mpv_initialize(s.handle);

            s.texture = new Texture2D(VideoWidth, VideoHeight, TextureFormat.RGB24, false);
            s.frameBuffer = new byte[VideoWidth * VideoHeight * 3];
            videoScreens[i].texture = s.texture;

            s.bufferHandle = GCHandle.Alloc(s.frameBuffer, GCHandleType.Pinned);
            IntPtr bufferPtr = s.bufferHandle.AddrOfPinnedObject();

            s.sizePtr = Marshal.AllocHGlobal(8);
            Marshal.Copy(new int[] { VideoWidth, VideoHeight }, 0, s.sizePtr, 2);
            s.formatPtr = Marshal.StringToHGlobalAnsi("rgb24");
            s.stridePtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(s.stridePtr, (IntPtr)(VideoWidth * 3));
            s.apiTypePtr = Marshal.StringToHGlobalAnsi("sw");

            mpv_render_param[] cParams = new mpv_render_param[] {
                new mpv_render_param { type = 1, data = s.apiTypePtr },
                new mpv_render_param { type = 0, data = IntPtr.Zero }
            };
            s.createParamsPtr = Marshal.AllocHGlobal(paramSize * 2);
            for (int j = 0; j < 2; j++) Marshal.StructureToPtr(cParams[j], new IntPtr(s.createParamsPtr.ToInt64() + (j * paramSize)), false);

            mpv_render_context_create(out s.renderContext, s.handle, s.createParamsPtr);

            mpv_render_param[] rParams = new mpv_render_param[] {
                new mpv_render_param { type = 17, data = s.sizePtr },
                new mpv_render_param { type = 18, data = s.formatPtr },
                new mpv_render_param { type = 19, data = s.stridePtr },
                new mpv_render_param { type = 20, data = bufferPtr },
                new mpv_render_param { type = 0, data = IntPtr.Zero }
            };
            s.renderParamsPtr = Marshal.AllocHGlobal(paramSize * 5);
            for (int j = 0; j < 5; j++) Marshal.StructureToPtr(rParams[j], new IntPtr(s.renderParamsPtr.ToInt64() + (j * paramSize)), false);

            mpv_command_string(s.handle, "set loop-file yes");
            mpv_command_string(s.handle, $"loadfile {videoPaths[i]}");

            s.isInitialized = true;
        }
    }

    void Update()
    {
        for (int i = 0; i < 3; i++)
        {
            MpvStream s = streams[i];
            if (!s.isInitialized || s.handle == IntPtr.Zero || s.renderContext == IntPtr.Zero) continue;

            if (mpv_render_context_update(s.renderContext) != 0)
            {
                mpv_render_context_render(s.renderContext, s.renderParamsPtr);
                s.texture.LoadRawTextureData(s.frameBuffer);
                s.texture.Apply();
            }
        }
    }

    void OnDestroy()
    {
        for (int i = 0; i < 3; i++)
        {
            MpvStream s = streams[i];
            if (!s.isInitialized) continue;

            if (s.renderContext != IntPtr.Zero) mpv_render_context_free(s.renderContext);
            if (s.handle != IntPtr.Zero) mpv_terminate_destroy(s.handle);

            if (s.bufferHandle.IsAllocated) s.bufferHandle.Free();
            if (s.sizePtr != IntPtr.Zero) Marshal.FreeHGlobal(s.sizePtr);
            if (s.formatPtr != IntPtr.Zero) Marshal.FreeHGlobal(s.formatPtr);
            if (s.stridePtr != IntPtr.Zero) Marshal.FreeHGlobal(s.stridePtr);
            if (s.apiTypePtr != IntPtr.Zero) Marshal.FreeHGlobal(s.apiTypePtr);
            if (s.createParamsPtr != IntPtr.Zero) Marshal.FreeHGlobal(s.createParamsPtr);
            if (s.renderParamsPtr != IntPtr.Zero) Marshal.FreeHGlobal(s.renderParamsPtr);
        }
    }
}