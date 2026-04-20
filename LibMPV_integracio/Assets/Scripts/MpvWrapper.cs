using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class MpvWrapper : MonoBehaviour
{
    // A DllImport megkeresi a Plugins/Linux/x86_64 mappában lévő libmpv.so fájlt
    private const string MpvLibName = "mpv"; 

    // Alapvető mpv C függvények bekötése C#-ba
    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport(MpvLibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    private IntPtr mpvHandle;

    void Start()
    {
        // 1. MPV példány létrehozása a memóriában
        mpvHandle = mpv_create();
        
        if (mpvHandle == IntPtr.Zero)
        {
            Debug.LogError("Kritikus hiba: Nem sikerült betölteni a libmpv.so fájlt! Ellenőrizd a Plugins mappát.");
            return;
        }

        // 2. MPV inicializálása
        int initResult = mpv_initialize(mpvHandle);
        
        if (initResult < 0)
        {
            Debug.LogError($"Hiba az MPV inicializálásakor. Hibakód: {initResult}");
            return;
        }

        Debug.Log("Siker: A Unity stabilan kapcsolódott a natív libmpv-hez!");
    }

    void OnDestroy()
    {
        // Memóriaszivárgás elkerülése: ha kilépünk, bezárjuk az mpv-t is
        if (mpvHandle != IntPtr.Zero)
        {
            mpv_terminate_destroy(mpvHandle);
        }
    }
}