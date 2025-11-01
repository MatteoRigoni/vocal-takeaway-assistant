using System.Collections.Generic;

namespace Takeaway.Api.VoiceDialog.IntentClassification;

public static class IntentLabels
{
    public const string StartOrder = "order.start";
    public const string AddItem = "order.add_item";
    public const string ModifyOrder = "order.modify";
    public const string CancelOrder = "order.cancel";
    public const string CheckStatus = "order.check_status";
    public const string CompleteOrder = "order.complete";
    public const string Affirm = "dialog.affirm";
    public const string Negate = "dialog.negate";
    public const string Greeting = "smalltalk.greeting";
    public const string Fallback = "fallback.unknown";

    public static readonly IReadOnlyList<string> All = new[]
    {
        StartOrder,
        AddItem,
        ModifyOrder,
        CancelOrder,
        CheckStatus,
        CompleteOrder,
        Affirm,
        Negate,
        Greeting,
        Fallback
    };
}
