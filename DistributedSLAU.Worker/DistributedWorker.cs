using System.Net;
using System.Net.Sockets;
using MessagePack;
using DistributedSLAU.Common;

namespace DistributedSLAU.Worker;

/// <summary>
/// Вычислительный узел для распределённого решения СЛАУ с клеточным разбиением
/// </summary>
public class DistributedWorker : UdpCommunicator
{
    private readonly string workerId;
    private readonly int listenPort;
    private LinearSystem? fullSystem;
    private MatrixBlock? currentBlock;
    private int startRow;
    private int rowCount;
    private string? coordinatorIp;
    private int coordinatorPort;
    private int currentPhase = 0;

    public DistributedWorker(int port) : base(port)
    {
        listenPort = port;
        workerId = $"{GetLocalIPAddress()}:{port}";
        MessageReceived += OnMessageReceived;
        Console.WriteLine($"[Worker {workerId}] Запущен и готов к работе");
        Console.WriteLine($"[Worker {workerId}] Порт: {port}");
    }

    private async Task OnMessageReceived(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        Console.WriteLine($"[Worker {workerId}] Получено сообщение: {message.Type} (Phase={message.Phase})");

        switch (message.Type)
        {
            case MessageType.TaskDistribution:
                await HandleTaskDistribution(message, remoteEndPoint);
                break;

            case MessageType.BlockDistribution:
                await HandleBlockDistribution(message, remoteEndPoint);
                break;

            case MessageType.ExchangeData:
                await HandleExchangeData(message, remoteEndPoint);
                break;

            case MessageType.Heartbeat:
                await SendToAsync(new NetworkMessage(MessageType.Heartbeat, null),
                    remoteEndPoint.Address.ToString(), remoteEndPoint.Port);
                break;

            case MessageType.Shutdown:
                Console.WriteLine($"[Worker {workerId}] Получена команда завершения");
                Stop();
                Environment.Exit(0);
                break;
        }
    }

    /// <summary>
    /// Обработка разбиения по строкам (старый режим)
    /// </summary>
    private async Task HandleTaskDistribution(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        if (message.Data == null)
        {
            Console.WriteLine($"[Worker {workerId}] Пустые данные!");
            return;
        }

        try
        {
            fullSystem = MessagePackSerializer.Deserialize<LinearSystem>(message.Data);
            startRow = message.BlockRowIndex;
            rowCount = message.BlockColIndex;
            coordinatorIp = remoteEndPoint.Address.ToString();
            coordinatorPort = remoteEndPoint.Port;

            Console.WriteLine($"[Worker {workerId}] Получена система {fullSystem.Size}x{fullSystem.Size}, " +
                             $"строки {startRow}-{startRow + rowCount - 1}");

            // Отправляем Ready
            var readyMessage = new NetworkMessage(MessageType.Ready, null)
            {
                BlockRowIndex = message.BlockRowIndex
            };
            await SendToAsync(readyMessage, coordinatorIp, coordinatorPort);

            // Решаем систему
            var fullSolution = CellGaussSolver.SolveSequential(fullSystem);

            // Берём свою часть
            var localSolution = new double[rowCount];
            for (int i = 0; i < rowCount && startRow + i < fullSolution.Length; i++)
            {
                localSolution[i] = fullSolution[startRow + i];
            }

            Console.WriteLine($"[Worker {workerId}] Решение: [{string.Join(", ", localSolution.Select(x => x.ToString("F6")))}]");

            // Отправляем результат
            var resultData = MessagePackSerializer.Serialize(localSolution);
            var resultMessage = new NetworkMessage(MessageType.PartialResult, resultData)
            {
                BlockRowIndex = message.BlockRowIndex
            };

            await SendToAsync(resultMessage, coordinatorIp, coordinatorPort);
            Console.WriteLine($"[Worker {workerId}] Результат отправлен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker {workerId}] Ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Обработка клеточного (2D) разбиения
    /// </summary>
    private async Task HandleBlockDistribution(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        if (message.Data == null)
        {
            Console.WriteLine($"[Worker {workerId}] Пустые данные блока!");
            return;
        }

        try
        {
            currentBlock = MessagePackSerializer.Deserialize<MatrixBlock>(message.Data);
            coordinatorIp = remoteEndPoint.Address.ToString();
            coordinatorPort = remoteEndPoint.Port;
            currentPhase = message.Phase;

            Console.WriteLine($"[Worker {workerId}] Получена клетка ({message.BlockRowIndex},{message.BlockColIndex}) " +
                            $"размером {currentBlock.RowCount}x{currentBlock.ColCount}");

            // Этап 1: Прямой ход для клетки
            if (message.Phase == 1)
            {
                Console.WriteLine($"[Worker {workerId}] Выполнение прямого хода для клетки...");
                CellGaussSolver.BlockForwardElimination(currentBlock);

                Console.WriteLine($"[Worker {workerId}] Клетка обработана, отправляю результат...");

                // Отправляем обработанный блок — ОДИН раз сериализуем!
                var blockData = MessagePackSerializer.Serialize(currentBlock);
                var resultMessage = new NetworkMessage(MessageType.BlockResult, blockData)
                {
                    BlockRowIndex = message.BlockRowIndex,
                    BlockColIndex = message.BlockColIndex,
                    GridSize = message.GridSize,
                    Phase = 1
                };

                await SendToAsync(resultMessage, coordinatorIp, coordinatorPort);
                Console.WriteLine($"[Worker {workerId}] Обработанная клетка отправлена");

                // Отправляем подтверждение завершения этапа
                var phaseComplete = new NetworkMessage(MessageType.PhaseComplete, null)
                {
                    Phase = 1
                };
                await SendToAsync(phaseComplete, coordinatorIp, coordinatorPort);
            }

            // Этап 2: Обмен данными (получение ведущих строк)
            else if (message.Phase == 2)
            {
                Console.WriteLine($"[Worker {workerId}] Получение данных для обмена...");
                var phaseComplete = new NetworkMessage(MessageType.PhaseComplete, null)
                {
                    Phase = 2
                };
                await SendToAsync(phaseComplete, coordinatorIp, coordinatorPort);
            }

            // Этап 3: Обратный ход
            else if (message.Phase == 3)
            {
                Console.WriteLine($"[Worker {workerId}] Выполнение обратного хода...");
                var phaseComplete = new NetworkMessage(MessageType.PhaseComplete, null)
                {
                    Phase = 3
                };
                await SendToAsync(phaseComplete, coordinatorIp, coordinatorPort);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker {workerId}] Ошибка обработки клетки: {ex.Message}");
            Console.WriteLine(ex.StackTrace);

            var errorMessage = new NetworkMessage(MessageType.BlockError, null)
            {
                BlockRowIndex = message.BlockRowIndex,
                BlockColIndex = message.BlockColIndex
            };
            await SendToAsync(errorMessage, coordinatorIp, coordinatorPort);
        }
    }

    /// <summary>
    /// Обмен данными между этапами
    /// </summary>
    private async Task HandleExchangeData(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        Console.WriteLine($"[Worker {workerId}] Получены данные для обмена");
        // Логика обмена ведущими строками между воркерами
        await Task.CompletedTask;
    }

    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }
}
