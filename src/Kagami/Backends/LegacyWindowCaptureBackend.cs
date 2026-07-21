using System.Diagnostics;
using System.Runtime.InteropServices;
using Kagami.Utilities;

namespace Kagami.Backends;

/// <summary>
/// Captures a single window surface using window-specific APIs.
/// Strategy priority:
///   1. PrintWindow (PW_RENDERFULLCONTENT) — captures window's own render surface
///   2. DWM Thumbnail — occlusion-free via compositor
///   3. GDI CopyFromScreen (fallback, has occlusion risk)
///
/// NOTE: This is NOT Windows.Graphics.Capture (WGC). WGC requires WinRT interop
/// (IGraphicsCaptureItemInterop) and is not yet implemented. This backend uses
/// legacy Win32 APIs that can capture window content occlusion-free in many cases.
/// </summary>
public class LegacyWindowCaptureBackend : ICaptureBackend
{
    public string Name => "legacy_window_capture";

    public bool IsAvailable
    {
        get
        {
            try { return Environment.OSVersion.Version.Build >= 18362; }
            catch { return false; }
        }
    }

    public Task<CaptureResult?> CaptureAsync(CaptureOptions options, CancellationToken ct)
    {
        if (!IsAvailable || options.Hwnd is null)
            return Task.FromResult<CaptureResult?>(null);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var hwnd = options.Hwnd.Value;

            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return null;

            // ── Geometry strategy (derived from diagnostics) ──
            //
            // For Avalonia WindowChrome windows on Win10 with DWM:
            //
            //   GetWindowRect:     L=182 T=182 R=1498 B=945  (W=1316 H=763)
            //   DWM Extended:      L=189 T=182 R=1491 B=938  (W=1302 H=756)
            //   GetClientRect:     W=1300 H=754
            //   ClientToScreen:    (190, 183)
            //
            // GetWindowRect includes invisible DWM shadow (7px left, 0px top,
            // 7px right, 7px bottom).
            //
            // DWM_EXTENDED_FRAME_BOUNDS is the visual window frame. It's offset
            // from ClientToScreen by exactly 1px on each side (the 1px border
            // that Avalonia renders for the window's own chrome).
            //
            // The real content is at ClientToScreen(0,0) with size from GetClientRect.
            //
            // PrintWindow draws the COMPLETE window surface starting at (0,0) of
            // the window's DC, with the exact size of GetWindowRect minus shadow.
            // We capture the full surface at GWR size, then crop.

            NativeMethods.GetWindowRect(hwnd, out var gwr);
            NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>());
            NativeMethods.GetClientRect(hwnd, out var cr);

            // Where does client (0,0) map to in screen coordinates?
            var clientOrigin = new POINT();
            NativeMethods.ClientToScreen(hwnd, ref clientOrigin);

            int srcW = gwr.Right - gwr.Left;
            int srcH = gwr.Bottom - gwr.Top;

            int contentScreenX = clientOrigin.X;
            int contentScreenY = clientOrigin.Y;
            int contentW = cr.Right - cr.Left;
            int contentH = cr.Bottom - cr.Top;

            // Strategy 1: PrintWindow — captures the window DC surface.
            // We draw at the full GWR size, then crop to the actual content region.
            var result = CaptureViaPrintWindow(hwnd, gwr.Left, gwr.Top, srcW, srcH,
                contentScreenX, contentScreenY, contentW, contentH, options);
            if (result is not null)
                return result;

            // Strategy 2: DWM Thumbnail — occlusion-free compositor capture
            result = CaptureViaDwmThumbnail(hwnd, contentScreenX, contentScreenY, contentW, contentH, options);
            if (result is not null)
                return result;

