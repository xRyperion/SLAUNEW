using MessagePack;

namespace DistributedSLAU.Common;

/// <summary>
/// Типы сообщений для распределённого решения СЛАУ
/// </summary>
public enum MessageType
{
    /// <summary>
    /// Координатор -> Воркер: распределение блока матрицы
    /// </summary>
    TaskDistribution,

    /// <summary>
    /// Воркер -> Координатор: результат обработки блока
    /// </summary>
    PartialResult,

    /// <summary>
    /// Воркер -> Координатор: подтверждение готовности
    /// </summary>
    Ready,

    /// <summary>
    /// Координатор -> Воркер: команда начать этап
    /// </summary>
    StartPhase,

    /// <summary>
    /// Воркер <-> Воркер: обмен данными между этапами
    /// </summary>
    ExchangeData,

    /// <summary>
    /// Координатор -> Воркер: завершение работы
    /// </summary>
    Shutdown,

    /// <summary>
    /// Проверка доступности узла
    /// </summary>
    Heartbeat,

    /// <summary>
    /// Воркер -> Координатор: завершение этапа
    /// </summary>
    PhaseComplete,

    /// <summary>
    /// Координатор -> Воркер: передача модифицированного блока
    /// </summary>
    UpdateBlock,

    /// <summary>
    /// Воркер -> Координатор: запрос данных следующего этапа
    /// </summary>
    RequestNextPhase,

    /// <summary>
    /// Координатор -> Воркер: распределение клетки матрицы (2D блок)
    /// </summary>
    BlockDistribution,

    /// <summary>
    /// Воркер -> Координатор: результат обработки клетки
    /// </summary>
    BlockResult,

    /// <summary>
    /// Воркер -> Координатор: ошибка обработки
    /// </summary>
    BlockError
}

/// <summary>
/// Сетевое сообщение для обмена между узлами
/// </summary>
[MessagePackObject]
public class NetworkMessage
{
    [Key(0)]
    public MessageType Type { get; set; }

    [Key(1)]
    public byte[]? Data { get; set; }

    [Key(2)]
    public DateTime Timestamp { get; set; }

    [Key(3)]
    public int BlockRowIndex { get; set; }

    [Key(4)]
    public int BlockColIndex { get; set; }

    [Key(5)]
    public int Phase { get; set; }

    [Key(6)]
    public string SenderId { get; set; } = string.Empty;

    [Key(7)]
    public int TotalPhases { get; set; }

    [Key(8)]
    public int GridSize { get; set; }

    public NetworkMessage()
    {
        Timestamp = DateTime.UtcNow;
    }

    public NetworkMessage(MessageType type, byte[]? data) : this()
    {
        Type = type;
        Data = data;
    }

    public NetworkMessage(MessageType type, byte[]? data, int blockRow, int blockCol) : this(type, data)
    {
        BlockRowIndex = blockRow;
        BlockColIndex = blockCol;
    }
}
