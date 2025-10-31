namespace Takeaway.Api.Domain.Constants;

public static class OrderStatusCatalog
{
    public const string Received = "Received";
    public const string InPreparation = "InPreparation";
    public const string Ready = "Ready";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        [Received] = Received,
        ["Pending"] = Received,
        ["Queued"] = Received,
        ["InProgress"] = InPreparation,
        [InPreparation] = InPreparation,
        ["Preparing"] = InPreparation,
        ["Prep"] = InPreparation,
        [Ready] = Ready,
        [Completed] = Completed,
        ["Done"] = Completed,
        ["Finished"] = Completed,
        [Cancelled] = Cancelled,
        ["Canceled"] = Cancelled
    };

    public static IReadOnlyCollection<string> AllNames { get; } = new[]
    {
        Received,
        InPreparation,
        Ready,
        Completed,
        Cancelled
    };

    public static bool TryNormalize(string? status, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(status))
            return false;

        var sanitized = status.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (Synonyms.TryGetValue(sanitized, out normalized))
            return true;

        if (Synonyms.TryGetValue(status, out normalized))
            return true;

        normalized = string.Empty;
        return false;
    }
}