            // Strategy 3: GDI fallback — CopyFromScreen (admits occlusion)
            return CaptureViaGdiFallback(contentScreenX, contentScreenY, contentW, contentH, options);
        }, ct);
    }

    /// <summary>
    /// PrintWindow: captures the window's DC surface at the full GWR size,
    /// then crops to the actual client content area.
    /// </summary>
    private static CaptureResult? CaptureViaPrintWindow(IntPtr hwnd,
        int srcX, int srcY, int srcW, int srcH,
        int cropX, int cropY, int cropW, int cropH,
        CaptureOptions options)
    {
        try
        {
            const int PW_RENDERFULLCONTENT = 0x00000002;

            using var bitmap = new System.Drawing.Bitmap(srcW, srcH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                bool success = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                if (!success)
                    return null;
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (IsBlankCapture(bitmap, srcW, srcH))
                return null;

            // Crop: the content starts at (cropX - srcX, cropY - srcY) in the bitmap
            int offsetX = cropX - srcX;
            int offsetY = cropY - srcY;

            // Clamp crop region to within the source bitmap
            offsetX = Math.Max(0, offsetX);
            offsetY = Math.Max(0, offsetY);
            cropW = Math.Min(cropW, srcW - offsetX);
            cropH = Math.Min(cropH, srcH - offsetY);

            if (cropW <= 0 || cropH <= 0)
                return null;

            using var croppedBitmap = bitmap.Clone(
                new System.Drawing.Rectangle(offsetX, offsetY, cropW, cropH),
                bitmap.PixelFormat);

            var path = options.OutputPath ?? TempFileManager.GetScreenshotPath();
            croppedBitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            return new CaptureResult
            {
                FilePath = path,
                Width = cropW,
                Height = cropH,
                X = cropX,
                Y = cropY,
                CaptureBackend = "legacy_window_capture",
                CaptureMethod = CaptureMethod.PrintWindow,
                ActualMode = "window",
                RequestedMode = options.RequestedMode.ToString().ToLowerInvariant(),
                FallbackUsed = false,
                OcclusionPossible = false
            };
        }
        catch
        {
            return null;
        }
    }

    private static CaptureResult? CaptureViaDwmThumbnail(IntPtr hwnd, int x, int y, int width, int height, CaptureOptions options)
    {
        IntPtr thumb = IntPtr.Zero;
        IntPtr parentHwnd = IntPtr.Zero;

        try
        {
            parentHwnd = CreateWindowEx(
                0, "STATIC", "", 0,
                0, 0, width, height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (parentHwnd == IntPtr.Zero)
                return null;

            int hr = DwmRegisterThumbnail(parentHwnd, hwnd, out thumb);
            if (hr != 0 || thumb == IntPtr.Zero)
                return null;

            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY,
                opacity = 255,
                fVisible = true
            };

            props.rcDestination = new RECT { Left = 0, Top = 0, Right = width, Bottom = height };
            props.rcSource = new RECT { Left = 0, Top = 0, Right = width, Bottom = height };

            DwmUpdateThumbnailProperties(thumb, ref props);
            Thread.Sleep(100);

            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                PrintWindow(parentHwnd, hdc, 0x00000002);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (IsBlankCapture(bitmap, width, height))
                return null;

            var path = options.OutputPath ?? TempFileManager.GetScreenshotPath();
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            return new CaptureResult
            {
                FilePath = path,
                Width = width,
                Height = height,
                X = x,
                Y = y,
                CaptureBackend = "legacy_window_capture",
                CaptureMethod = CaptureMethod.DwmThumbnail,
                ActualMode = "window",
                RequestedMode = options.RequestedMode.ToString().ToLowerInvariant(),
                FallbackUsed = false,
                OcclusionPossible = false
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb);
            if (parentHwnd != IntPtr.Zero) DestroyWindow(parentHwnd);
        }
    }

    private static CaptureResult? CaptureViaGdiFallback(int x, int y, int width, int height, CaptureOptions options)
    {
        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));

            var path = options.OutputPath ?? TempFileManager.GetScreenshotPath();
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            return new CaptureResult
            {
                FilePath = path,
                Width = width,
                Height = height,
                X = x,
                Y = y,
                CaptureBackend = "legacy_window_capture",
                CaptureMethod = CaptureMethod.GdiCopyFromScreen,
                ActualMode = "visible-desktop-crop",
                RequestedMode = options.RequestedMode.ToString().ToLowerInvariant(),
                FallbackUsed = true,
                OcclusionPossible = true
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBlankCapture(System.Drawing.Bitmap bitmap, int width, int height)
    {
        int[] xs = [width / 4, width / 2, 3 * width / 4];
        int[] ys = [height / 4, height / 2, 3 * height / 4];

        var colors = new HashSet<int>();
        foreach (var sx in xs)
        foreach (var sy in ys)
        {
            if (sx < width && sy < height)
                colors.Add(bitmap.GetPixel(sx, sy).ToArgb());
        }

        return colors.Count <= 1;
    }

    // ── P/Invoke ──

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, int nFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DWM_THUMBNAIL_PROPERTIES ptnProperties);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    private const uint DWM_TNP_RECTSOURCE = 0x00000002;
    private const uint DWM_TNP_OPACITY = 0x00000004;
    private const uint DWM_TNP_VISIBLE = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        public bool fVisible;
        public bool fSourceClientAreaOnly;
    }
}
