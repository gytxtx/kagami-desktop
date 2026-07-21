using System.Drawing;
using System.Drawing.Imaging;
using Kagami.Utilities;

namespace Kagami.Backends;

/// <summary>
/// Legacy GDI-based screenshot fallback. Uses BitBlt-style capture.
/// May fail or return black/blank for hardware-accelerated windows.
/// Only used as a last resort when DXGI is unavailable.
/// </summary>
public class LegacyCaptureBackend : ICaptureBackend
{
    public string Name => "legacy_gdi";

    public bool IsAvailable => true; // Always available on Windows

    public Task<CaptureResult?> CaptureAsync(CaptureOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            int x = options.X ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = options.Y ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = options.Width ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = options.Height ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            return TryCapture(x, y, w, h, options);
        }, ct);
    }

    public static CaptureResult? TryCapture(int x, int y, int width, int height, CaptureOptions options)
    {
        try
        {
            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));

            var path = options.OutputPath ?? TempFileManager.GetScreenshotPath();
            bitmap.Save(path, ImageFormat.Png);

            return new CaptureResult
            {
                FilePath = path,
                Width = width,
                Height = height,
                X = x,
                Y = y,
                CaptureBackend = "legacy_gdi",
                CaptureMethod = CaptureMethod.GdiCopyFromScreen,
                ActualMode = "visible-desktop",
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
}
