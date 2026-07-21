namespace Kagami.Protocol;

public static class ErrorCodes
{
    // Window errors
    public const string WindowNotFound = "WINDOW_NOT_FOUND";
    public const string WindowDestroyed = "WINDOW_DESTROYED";
    public const string WindowMinimized = "WINDOW_MINIMIZED";

    // Locator errors
    public const string LocatorNotFound = "LOCATOR_NOT_FOUND";
    public const string LocatorAmbiguous = "LOCATOR_AMBIGUOUS";
    public const string LocatorStale = "LOCATOR_STALE";
    public const string LocatorRootChanged = "LOCATOR_ROOT_CHANGED";

    // State / observation errors
    public const string StaleObservation = "STALE_OBSERVATION";

    // Input errors
    public const string InputInjectionFailed = "INPUT_INJECTION_FAILED";
    public const string TargetHigherIntegrity = "TARGET_HIGHER_INTEGRITY";
    public const string ForegroundActivationDenied = "FOREGROUND_ACTIVATION_DENIED";

    // Capture errors
    public const string CaptureBackendUnavailable = "CAPTURE_BACKEND_UNAVAILABLE";
    public const string CaptureFailed = "CAPTURE_FAILED";

    // UIA errors
    public const string UiaProviderUnresponsive = "UIA_PROVIDER_UNRESPONSIVE";
    public const string OperationTimeout = "OPERATION_TIMEOUT";
    public const string ElementNotAvailable = "ELEMENT_NOT_AVAILABLE";
    public const string PatternNotSupported = "PATTERN_NOT_SUPPORTED";

    // General
    public const string MutexLocked = "MUTEX_LOCKED";
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string InternalError = "INTERNAL_ERROR";
}
