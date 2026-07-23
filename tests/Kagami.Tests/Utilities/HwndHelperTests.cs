using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class HwndHelperTests
{
    [Fact]
    public void ParseExisting_WithMalformedValue_ThrowsInvalidArgument()
    {
        var exception = Assert.Throws<CommandException>(() => HwndHelper.ParseExisting("not-a-handle"));

        Assert.Equal(ErrorCodes.InvalidArgument, exception.ErrorCode);
        Assert.Equal(1, exception.ExitCode);
        Assert.NotEmpty(exception.Message);
    }

    [Fact]
    public void ParseExisting_WithMissingWindow_ThrowsWindowDestroyed()
    {
        var exception = Assert.Throws<CommandException>(() => HwndHelper.ParseExisting("0xDEADBEEF"));

        Assert.Equal(ErrorCodes.WindowDestroyed, exception.ErrorCode);
        Assert.Equal(1, exception.ExitCode);
        Assert.Contains("0xDEADBEEF", exception.Message);
    }
}
