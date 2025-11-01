using System;

namespace Takeaway.Api.Extensions;

public static class DateTimeExtensions
{
    public static DateTime AsUtc(this DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };

    public static DateTime? AsUtc(this DateTime? value)
        => value?.AsUtc();
}
