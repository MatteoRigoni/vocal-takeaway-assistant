using System.ComponentModel.DataAnnotations;

namespace Takeaway.Api.Options;

public sealed class IntentClassifierOptions
{
    public const string SectionName = "IntentClassifier";

    /// <summary>
    /// Enables the ML-based intent classifier when set to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional path to a pre-trained ML.NET model (.zip). When provided and the file exists,
    /// the classifier loads it instead of training an in-memory baseline model.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Minimum probability required to expose a predicted intent to the dialog state machine.
    /// Values below this threshold are treated as "no prediction".
    /// </summary>
    [Range(0, 1)]
    public double MinimumConfidence { get; set; } = 0.25d;
}
