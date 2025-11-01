using System;

namespace Takeaway.Api.VoiceDialog.Slots;

public static class SlotValidation
{
    public const int MaxQuantity = 50;

    public static bool IsValidQuantity(int quantity)
    {
        return quantity >= 1 && quantity <= MaxQuantity;
    }

    public static bool IsValidPickupTime(DateTimeOffset pickupTime, DateTimeOffset now, TimeSpan? minimumLeadTime = null)
    {
        var leadTime = minimumLeadTime ?? TimeSpan.FromMinutes(10);
        return pickupTime >= now.Add(leadTime);
    }
}
