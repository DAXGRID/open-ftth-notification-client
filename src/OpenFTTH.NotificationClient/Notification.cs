using Newtonsoft.Json;

namespace OpenFTTH.NotificationClient;

public sealed record Notification
{
    [JsonProperty("type")]
    public string Type { get; init; }

    [JsonProperty("body")]
    public string Body { get; init; }

    [JsonConstructor]
    public Notification(string type, string body)
    {
        Type = type;
        Body = body;
    }
}
