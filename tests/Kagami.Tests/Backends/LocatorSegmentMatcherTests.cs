using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class LocatorSegmentMatcherTests
{
    [Fact]
    public void Select_EmptyName_UsesClassAndOrdinal()
    {
        var candidates = new[]
        {
            Candidate("Group", null, "", "Panel", "first-panel"),
            Candidate("Group", null, "", "Grid", "grid"),
            Candidate("Group", null, "", "Panel", "second-panel")
        };
        var segment = new LocatorSegment
        {
            ControlType = "Group",
            Name = "",
            ClassName = "Panel",
            Ordinal = 1
        };

        var selected = LocatorSegmentMatcher.Select(candidates, segment);

        Assert.Same(candidates[2], selected);
    }

    [Fact]
    public void Select_UniqueAutomationId_TakesPriorityOverNameAndClass()
    {
        var candidates = new[]
        {
            Candidate("Button", "settings", "Different", "OtherClass", "automation-id"),
            Candidate("Button", "other", "Settings", "Button", "name-and-class")
        };
        var segment = new LocatorSegment
        {
            ControlType = "Button",
            AutomationId = "settings",
            Name = "Settings",
            ClassName = "Button"
        };

        var selected = LocatorSegmentMatcher.Select(candidates, segment);

        Assert.Same(candidates[0], selected);
    }

    [Fact]
    public void CreateSegment_EmptyName_UsesSameClassCandidateSetForOrdinal()
    {
        var candidates = new[]
        {
            Candidate("Group", "", "", "Panel", "first-panel"),
            Candidate("Group", "", "", "Grid", "grid"),
            Candidate("Group", "", "", "Panel", "second-panel")
        };

        var segment = LocatorSegmentMatcher.CreateSegment(candidates, candidates[2]);

        Assert.Null(segment.AutomationId);
        Assert.Null(segment.Name);
        Assert.Equal("Panel", segment.ClassName);
        Assert.Equal(1, segment.Ordinal);
        Assert.Same(candidates[2], LocatorSegmentMatcher.Select(candidates, segment));
    }

    [Fact]
    public void Select_NonEmptyName_RequiresExactNameAndClass()
    {
        var candidates = new[]
        {
            Candidate("Button", null, "Settings advanced", "Button", "partial-name"),
            Candidate("Button", null, "Settings", "Link", "wrong-class"),
            Candidate("Button", null, "Settings", "Button", "exact")
        };
        var segment = new LocatorSegment
        {
            ControlType = "Button",
            Name = "Settings",
            ClassName = "Button"
        };

        var selected = LocatorSegmentMatcher.Select(candidates, segment);

        Assert.Same(candidates[2], selected);
    }

    [Fact]
    public void Select_AmbiguousStableKey_ReportsStructuredDetails()
    {
        var candidates = new[]
        {
            Candidate("Button", "duplicate", "One", "Button", 1),
            Candidate("Button", "duplicate", "Two", "Button", 2)
        };
        var segment = new LocatorSegment
        {
            ControlType = "Button",
            AutomationId = "duplicate",
            Ordinal = 0
        };

        var exception = Assert.Throws<CommandException>(() =>
            LocatorSegmentMatcher.Select(candidates, segment, segmentIndex: 3));

        Assert.Equal(ErrorCodes.LocatorAmbiguous, exception.ErrorCode);
        Assert.Equal(3, exception.Details["segment_index"]);
        Assert.Same(segment, exception.Details["segment"]);
        Assert.Equal(2, exception.Details["candidate_count"]);
        Assert.IsAssignableFrom<IReadOnlyList<object>>(exception.Details["candidates"]);
    }

    [Fact]
    public void Select_ClassOrdinalOutOfRange_ReportsStructuredDetails()
    {
        var candidates = new[]
        {
            Candidate("Group", null, "", "Panel", 1),
            Candidate("Group", null, "", "Panel", 2)
        };
        var segment = new LocatorSegment
        {
            ControlType = "Group",
            Name = "",
            ClassName = "Panel",
            Ordinal = 2
        };

        var exception = Assert.Throws<CommandException>(() =>
            LocatorSegmentMatcher.Select(candidates, segment, segmentIndex: 4));

        Assert.Equal(ErrorCodes.LocatorNotFound, exception.ErrorCode);
        Assert.Equal(4, exception.Details["segment_index"]);
        Assert.Same(segment, exception.Details["segment"]);
        Assert.Equal(2, exception.Details["candidate_count"]);
        Assert.IsAssignableFrom<IReadOnlyList<object>>(exception.Details["candidates"]);
    }

    private static LocatorCandidate Candidate(
        string controlType,
        string? automationId,
        string? name,
        string? className,
        object value) =>
        new(controlType, automationId, name, className, value);
}
