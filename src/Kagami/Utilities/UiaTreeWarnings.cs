using Kagami.Protocol;

namespace Kagami.Utilities;

public static class UiaTreeWarnings
{
    private const double AmbiguousOverlapThreshold = 0.8;

    public static JsonWarning? ForEmptyRoot(TreeNode? tree)
    {
        if (tree is null || tree.Children.Count > 0)
            return null;

        return new JsonWarning
        {
            Code = "uia_tree_empty",
            Message = "UI Automation returned only the window root and exposed no child controls. Try another UIA view or use physical interaction."
        };
    }

    public static JsonWarning? ForAmbiguousVisibility(TreeNode? tree)
    {
        if (tree is null)
            return null;

        var visibleCustomRects = tree.Children
            .Where(child =>
                child.ControlType == "Custom" &&
                !child.IsOffscreen &&
                HasPositiveArea(child.Rect))
            .Select(child => child.Rect!)
            .ToList();

        for (var firstIndex = 0; firstIndex < visibleCustomRects.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < visibleCustomRects.Count; secondIndex++)
            {
                if (OverlapRatioOfSmaller(
                        visibleCustomRects[firstIndex],
                        visibleCustomRects[secondIndex]) >= AmbiguousOverlapThreshold)
                {
                    return new JsonWarning
                    {
                        Code = "uia_visibility_ambiguous",
                        Message = "UI Automation reported multiple overlapping top-level Custom surfaces with is_offscreen=false, but UIA is_offscreen does not guarantee visual visibility. Use a current screenshot and state properties to confirm what is actually visible before acting."
                    };
                }
            }
        }

        return null;
    }

    private static bool HasPositiveArea(DetailedRect? rect) =>
        rect is not null && rect.Right > rect.Left && rect.Bottom > rect.Top;

    private static double OverlapRatioOfSmaller(DetailedRect first, DetailedRect second)
    {
        var intersectionWidth = Math.Max(
            0d,
            Math.Min((double)first.Right, second.Right) - Math.Max((double)first.Left, second.Left));
        var intersectionHeight = Math.Max(
            0d,
            Math.Min((double)first.Bottom, second.Bottom) - Math.Max((double)first.Top, second.Top));
        var intersectionArea = intersectionWidth * intersectionHeight;
        var firstArea = ((double)first.Right - first.Left) * ((double)first.Bottom - first.Top);
        var secondArea = ((double)second.Right - second.Left) * ((double)second.Bottom - second.Top);

        return intersectionArea / Math.Min(firstArea, secondArea);
    }
}
