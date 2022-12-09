using NetCoreServer;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace OpenFTTH.NotificationClient;

internal class NotificationTcpClient : WsClient
{
    private readonly Action<string> _onMessageReceivedCallback;
    private bool _stop;

    public NotificationTcpClient(
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
            ConnectAsync();
    }

    public override void OnWsReceived(byte[] buffer, long offset, long size)
    {
        var message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
        _onMessageReceivedCallback(message);
    }
}

public class Client
{
    private readonly NotificationTcpClient _notificationTcpClient;
    private readonly Channel<Notification> _channel;

    public Client(IPAddress ipAddress, int port)
    {
        _notificationTcpClient = new(ipAddress, port, WriteNotificationToChannel);
        _channel = Channel.CreateUnbounded<Notification>();
    }

    public ChannelReader<Notification> Connect()
    {
        _notificationTcpClient.ConnectAsync();
        return _channel.Reader;
    }

    public void Disconnect()
    {
        _notificationTcpClient.DisconnectAsync();
        _channel.Writer.Complete();
    }

    public void Send(Notification notification)
    {
        _notificationTcpClient.SendText(
            JsonConvert.SerializeObject(notification));
    }

    private void WriteNotificationToChannel(string s)
    {
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
