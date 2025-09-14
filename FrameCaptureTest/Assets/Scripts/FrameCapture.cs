// AutoFrameSaver.cs
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class FrameCapture : MonoBehaviour
{
    [Header("When to capture")]
    public bool autoStart = true;          // start capturing at Play
    public KeyCode toggleKey = KeyCode.P;  // press to start/stop

    [Header("Frame stepping (offline friendly)")]
    public bool lockSimFrameRate = true;   // deterministic offline capture
    public int targetFramerate = 30;       // simulated fps when locked

    [Header("Output")]
    public string folderName = "Captures"; // under Application.persistentDataPath
    public string filePrefix = "frame_";
    public bool useEXR = false;            // PNG (8-bit) vs EXR (HDR, linear)
    public int supersize = 1;              // for PNG only (Game view upscaling)

    [Header("Camera (optional)")]
    public Camera captureCam;              // defaults to Camera.main if empty
    public int width = 1920;               // used for EXR path (offscreen)
    public int height = 1080;              // used for EXR path (offscreen)

    bool capturing;
    int frameIndex;
    string outDir;

    // for EXR path
    RenderTexture rt;
    Texture2D cpuTex;

    void Start()
    {
        if (captureCam == null) captureCam = Camera.main;

        outDir = Path.Combine(Application.persistentDataPath, folderName);
        Directory.CreateDirectory(outDir);

        if (autoStart) StartCapture();
    }

    void OnDestroy()
    {
        CleanupEXR();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (capturing) StopCapture();
            else StartCapture();
        }
    }

    void OnEnable()
    {
        Application.quitting += CleanupEXR;
    }
    void OnDisable()
    {
        Application.quitting -= CleanupEXR;
    }

    void StartCapture()
    {
        if (capturing) return;

        if (lockSimFrameRate)
        {
            Time.captureFramerate = targetFramerate; // fixed timestep; rendering may run slower
        }

        if (useEXR)
        {
            if (captureCam == null)
            {
                Debug.LogError("AutoFrameSaver: No capture camera found for EXR mode.");
                return;
            }
            SetupEXRResources();
        }

        capturing = true;
        frameIndex = 0;
        StartCoroutine(CaptureLoop());
        Debug.Log($"AutoFrameSaver: capturing to {outDir} (EXR={useEXR})");
    }

    void StopCapture()
    {
        if (!capturing) return;
        capturing = false;
        Time.captureFramerate = 0; // restore realtime
        CleanupEXR();
        Debug.Log("AutoFrameSaver: stopped capturing.");
    }

    System.Collections.IEnumerator CaptureLoop()
    {
        // Capture one file per simulated frame
        while (capturing)
        {
            yield return new WaitForEndOfFrame();

            if (useEXR)
            {
                CaptureEXRFrame();
            }
            else
            {
                // Smallest, zero-boilerplate option (Game view)
                string path = Path.Combine(outDir, $"{filePrefix}{frameIndex:D6}.png");
                ScreenCapture.CaptureScreenshot(path, supersize);
                // Note: writing is async; fine for non-realtime offline dumps
            }

            frameIndex++;
        }
    }

    // ---------- EXR path (off-screen, HDR/linear, resolution-controlled) ----------
    void SetupEXRResources()
    {
        rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf);
        rt.Create();
        cpuTex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, /*linear*/ true);
        captureCam.targetTexture = rt;
    }

    void CleanupEXR()
    {
        if (captureCam != null) captureCam.targetTexture = null;
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        if (cpuTex != null) { Destroy(cpuTex); cpuTex = null; }
    }

    void CaptureEXRFrame()
    {
        // Render off-screen at exact width/height, independent of Game view
        captureCam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        cpuTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        cpuTex.Apply(false, false);
        RenderTexture.active = prev;

        var bytes = cpuTex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
        string path = Path.Combine(outDir, $"{filePrefix}{frameIndex:D6}.exr");
        File.WriteAllBytes(path, bytes);
    }
    public void ExitGame()
    {
        Application.Quit();
    }
}
