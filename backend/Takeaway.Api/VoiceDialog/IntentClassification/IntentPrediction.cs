namespace Takeaway.Api.VoiceDialog.IntentClassification;

public readonly record struct IntentPrediction(string? Label, double Confidence, bool IsEnabled, bool IsSuccessful)
{
    public static IntentPrediction Disabled { get; } = new(null, 0d, false, false);

    public bool HasPrediction => IsEnabled && IsSuccessful && !string.IsNullOrWhiteSpace(Label);
}
