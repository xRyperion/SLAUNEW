namespace DistributedSLAU.Common;

/// <summary>
/// Информация о вычислительном узле
/// </summary>
public class WorkerNode
{
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public string Id => $"{IpAddress}:{Port}";
    public bool IsAlive { get; set; } = true;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    public WorkerNode(string ipAddress, int port)
    {
        IpAddress = ipAddress;
        Port = port;
    }

    public override string ToString() => Id;
}
