using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class ConcurrencyGuardTests
{
    [Fact]
    public void TryAcquire_FirstCall_ReturnsTrue()
    {
        using var guard = new ConcurrencyGuard();
        Assert.True(guard.TryAcquire(0));
    }

    [Fact]
    public void Release_DoesNotThrow()
    {
        using var guard = new ConcurrencyGuard();
        guard.TryAcquire(0);
        // Should not throw
        var ex = Record.Exception(() => guard.Release());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_WithoutAcquire_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            using var guard = new ConcurrencyGuard();
            // Dispose without calling TryAcquire
        });
        Assert.Null(ex);
    }
}
