using Kagami.Protocol;

namespace Kagami.Backends;

internal sealed record LocatorCandidate(
    string ControlType,
    string? AutomationId,
    string? Name,
    string? ClassName,
    object Value);

internal static class LocatorSegmentMatcher
{
    public static IReadOnlyList<LocatorCandidate> Filter(
        IReadOnlyList<LocatorCandidate> candidates,
        LocatorSegment segment)
    {
        var controlTypeMatches = candidates
            .Where(candidate => segment.ControlType is null ||
                EqualControlType(candidate.ControlType, segment.ControlType));

        if (!string.IsNullOrWhiteSpace(segment.AutomationId))
        {
            return controlTypeMatches
                .Where(candidate => Equal(candidate.AutomationId, segment.AutomationId))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(segment.Name))
        {
            return controlTypeMatches
                .Where(candidate => Equal(candidate.Name, segment.Name))
                .Where(candidate => segment.ClassName is null ||
                    Equal(candidate.ClassName, segment.ClassName))
                .ToList();
        }

        return controlTypeMatches
            .Where(candidate => segment.ClassName is null ||
                Equal(candidate.ClassName, segment.ClassName))
            .ToList();
    }

    public static LocatorSegment CreateSegment(
        IReadOnlyList<LocatorCandidate> candidates,
        LocatorCandidate target)
    {
        if (!string.IsNullOrWhiteSpace(target.AutomationId))
        {
            var automationIdSegment = NewSegment(target, target.AutomationId, target.Name);
            var automationIdMatches = Filter(candidates, automationIdSegment);
            if (automationIdMatches.Count == 1 && ReferenceEquals(automationIdMatches[0], target))
                return automationIdSegment;
        }

        if (!string.IsNullOrWhiteSpace(target.Name))
        {
            var nameSegment = NewSegment(target, automationId: null, name: target.Name);
            var nameMatches = Filter(candidates, nameSegment);
            if (nameMatches.Count == 1 && ReferenceEquals(nameMatches[0], target))
                return nameSegment;
        }

        var ordinalSegment = NewSegment(target, automationId: null, name: null);
        var ordinalCandidates = Filter(candidates, ordinalSegment);
        var ordinal = ordinalCandidates
            .Select((candidate, index) => (candidate, index))
            .Where(item => ReferenceEquals(item.candidate, target))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();

        if (ordinal < 0)
            throw new InvalidOperationException("Target is missing from its locator candidate set.");

        return new LocatorSegment
        {
            ControlType = ordinalSegment.ControlType,
            ClassName = ordinalSegment.ClassName,
            Ordinal = ordinal
        };
    }

    public static LocatorCandidate Select(
        IReadOnlyList<LocatorCandidate> candidates,
        LocatorSegment segment,
        int segmentIndex = 0)
    {
        var filtered = Filter(candidates, segment);
        var usesStableKey = !string.IsNullOrWhiteSpace(segment.AutomationId) ||
            !string.IsNullOrWhiteSpace(segment.Name);

        if (usesStableKey)
        {
            if (filtered.Count == 1)
                return filtered[0];

            var code = filtered.Count == 0
                ? ErrorCodes.LocatorNotFound
                : ErrorCodes.LocatorAmbiguous;
            var message = filtered.Count == 0
                ? $"Locator segment {segmentIndex} has no matching candidate."
                : $"{filtered.Count} candidates match locator segment {segmentIndex}.";

            throw new CommandException(
                code,
                message,
                details: CreateDetails(segmentIndex, segment, filtered));
        }

        if (segment.Ordinal >= 0 && segment.Ordinal < filtered.Count)
            return filtered[segment.Ordinal];

        throw new CommandException(
            ErrorCodes.LocatorNotFound,
            $"Locator segment {segmentIndex} has no candidate at ordinal {segment.Ordinal}.",
            details: CreateDetails(segmentIndex, segment, filtered));
    }

    private static LocatorSegment NewSegment(
        LocatorCandidate target,
        string? automationId,
        string? name) => new()
        {
            ControlType = target.ControlType,
            AutomationId = automationId,
            Name = name,
            ClassName = target.ClassName,
            Ordinal = 0
        };

    private static IReadOnlyDictionary<string, object?> CreateDetails(
        int segmentIndex,
        LocatorSegment segment,
        IReadOnlyList<LocatorCandidate> candidates)
    {
        IReadOnlyList<object> summaries = candidates
            .Select((candidate, ordinal) => (object)new Dictionary<string, object?>
            {
                ["ordinal"] = ordinal,
                ["control_type"] = candidate.ControlType,
                ["automation_id"] = candidate.AutomationId,
                ["name"] = candidate.Name,
                ["class_name"] = candidate.ClassName
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["segment_index"] = segmentIndex,
            ["segment"] = segment,
            ["candidate_count"] = candidates.Count,
            ["candidates"] = summaries
        };
    }

    private static bool Equal(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool EqualControlType(string actual, string expected) =>
        Equal(StripPrefix(actual), StripPrefix(expected));

    private static string StripPrefix(string value) =>
        value.StartsWith("ControlType.", StringComparison.OrdinalIgnoreCase)
            ? value["ControlType.".Length..]
            : value;
}
