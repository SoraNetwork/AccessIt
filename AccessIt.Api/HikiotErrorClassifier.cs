namespace AccessIt.Api.Hikiot;

/// <summary>
/// Classifies HIKIoT API error codes as retryable (transient) or terminal (require human intervention).
/// Codes sourced from HIKIoT Open Platform documentation and observed production responses.
/// </summary>
public static class HikiotErrorClassifier
{
    /// <summary>Device or gateway temporarily unreachable / busy — safe to retry.</summary>
    private static readonly HashSet<int> RetryableCodes =
    [
        // Generic device transient errors
        160099, // device temporarily unavailable
        160101, // device busy
        160102, // device command timeout
        160199, // unknown transient error

        // Device offline / network
        160200, // device offline
        160201, // device unreachable

        // Token / auth transient (gateway will refresh, subsequent attempt should succeed)
        401,    // HTTP Unauthorized — token may have just expired; gateway refreshes automatically

        // HIKIoT platform throttle / overload
        10002,  // request rate limit
        10003,  // service temporarily unavailable
        10004,  // gateway timeout

        // Issuance engine transient
        120500, // issuance engine busy
        120501, // previous issue not yet completed (retry after delay)
        120502, // batch processing, try again
    ];

    /// <summary>
    /// Terminal codes where retrying will never succeed without operator intervention.
    /// Kept here for documentation; callers check via <see cref="IsRetryable"/>.
    /// </summary>
    private static readonly HashSet<int> TerminalCodes =
    [
        160103, // person or device not found
        160104, // card number conflict
        160105, // employee number conflict
        160106, // capacity exceeded (person/card/face)
        160107, // credential not supported by device
        160108, // face score too low
        160109, // password too short or unsupported
        160110, // duplicate password on device
        120524, // no changes to issue (not an error — treat as success in callers)
    ];

    public static bool IsRetryable(int code) => RetryableCodes.Contains(code);

    /// <summary>
    /// Returns true if the code represents "nothing to issue" — callers should treat this as success.
    /// </summary>
    public static bool IsAlreadyUpToDate(int code) => code == 120524;

    /// <summary>
    /// Returns true if the error is due to insufficient device capacity (person/card/face slots full).
    /// These must be surfaced to operators immediately without retry.
    /// </summary>
    public static bool IsCapacityError(int code) => code == 160106;

    /// <summary>
    /// Returns true if the error is an unsupported credential type on the target device.
    /// Indicates the device capability flags were not checked before issuing.
    /// </summary>
    public static bool IsUnsupportedCredential(int code) => code == 160107;

    /// <summary>
    /// Returns true if the face image was rejected by HIKIoT's quality evaluation.
    /// The image must be replaced before retrying.
    /// </summary>
    public static bool IsFaceScoreFailure(int code) => code == 160108;

    /// <summary>
    /// Returns true if the password failed device constraints (too short, too long, duplicate, unsupported).
    /// The password must be changed before retrying.
    /// </summary>
    public static bool IsPasswordError(int code) => code is 160109 or 160110;
}
