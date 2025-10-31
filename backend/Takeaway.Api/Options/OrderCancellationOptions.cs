namespace Takeaway.Api.Options;

public class OrderCancellationOptions
{
    public const string SectionName = "OrderCancellation";

    /// <summary>
    /// Gets or sets the number of minutes before the scheduled pickup time when cancellations are no longer allowed.
    /// </summary>
    public int CancellationWindowMinutes { get; set; } = 10;
}
