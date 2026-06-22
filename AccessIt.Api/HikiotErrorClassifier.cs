public static class HikiotErrorClassifier
{
    private static readonly HashSet<int> RetryableCodes =
    [
        160099,
        160101,
        160102,
        160199
    ];

    public static bool IsRetryable(int code) => RetryableCodes.Contains(code);
}
