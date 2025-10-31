namespace Takeaway.Api.Services;

public interface IOrderCodeGenerator
{
    string Generate(DateTime timestampUtc, int orderId);
}

public sealed class OrderCodeGenerator : IOrderCodeGenerator
{
    public string Generate(DateTime timestampUtc, int orderId)
    {
        return $"ORD-{timestampUtc:yyyyMMddHHmm}-{orderId:D6}";
    }
}
