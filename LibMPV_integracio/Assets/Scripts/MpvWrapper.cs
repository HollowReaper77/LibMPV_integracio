using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class MpvWrapper : MonoBehaviour
{
    private const string MpvLibName = "mpv";

    // --- 1. ALAPVETŐ MPV FÜGGVÉNYEK (C könyvtár importálása) ---
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    // --- 2. RENDERELŐ (KÉP-KIMÁSOLÓ) FÜGGVÉNYEK ---
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, IntPtr[] param);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_update(IntPtr ctx);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr ctx, IntPtr[] param);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr ctx);
    
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

    [Header("Unity Megjelenítés")]
    public RawImage videoScreen; // Ide kell behúzni a UI réteget az Inspectorban
    
    [Header("4K Memóriakezelés")]
    private const int VideoWidth = 3840;
    private const int VideoHeight = 2160;
    
    private Texture2D videoTexture;
    private IntPtr mpvHandle;
    private IntPtr renderContext;
    private byte[] frameBuffer; // A 33 MB-os állandó tárolónk

    void Start()
    {
        // 1. MPV mag elindítása
        mpvHandle = mpv_create();
        if (mpvHandle == IntPtr.Zero)
        {
            Debug.LogError("Kritikus hiba: Nem találom a libmpv.so fájlt a Plugins mappában!");
            return;
        }

        mpv_initialize(mpvHandle);
        Debug.Log("Siker: Az mpv mag betöltve és fut!");

        // 2. A 4K Textúra és a fizikai memóriatároló lefoglalása
        videoTexture = new Texture2D(VideoWidth, VideoHeight, TextureFormat.BGRA32, false);
        frameBuffer = new byte[VideoWidth * VideoHeight * 4];

        // Rákötjük a textúrát a Unity 2D-s vásznára
        if (videoScreen != null)
        {
            videoScreen.texture = videoTexture;
        }

        // 3. Renderelő környezet inicializálása (Itt mondjuk meg a C kódnak, hogy szoftveres/memóriába rajzolást kérünk)
        // (A GitHub-os wrapperből ide jön majd egy paramétertömb, egyelőre null-lal indítjuk a hidat)
        mpv_render_context_create(out renderContext, mpvHandle, null);
        
        // A videó abszolút elérési útja
        string videoPath = "/home/hollowreaper/Letöltések/welcome.mp4";

        // Kiadjuk a lejátszási parancsot a C motornak
        mpv_command_string(mpvHandle, $"loadfile {videoPath}");
    }

    void Update()
    {
        // Ha valamiért leállt a mag, nem csinálunk semmit
        if (mpvHandle == IntPtr.Zero || renderContext == IntPtr.Zero) return;

        // 1. Megkérdezzük az mpv-t: Van új, kirajzolásra kész 4K képkocka?
        if (mpv_render_context_update(renderContext) != 0)
        {
            // 2. Belemásoltatjuk a C kóddal a pixeleket a mi frameBuffer tömbünkbe
            GetFramePixelsFromMpv();
            
            // 3. Rátöltjük a friss adatot a Unity textúrájára, és beküldjük a videókártyának
            videoTexture.LoadRawTextureData(frameBuffer);
            videoTexture.Apply();
        }
    }

    private void GetFramePixelsFromMpv()
    {
        // "Lehorgonyozzuk" a tömböt a memóriában, hogy a Unity ne piszkáljon bele, amíg a C kód dolgozik
        GCHandle bufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
        IntPtr bufferPtr = bufferHandle.AddrOfPinnedObject();

        try
        {
            // --- ITT FOG MEGTÖRTÉNNI A CSODA ---
            // A GitHub-os Mpv.NET wrapperből átemelt paraméter-struktúrák (felbontás, színkód, memória mutató)
            // ide fognak bekerülni, majd meghívják a natív renderelést:
            // mpv_render_context_render(renderContext, renderParams);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Hiba a képkocka másolásakor: {ex.Message}");
        }
        finally
        {
            // Nagyon fontos: Feloldjuk a horgonyt, különben pillanatok alatt elfogy a Kiosk memóriája!
            bufferHandle.Free();
        }
    }

    void OnDestroy()
    {
        // Szabályos kilépés: mindent bezárunk és takarítunk magunk után
        if (renderContext != IntPtr.Zero)
        {
            mpv_render_context_free(renderContext);
        }
        
        if (mpvHandle != IntPtr.Zero)
        {
            mpv_terminate_destroy(mpvHandle);
        }
    }
}