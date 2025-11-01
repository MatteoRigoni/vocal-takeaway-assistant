namespace Takeaway.Api.Options;

public class SpeechServicesOptions
{
    public const string SectionName = "SpeechServices";

    public Uri? SpeechToTextBaseUrl { get; set; }

    public Uri? TextToSpeechBaseUrl { get; set; }
}
