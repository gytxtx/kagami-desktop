namespace Kagami.Utilities;

internal static class ProcessNameMatcher
{
    public static bool EqualsIgnoringExe(string expected, string actual) =>
        string.Equals(
            RemoveExeSuffix(expected),
            RemoveExeSuffix(actual),
            StringComparison.OrdinalIgnoreCase);

    private static string RemoveExeSuffix(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
}
