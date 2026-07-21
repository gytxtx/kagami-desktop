using Kagami.Protocol;

namespace Kagami.Tests.Protocol;

public class ErrorCodesTests
{
    [Fact]
    public void AllErrorCodes_AreNonEmpty_AndUnique()
    {
        var codes = new[]
        {
            ErrorCodes.WindowNotFound,
            ErrorCodes.WindowDestroyed,
            ErrorCodes.WindowMinimized,
            ErrorCodes.LocatorNotFound,
            ErrorCodes.LocatorAmbiguous,
            ErrorCodes.LocatorStale,
            ErrorCodes.LocatorRootChanged,
            ErrorCodes.StaleObservation,
            ErrorCodes.InputInjectionFailed,
            ErrorCodes.TargetHigherIntegrity,
            ErrorCodes.ForegroundActivationDenied,
            ErrorCodes.CaptureBackendUnavailable,
            ErrorCodes.CaptureFailed,
            ErrorCodes.UiaProviderUnresponsive,
            ErrorCodes.OperationTimeout,
            ErrorCodes.ElementNotAvailable,
            ErrorCodes.PatternNotSupported,
            ErrorCodes.MutexLocked,
            ErrorCodes.InvalidArgument,
            ErrorCodes.InternalError
        };

        Assert.All(codes, c => Assert.False(string.IsNullOrWhiteSpace(c), $"Error code must not be empty"));

        var distinct = codes.Distinct().Count();
        Assert.Equal(codes.Length, distinct);
    }
}
