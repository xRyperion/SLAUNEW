using System.Net;
using System.Net.Sockets;
using MessagePack;

namespace DistributedSLAU.Common;

/// <summary>
/// Базовый класс для UDP-коммуникации между узлами
/// </summary>
public abstract class UdpCommunicator : IDisposable
{
    protected UdpClient udpClient;
    protected CancellationTokenSource cts;
    protected Task? listenerTask;
    protected readonly int port;
    protected readonly IPEndPoint listenEndPoint;

    public event Func<NetworkMessage, IPEndPoint, Task>? MessageReceived;

    protected UdpCommunicator(int localPort)
    {
        port = localPort;
        listenEndPoint = new IPEndPoint(IPAddress.Any, localPort);

        udpClient = new UdpClient();
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(listenEndPoint);

        cts = new CancellationTokenSource();

        Console.WriteLine($"[UDP] Слушаем на порту {localPort}");
    }

    public void StartListening()
    {
        listenerTask = Task.Run(ListenLoop);
    }

    private async Task ListenLoop()
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                _ = Task.Run(() => ProcessMessage(result.Buffer, result.RemoteEndPoint));
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP] Ошибка приема: {ex.Message}");
            }
        }
    }

    private async Task ProcessMessage(byte[] data, IPEndPoint remoteEndPoint)
    {
        try
        {
            var message = MessagePackSerializer.Deserialize<NetworkMessage>(data);

            if (MessageReceived != null)
            {
                await MessageReceived.Invoke(message, remoteEndPoint);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP] Ошибка десериализации: {ex.Message}");
        }
    }

    public async Task SendToAsync(NetworkMessage message, string ipAddress, int port)
    {
        try
        {
            var data = MessagePackSerializer.Serialize(message);
            var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
            await udpClient.SendAsync(data, data.Length, endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP] Ошибка отправки: {ex.Message}");
        }
    }

    public async Task BroadcastAsync(NetworkMessage message, int port)
    {
        try
        {
            var data = MessagePackSerializer.Serialize(message);
            udpClient.EnableBroadcast = true;
            var endpoint = new IPEndPoint(IPAddress.Broadcast, port);
            await udpClient.SendAsync(data, data.Length, endpoint);
            udpClient.EnableBroadcast = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP] Ошибка широковещательной отправки: {ex.Message}");
        }
    }

    public virtual void Stop()
    {
        cts.Cancel();
        udpClient?.Close();
        udpClient?.Dispose();
        listenerTask?.Wait(1000);
    }

    public void Dispose()
    {
        Stop();
        udpClient?.Dispose();
    }
}
