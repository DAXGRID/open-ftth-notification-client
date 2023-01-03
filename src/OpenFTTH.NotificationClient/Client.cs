using NetCoreServer;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace OpenFTTH.NotificationClient;

internal sealed class NotificationWsClient : WsClient
{
    private const int MAX_RETRIES = 10;
    private const int SLEEP_INTERVAL_MS = 250;
    private readonly Action<string> _onMessageReceivedCallback;
    private int retries;
    private bool _stop;

    public NotificationWsClient(
        IPAddress address,
        int port,
        Action<string> onMessageReceivedCallback) : base(address, port)
    {
        _onMessageReceivedCallback = onMessageReceivedCallback;
    }

    public void DisconnectAndStop()
    {
        _stop = true;

        // The WebSocket protocol defines a status code of 1000
        // to indicate that a normal closure,
        // meaning that the connection is being closed in a normal way.
        // This is a normal operation that occurs when a WebSocket connection is
        // closed gracefully, without any errors.
        CloseAsync(1000);

        // We wait for it to disconnect, and keep yielding the thread until
        // the connection is closed.
        while (IsConnected)
        {
            Thread.Yield();
        }
    }

    public override void OnWsConnecting(HttpRequest request)
    {
        // The endpoint will always be the '/', so we do not care about
        // making it configurable.
        request.SetBegin("GET", "/");
        request.SetHeader("Upgrade", "websocket");
        request.SetHeader("Connection", "Upgrade");
        request.SetHeader("Sec-WebSocket-Key", Convert.ToBase64String(WsNonce));
        request.SetHeader("Sec-WebSocket-Version", "13");
        request.SetBody();
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();

        // Wait for a while... Since we don't want to reconnect straight away.
        Thread.Sleep(1000);

        // Try to connect again
        if (!_stop)
        {
            if (retries == MAX_RETRIES)
            {
                throw new MaxReconnectRetriesReachedException(
                    $"Reached the max reconnection retries of '{MAX_RETRIES}'.");
            }

            if (ConnectAsync())
            {
                retries = 0;
            }
            else
            {
                retries++;
            }

            Thread.Sleep(SLEEP_INTERVAL_MS * retries);
        }
    }

    public override void OnWsReceived(byte[] buffer, long offset, long size)
    {
        var message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
        _onMessageReceivedCallback(message);
    }
}

public sealed class Client : IDisposable
{
    private readonly NotificationWsClient _notificationTcpClient;
    private readonly Channel<Notification> _channel;
    private readonly bool _writeOnly;

    private bool _disposed;

    public Client(IPAddress ipAddress, int port)
    {
        _notificationTcpClient = new(ipAddress, port, WriteNotificationToChannel);
        _channel = Channel.CreateUnbounded<Notification>();
    }

    public Client(IPAddress ipAddress, int port, bool writeOnly)
    {
        _notificationTcpClient = new(ipAddress, port, WriteNotificationToChannel);
        _channel = Channel.CreateUnbounded<Notification>();
        _writeOnly = writeOnly;
    }

    public ChannelReader<Notification> Connect()
    {
        if (_disposed)
        {
            throw new InvalidOperationException(
                "Cannot connect to a server that has already been disposed.");
        }

        if (_notificationTcpClient.IsConnected)
        {
            throw new InvalidOperationException(
                "The server is already connected.");
        }

        _notificationTcpClient.ConnectAsync();

        return _channel.Reader;
    }

    public void Send(Notification notification)
    {
        _notificationTcpClient.SendText(
            JsonConvert.SerializeObject(notification));
    }

    public void Dispose()
    {
        // No reason to dispose agian, when the resource has already been disposed.
        if (_disposed)
        {
            return;
        }

        if (_notificationTcpClient.IsConnected)
        {
            _notificationTcpClient.DisconnectAsync();
        }
        if (!_notificationTcpClient.IsDisposed)
        {
            _notificationTcpClient.Dispose();
        }

        try
        {
            _channel.Writer.Complete();
        }
        catch (InvalidOperationException)
        {
            // Do nothing, this can be because of channel has already been closed.
        }

        _disposed = true;
    }

    private void WriteNotificationToChannel(string s)
    {
        if (_writeOnly)
        {
            // When we are in write only mode, we only care about sending messages,
            // so there is no reason to spend time deserialize messages.
            return;
        }

        var notification = JsonConvert.DeserializeObject<Notification>(s);

        if (notification is null)
        {
            throw new InvalidOperationException(
                "Could not deserialize {nameof(Notification)}.");
        }

        while (true)
        {
            if (_channel.Writer.TryWrite(notification))
            {
                break;
            }

            Thread.Yield();
        }
    }
}
