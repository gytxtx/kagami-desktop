using Kagami.Protocol;

namespace Kagami.Backends;

public interface IObservationGuardStore
{
    /// <summary>Save a guard file and return its path.</summary>
    Task<string> SaveAsync(ObservationGuard guard, CancellationToken ct);

    /// <summary>
    /// Load a guard from disk and validate it against the current state.
    /// A successful result includes the validated guard instance.
    /// </summary>
    Task<GuardValidationResult> LoadAndValidateAsync(string guardPath, CancellationToken ct);

    /// <summary>Clean up expired guards (older than the TTL).</summary>
    Task CleanupExpiredAsync(CancellationToken ct);
}
