namespace OpenFTTH.NotificationClient;

public class MaxReconnectRetriesReachedException : Exception
{
    public MaxReconnectRetriesReachedException()
    {
    }

    public MaxReconnectRetriesReachedException(string? message) : base(message)
    {
    }

    public MaxReconnectRetriesReachedException(
        string? message,
        Exception? innerException) : base(message, innerException)
    {
    }
}
