namespace Takeaway.Api.VoiceDialog.IntentClassification;

public interface IIntentClassifier
{
    IntentPrediction PredictIntent(string? utterance);
}
