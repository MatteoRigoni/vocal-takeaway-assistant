using System;

namespace Takeaway.Api.VoiceDialog;

public sealed class VoiceOrderProcessingException : Exception
{
    public VoiceOrderProcessingException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
