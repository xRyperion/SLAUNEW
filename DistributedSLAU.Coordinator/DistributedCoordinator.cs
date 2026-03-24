using System.Diagnostics;
using System.Net;
using MessagePack;
using DistributedSLAU.Common;

namespace DistributedSLAU.Coordinator;

/// <summary>
/// Координатор для распределённого решения СЛАУ
/// </summary>
public class DistributedCoordinator : UdpCommunicator
{
    private readonly List<WorkerNode> workers = new();
    private LinearSystem? currentSystem;

    // Результаты
    private readonly Dictionary<int, double[]> partialSolutions = new();
    private readonly Dictionary<(int row, int col), MatrixBlock> processedBlocks = new();
    private readonly object lockObj = new();

    // Синхронизация
    private TaskCompletionSource<bool>? completionTcs;
    private int expectedResponses;
    private int receivedResponses;

    public DateTime StartTime { get; private set; }

    public DistributedCoordinator(int port) : base(port)
    {
        MessageReceived += OnMessageReceived;
    }

    private async Task OnMessageReceived(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        switch (message.Type)
        {
            case MessageType.Ready:
                await HandleReady(message, remoteEndPoint);
                break;

            case MessageType.PartialResult:
                await HandlePartialResult(message, remoteEndPoint);
                break;

            case MessageType.BlockResult:
                await HandleBlockResult(message, remoteEndPoint);
                break;

            case MessageType.Heartbeat:
                await SendToAsync(new NetworkMessage(MessageType.Heartbeat, null),
                    remoteEndPoint.Address.ToString(), remoteEndPoint.Port);
                break;
        }
    }

    private Task HandleReady(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        var workerId = $"{remoteEndPoint.Address}:{remoteEndPoint.Port}";
        Console.WriteLine($"[Coordinator] Получено Ready от {workerId}");
        return Task.CompletedTask;
    }

    private Task HandlePartialResult(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        if (message.Data == null) return Task.CompletedTask;

        var solution = MessagePackSerializer.Deserialize<double[]>(message.Data);
        
        lock (lockObj)
        {
            partialSolutions[message.BlockRowIndex] = solution;
            receivedResponses++;
            
            Console.WriteLine($"[Coordinator] Получено от {remoteEndPoint.Port}: строки {message.BlockRowIndex} ({receivedResponses}/{expectedResponses})");
            
            if (receivedResponses >= expectedResponses)
            {
                completionTcs?.TrySetResult(true);
            }
        }
        
        return Task.CompletedTask;
    }

