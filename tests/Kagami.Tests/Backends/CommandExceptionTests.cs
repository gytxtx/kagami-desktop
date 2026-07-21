using Kagami.Backends;

namespace Kagami.Tests.Backends;

public class CommandExceptionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var ex = new CommandException("TEST_CODE", "Test message", retryable: true, nativeCode: 42, exitCode: 3);

        Assert.Equal("TEST_CODE", ex.ErrorCode);
        Assert.Equal("Test message", ex.Message);
        Assert.True(ex.Retryable);
        Assert.Equal(42, ex.NativeCode);
        Assert.Equal(3, ex.ExitCode);
    }

    [Fact]
    public void Constructor_DefaultExitCode_ReturnsOne()
    {
        var ex = new CommandException("A", "B");
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void Constructor_DefaultRetryable_ReturnsFalse()
    {
        var ex = new CommandException("A", "B");
        Assert.False(ex.Retryable);
    }

    [Fact]
    public void Exception_IsCatchable()
    {
        try
        {
            throw new CommandException("CATCH_ME", "test");
        }
        catch (CommandException ex)
        {
            Assert.Equal("CATCH_ME", ex.ErrorCode);
        }
    }
}
