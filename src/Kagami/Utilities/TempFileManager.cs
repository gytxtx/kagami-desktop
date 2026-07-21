namespace Kagami.Utilities;

internal static class TempFileManager
{
    private static readonly TimeSpan FileTtl = TimeSpan.FromMinutes(5);

    public static string BaseDir => Path.Combine(Path.GetTempPath(), "kagami");

    public static string GetScreenshotPath(string? outputPath = null)
    {
        if (outputPath is not null)
            return Path.GetFullPath(outputPath);

        var dir = Path.Combine(BaseDir, "screenshots");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid()}.png");
    }

    public static string GetGuardPath()
    {
        var dir = GetGuardDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"guard-{Guid.NewGuid()}.json");
    }

    public static string GetGuardDirectory()
    {
        return Path.Combine(BaseDir, "guards");
    }

    /// <summary>
    /// Clean up screenshots and guards older than 5 minutes.
    /// Failures are silently ignored — cleanup is best-effort.
    /// </summary>
    public static void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - FileTtl;

        CleanDirectory(Path.Combine(BaseDir, "screenshots"), cutoff);
        CleanDirectory(Path.Combine(BaseDir, "guards"), cutoff);
    }

    private static void CleanDirectory(string dir, DateTime cutoff)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    if (File.GetCreationTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch
        {
            // best-effort
        }
    }
}