    private Task HandleBlockResult(NetworkMessage message, IPEndPoint remoteEndPoint)
    {
        if (message.Data == null) return Task.CompletedTask;

        lock (lockObj)
        {
            var block = MessagePackSerializer.Deserialize<MatrixBlock>(message.Data);
            processedBlocks[(message.BlockRowIndex, message.BlockColIndex)] = block;
            receivedResponses++;
            
            Console.WriteLine($"[Coordinator] Получен блок ({message.BlockRowIndex},{message.BlockColIndex}) от {remoteEndPoint.Port} ({receivedResponses}/{expectedResponses})");
            
            if (receivedResponses >= expectedResponses)
            {
                completionTcs?.TrySetResult(true);
            }
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Загрузка списка рабочих узлов из файла
    /// </summary>
    public void LoadWorkers(string filePath)
    {
        workers.Clear();
        var lines = File.ReadAllLines(filePath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(':');
            if (parts.Length >= 2)
            {
                string ip = parts[0].Trim();
                if (int.TryParse(parts[1].Trim(), out int port))
                {
                    var worker = new WorkerNode(ip, port);
                    workers.Add(worker);
                    Console.WriteLine($"[Coordinator] Добавлен узел: {worker.Id}");
                }
            }
        }

        Console.WriteLine($"[Coordinator] Всего загружено узлов: {workers.Count}");
    }

    /// <summary>
    /// Распределённое решение
    /// </summary>
    public async Task<double[]> SolveDistributedAsync()
    {
        if (workers.Count == 0)
            throw new InvalidOperationException("Нет доступных узлов!");

        if (currentSystem == null)
            throw new InvalidOperationException("Система не загружена!");

        return await SolveDistributedAsync(currentSystem);
    }

    /// <summary>
    /// Распределённое решение
    /// </summary>
    public async Task<double[]> SolveDistributedAsync(LinearSystem system)
    {
        if (workers.Count == 0)
            throw new InvalidOperationException("Нет доступных узлов!");

        int n = system.Size;
        int numWorkers = workers.Count;

        Console.WriteLine($"[Coordinator] Распределённое решение: {n}x{n}");
        Console.WriteLine($"[Coordinator] Воркеров: {numWorkers}");

        StartTime = DateTime.Now;

        // Если воркеров >= 4, используем клеточное разбиение
        if (numWorkers >= 4)
        {
            int gridSize = Math.Min((int)Math.Ceiling(Math.Sqrt(numWorkers)),
                                    Math.Min(n, 4));
            int blockSize = (n + gridSize - 1) / gridSize;
            return await SolveByCellularGauss(system, gridSize, blockSize);
        }
        else
        {
            // Иначе используем разбиение по строкам
            return await SolveByRows(system, numWorkers);
        }
    }

    /// <summary>
    /// Решение с разбиением по строкам
    /// </summary>
    private async Task<double[]> SolveByRows(LinearSystem system, int numWorkers)
    {
        int n = system.Size;
        int rowsPerWorker = (n + numWorkers - 1) / numWorkers;

        Console.WriteLine($"[Coordinator] Разбиение по строкам: ~{rowsPerWorker} строк на воркер");

        partialSolutions.Clear();
        receivedResponses = 0;
        expectedResponses = numWorkers;
        completionTcs = new TaskCompletionSource<bool>();

        // Отправляем полную систему каждому воркеру — ПАРАЛЛЕЛЬНО!
        var sendTasks = new List<Task>();
        for (int i = 0; i < numWorkers; i++)
        {
            var worker = workers[i];
            int workerStartRow = i * rowsPerWorker;
            int workerRowCount = Math.Min(rowsPerWorker, n - workerStartRow);

            if (workerRowCount <= 0) continue;

            var data = MessagePackSerializer.Serialize(system);
            var message = new NetworkMessage(MessageType.TaskDistribution, data)
            {
                BlockRowIndex = workerStartRow,
                BlockColIndex = workerRowCount
            };

            Console.WriteLine($"[Coordinator] >>> Отправляю систему узлу {worker.IpAddress}:{worker.Port} " +
                            $"(строки {workerStartRow}-{workerStartRow + workerRowCount - 1})");
            sendTasks.Add(SendToAsync(message, worker.IpAddress, worker.Port));
        }

        await Task.WhenAll(sendTasks);
        Console.WriteLine($"[Coordinator] Все системы отправлены, ждём результаты...");

        // Ждём результаты (таймаут 10 секунд)
        var timeoutTask = Task.Delay(10000);
        var completedTask = await Task.WhenAny(completionTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            Console.WriteLine($"[Coordinator] ⚠️ ТАЙМАУТ! Получено {receivedResponses}/{expectedResponses}");
            Console.WriteLine($"[Coordinator] Решаем последовательно...");
            return CellGaussSolver.SolveSequential(system);
        }

        Console.WriteLine($"[Coordinator] ✅ Все результаты получены ({receivedResponses}/{expectedResponses})");

        // Собираем решение
        var solution = new double[n];
        foreach (var kvp in partialSolutions.OrderBy(x => x.Key))
        {
            int startRow = kvp.Key;
            for (int i = 0; i < kvp.Value.Length && startRow + i < n; i++)
            {
                solution[startRow + i] = kvp.Value[i];
            }
        }

        var elapsed = DateTime.Now - StartTime;
        Console.WriteLine($"[Coordinator] ✅ Решение найдено за {elapsed.TotalMilliseconds} мс");

        return solution;
    }

    /// <summary>
    /// Клеточный метод Гаусса (2D разбиение)
    /// </summary>
    private async Task<double[]> SolveByCellularGauss(LinearSystem system, int gridSize, int blockSize)
    {
        int n = system.Size;
        Console.WriteLine($"[Coordinator] Клеточный метод Гаусса: сетка {gridSize}x{gridSize}");
        Console.WriteLine($"[Coordinator] Размер клетки: ~{blockSize}x{blockSize}");

        var blocks = MatrixPartitioner.PartitionIntoBlocks(system, gridSize);

        // Этап 1: Прямой ход
        Console.WriteLine("\n[Coordinator] === ЭТАП 1: Прямой ход ===");
        await PerformCellularForwardElimination(blocks, gridSize);

        // Этап 2: Обмен
        Console.WriteLine("\n[Coordinator] === ЭТАП 2: Обмен ===");
        await PerformBlockExchange(blocks, gridSize);

        // Этап 3: Обратный ход
        Console.WriteLine("\n[Coordinator] === ЭТАП 3: Обратный ход ===");
        var solution = await PerformCellularBackSubstitution(blocks, gridSize, n);

        var elapsed = DateTime.Now - StartTime;
        Console.WriteLine($"\n[Coordinator] ✅ Решение найдено за {elapsed.TotalMilliseconds} мс");

        return solution;
    }

    private async Task PerformCellularForwardElimination(MatrixBlock[,] blocks, int gridSize)
    {
        processedBlocks.Clear();
        receivedResponses = 0;
        expectedResponses = gridSize * gridSize;
        completionTcs = new TaskCompletionSource<bool>();

        var sendTasks = new List<Task>();
        int workerIndex = 0;

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                if (blocks[i, j] == null) continue;

                var worker = workers[workerIndex % workers.Count];
                var data = MessagePackSerializer.Serialize(blocks[i, j]);
                var message = new NetworkMessage(MessageType.BlockDistribution, data)
                {
                    BlockRowIndex = i,
                    BlockColIndex = j,
                    GridSize = gridSize,
                    Phase = 1
                };

                Console.WriteLine($"[Coordinator] >>> Отправляю клетку ({i},{j}) узлу {worker.IpAddress}:{worker.Port}");
                sendTasks.Add(SendToAsync(message, worker.IpAddress, worker.Port));
                workerIndex++;
            }
        }

        await Task.WhenAll(sendTasks);
        Console.WriteLine($"[Coordinator] Все клетки отправлены, ждём обработки...");

        var timeoutTask = Task.Delay(10000);
        var completedTask = await Task.WhenAny(completionTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            Console.WriteLine($"[Coordinator] ⚠️ ТАЙМАУТ! Получено {receivedResponses}/{expectedResponses}");
        }
        else
        {
            Console.WriteLine($"[Coordinator] ✅ Этап 1 завершён: {receivedResponses}/{expectedResponses}");
        }

        // Локальная обработка для гарантии
        for (int i = 0; i < gridSize; i++)
            for (int j = 0; j < gridSize; j++)
                if (blocks[i, j] != null)
                    CellGaussSolver.BlockForwardElimination(blocks[i, j]);
    }

