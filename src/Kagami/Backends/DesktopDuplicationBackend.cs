using System.Diagnostics;
using System.Runtime.InteropServices;
using Kagami.Utilities;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using D3D11 = SharpDX.Direct3D11;
using Dxgi = SharpDX.DXGI;

namespace Kagami.Backends;

/// <summary>
/// Captures desktop regions using DXGI Desktop Duplication API.
/// Captures the composited desktop frame buffer — works for all window types
/// including hardware-accelerated rendering (Avalonia, WPF, games).
///
/// Unlike GDI CopyFromScreen, DXGI reads directly from the GPU framebuffer
/// and can capture hardware-accelerated content that GDI misses.
///
/// Limitation: captures what is VISIBLE on the desktop (occlusion possible).
/// For occlusion-free window capture, use WindowsGraphicsCaptureBackend.
/// </summary>
public class DesktopDuplicationBackend : ICaptureBackend
{
    public string Name => "desktop_duplication";

    public bool IsAvailable
    {
        get
        {
            try { return Environment.OSVersion.Version.Build >= 9200; }
            catch { return false; }
        }
    }

    public Task<CaptureResult?> CaptureAsync(CaptureOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            int x = options.X ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = options.Y ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = options.Width ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = options.Height ?? NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            try
            {
                return CaptureDxgi(x, y, w, h, options);
            }
            catch
            {
                // DXGI failure — return null to let CaptureService choose the next backend.
                // Do NOT internally fall back to GDI; that bypasses the service's policy.
                return null;
            }
        }, ct);
    }

    private static unsafe CaptureResult? CaptureDxgi(int x, int y, int width, int height, CaptureOptions options)
    {
        using var factory = new Dxgi.Factory1();

        // Find the adapter and output covering our target coordinates.
        // Select the output whose desktop bounds contain the center of the region;
        // fall back to the output with the largest overlap area.
        Adapter1? bestAdapter = null;
        Output? bestOutput = null;
        int bestScore = -1;
        var adaptersToDispose = new List<Adapter1>();
        var outputsToDispose = new List<Output>();

        for (int i = 0; i < factory.GetAdapterCount1(); i++)
        {
            var adapter = factory.GetAdapter1(i);
            adaptersToDispose.Add(adapter);

            for (int j = 0; j < adapter.GetOutputCount(); j++)
            {
                var output = adapter.GetOutput(j);
                outputsToDispose.Add(output);
                var desc = output.Description;
                var or = desc.DesktopBounds;

                // Prefer outputs containing the center point of our region
                int cx = x + width / 2;
                int cy = y + height / 2;

                if (cx >= or.Left && cx < or.Right && cy >= or.Top && cy < or.Bottom)
                {
                    bestAdapter = adapter;
                    bestOutput = output;
                    break;
                }

                // Check overlap as fallback — select the output with LARGEST area
                if (x < or.Right && x + width > or.Left && y < or.Bottom && y + height > or.Top)
                {
                    int overlapX = Math.Min(x + width, or.Right) - Math.Max(x, or.Left);
                    int overlapY = Math.Min(y + height, or.Bottom) - Math.Max(y, or.Top);
                    int overlap = overlapX * overlapY;
                    if (overlap > bestScore)
                    {
                        bestScore = overlap;
                        bestAdapter = adapter;
                        bestOutput = output;
                    }
                }
            }

            if (bestAdapter is not null && bestOutput is not null) break;
        }

        // Dispose all adapters/outputs except the selected ones
        foreach (var a in adaptersToDispose)
            if (a != bestAdapter) a.Dispose();
        foreach (var o in outputsToDispose)
            if (o != bestOutput) o.Dispose();

        if (bestAdapter is null || bestOutput is null)
        {
            adaptersToDispose.ForEach(a => a.Dispose());
            outputsToDispose.ForEach(o => o.Dispose());
            return null;
        }

        try
        {
            var outDesc = bestOutput.Description;
            int outWidth = outDesc.DesktopBounds.Right - outDesc.DesktopBounds.Left;
            int outHeight = outDesc.DesktopBounds.Bottom - outDesc.DesktopBounds.Top;

            // Create D3D11 device for this adapter
            using var device = new D3D11.Device(bestAdapter, DeviceCreationFlags.None);

            // Get Output1 for duplication
            var output1 = bestOutput.QueryInterface<Dxgi.Output1>();

            try
            {
                using var duplication = output1.DuplicateOutput(device);

                // Acquire a frame
                Dxgi.OutputDuplicateFrameInformation frameInfo;
                Dxgi.Resource? screenResource = null;
                bool frameAcquired = false;

                // First try: quick acquire
                var result = duplication.TryAcquireNextFrame(50, out frameInfo, out screenResource);
                if (result.Failure || screenResource is null)
                {
                    screenResource?.Dispose();
                    screenResource = null;
                    // Second try with longer wait
                    result = duplication.TryAcquireNextFrame(500, out frameInfo, out screenResource);
                }

                if (result.Failure || screenResource is null)
                {
                    screenResource?.Dispose();
                    return null;
                }

                frameAcquired = true;

                try
                {
                    using var sourceTexture = screenResource.QueryInterface<D3D11.Texture2D>();

                    // Create a staging texture for CPU readback (full output size)
                    var stagingDesc = new Texture2DDescription
                    {
                        CpuAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None,
                        Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        Width = outWidth,
                        Height = outHeight,
                        OptionFlags = ResourceOptionFlags.None,
                        MipLevels = 1,
                        ArraySize = 1,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging
                    };

                    using var staging = new D3D11.Texture2D(device, stagingDesc);

                    // Copy full output to staging
                    device.ImmediateContext.CopyResource(sourceTexture, staging);

                    // Map the staging texture
                    var dataBox = device.ImmediateContext.MapSubresource(
                        staging, 0, MapMode.Read, D3D11.MapFlags.None);

                    try
                    {
                        // Calculate crop region relative to this output
                        int cropX = Math.Max(0, x - outDesc.DesktopBounds.Left);
                        int cropY = Math.Max(0, y - outDesc.DesktopBounds.Top);
                        int cropW = Math.Min(width, outWidth - cropX);
                        int cropH = Math.Min(height, outHeight - cropY);

                        // Read only the cropped region from the staging texture
                        var path = options.OutputPath ?? TempFileManager.GetScreenshotPath();
                        var croppedData = ExtractBgraRegion(
                            (byte*)dataBox.DataPointer, dataBox.RowPitch,
                            outWidth, outHeight,
                            cropX, cropY, cropW, cropH);

                        SaveAsPng(croppedData, cropW, cropH, path);

                        return new CaptureResult
                        {
                            FilePath = path,
                            Width = cropW,
                            Height = cropH,
                            X = outDesc.DesktopBounds.Left + cropX,
                            Y = outDesc.DesktopBounds.Top + cropY,
                            CaptureBackend = "desktop_duplication",
                            CaptureMethod = CaptureMethod.DxgiDesktopDuplication,
                            ActualMode = "visible-desktop",
                            RequestedMode = options.RequestedMode.ToString().ToLowerInvariant(),
                            FallbackUsed = false,
                            OcclusionPossible = true
                        };
                    }
                    finally
                    {
                        device.ImmediateContext.UnmapSubresource(staging, 0);
                    }
                }
                finally
                {
                    if (frameAcquired)
                        duplication.ReleaseFrame();
                }
            }
            finally
            {
                output1.Dispose();
            }
        }
        finally
        {
            bestOutput.Dispose();
            bestAdapter.Dispose();
        }
    }

    /// <summary>
    /// Extract a BGRA region from a full-frame staging buffer.
    /// </summary>
    private static unsafe byte[] ExtractBgraRegion(byte* src, int srcStride, int srcWidth, int srcHeight,
        int cropX, int cropY, int cropW, int cropH)
    {
        var result = new byte[cropW * cropH * 4];
        const int bpp = 4;

        for (int row = 0; row < cropH; row++)
        {
            int srcOffset = (cropY + row) * srcStride + cropX * bpp;
            int dstOffset = row * cropW * bpp;
            Marshal.Copy((IntPtr)(src + srcOffset), result, dstOffset, cropW * bpp);
        }

        return result;
    }

    /// <summary>
    /// Minimal PNG encoder for raw BGRA pixel data.
    /// Converts BGRA → RGBA during encoding.
    /// </summary>
    private static void SaveAsPng(byte[] bgra, int width, int height, string path)
    {
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];     // R ← B
            rgba[i + 1] = bgra[i + 1]; // G ← G
            rgba[i + 2] = bgra[i];     // B ← R
            rgba[i + 3] = bgra[i + 3]; // A ← A
        }

        using var ms = new MemoryStream();
        WritePng(ms, rgba, width, height);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WritePng(Stream stream, byte[] rgba, int width, int height)
    {
        using var bw = new BinaryWriter(stream);

        // PNG signature
        bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR chunk
        WriteChunk(bw, "IHDR", w =>
        {
            w.Write(BigEndian(width));
            w.Write(BigEndian(height));
            w.Write((byte)8);  // bit depth
            w.Write((byte)6);  // color type: RGBA
            w.Write((byte)0);  // compression
            w.Write((byte)0);  // filter
            w.Write((byte)0);  // interlace
        });

        // IDAT chunk with raw+deflate
        var raw = new List<byte>();
        for (int row = 0; row < height; row++)
        {
            raw.Add(0); // filter byte: None
            int offset = row * width * 4;
            raw.AddRange(rgba.Skip(offset).Take(width * 4));
        }

        var compressed = Deflate(raw.ToArray());
        WriteChunk(bw, "IDAT", w => w.Write(compressed));

        // IEND chunk
        WriteChunk(bw, "IEND", _ => { });

        bw.Flush();
    }

    private static void WriteChunk(BinaryWriter bw, string type, Action<BinaryWriter> writeData)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            writeData(w);
        }

        var data = ms.ToArray();
        bw.Write(BigEndian(data.Length));
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        bw.Write(typeBytes);
        bw.Write(data);

        var crcData = new byte[typeBytes.Length + data.Length];
        System.Buffer.BlockCopy(typeBytes, 0, crcData, 0, typeBytes.Length);
        System.Buffer.BlockCopy(data, 0, crcData, typeBytes.Length, data.Length);
        bw.Write(BigEndian((int)Crc32(crcData)));
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }
        return output.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static byte[] BigEndian(int v) =>
        [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
}
