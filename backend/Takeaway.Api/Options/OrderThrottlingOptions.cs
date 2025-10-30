namespace Takeaway.Api.Options;

public class OrderThrottlingOptions
{
    public const string SectionName = "OrderThrottling";
    public int MaxOrdersPerSlot { get; set; } = 20;
}