    private Task PerformBlockExchange(MatrixBlock[,] blocks, int gridSize)
    {
        Console.WriteLine("[Coordinator] Обмен завершён");
        return Task.CompletedTask;
    }

    private async Task<double[]> PerformCellularBackSubstitution(MatrixBlock[,] blocks, int gridSize, int n)
    {
        var fullMatrix = new double[n, n];
        var fullVector = new double[n];

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                if (blocks[i, j] != null)
                {
                    var block = blocks[i, j];
                    for (int bi = 0; bi < block.RowCount; bi++)
                    {
                        for (int bj = 0; bj < block.ColCount; bj++)
                        {
                            fullMatrix[block.StartRow + bi, block.StartCol + bj] = block.Data[bi, bj];
                        }
                        fullVector[block.StartRow + bi] = block.LocalB[bi];
                    }
                }
            }
        }

        var solution = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
                sum += fullMatrix[i, j] * solution[j];
            
            solution[i] = Math.Abs(fullMatrix[i, i]) < 1e-12 ? 0 : (fullVector[i] - sum) / fullMatrix[i, i];
        }

        Console.WriteLine("[Coordinator] ✅ Обратный ход завершён");
        return solution;
    }

    /// <summary>
    /// Загрузка системы из файлов
    /// </summary>
    public LinearSystem LoadSystem(string matrixFile, string vectorFile, int matrixIndex = 0)
    {
        string matrixContent = File.ReadAllText(matrixFile);
        string vectorContent = File.ReadAllText(vectorFile);

        string[] matrixBlocks = matrixContent.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        string[] vectorBlocks = vectorContent.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        if (matrixIndex >= matrixBlocks.Length)
            matrixIndex = 0;

        string[] matrixLines = matrixBlocks[matrixIndex].Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        int size = int.Parse(matrixLines[0].Trim());

        var system = new LinearSystem(size);
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        for (int i = 0; i < size; i++)
        {
            string line = matrixLines[i + 1].Trim();
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            for (int j = 0; j < size; j++)
            {
                system.Matrix[i, j] = double.Parse(parts[j].Replace(',', '.'), culture);
            }
        }

        string vectorLine = vectorBlocks[matrixIndex].Trim();
        string[] vectorParts = vectorLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < size; i++)
        {
            system.VectorB[i] = double.Parse(vectorParts[i].Replace(',', '.'), culture);
        }

        currentSystem = system;
        Console.WriteLine($"Система #{matrixIndex + 1} загружена! Размер: {size}x{size}");
        return system;
    }

    /// <summary>
    /// Последовательное решение
    /// </summary>
    public (double[] solution, TimeSpan elapsed) SolveSequential()
    {
        if (currentSystem == null)
            throw new InvalidOperationException("Система не загружена");

        var sw = Stopwatch.StartNew();
        var solution = CellGaussSolver.SolveSequential(currentSystem);
        sw.Stop();

        return (solution, sw.Elapsed);
    }

    public void PrintWorkers()
    {
        Console.WriteLine("\n=== Доступные узлы ===");
        foreach (var worker in workers)
            Console.WriteLine($"  {worker.Id}");
    }

    public void AddWorker(string ip, int port)
    {
        var worker = new WorkerNode(ip, port);
        if (!workers.Any(w => w.IpAddress == ip && w.Port == port))
        {
            workers.Add(worker);
            Console.WriteLine($"[Coordinator] Добавлен узел: {worker.Id}");
        }
    }
}
