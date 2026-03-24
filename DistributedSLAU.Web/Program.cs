using DistributedSLAU.Common;
using DistributedSLAU.Coordinator;
using System.Collections.Concurrent;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
});
builder.Services.AddSingleton<WorkerManager>();
builder.Services.AddSingleton<SolutionSessionManager>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Автозапуск воркеров из файла workers.txt при старте
_ = Task.Run(async () =>
{
    await Task.Delay(2000); // Ждём запуска сервера
    var workersFilePath = Path.Combine(AppContext.BaseDirectory, "workers.txt");
    if (File.Exists(workersFilePath))
    {
        var lines = File.ReadAllLines(workersFilePath);
        var workerManager = app.Services.GetRequiredService<WorkerManager>();
        
        Console.WriteLine($"[AutoStart] Загрузка воркеров из {workersFilePath}");
        
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var parts = line.Trim().Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int port))
            {
                Console.WriteLine($"[AutoStart] Запуск воркера на порту {port}...");
                var result = await workerManager.StartWorker(port);
                Console.WriteLine($"[AutoStart] Результат: {result}");
            }
        }
    }
});

// API для управления воркерами
app.MapPost("/api/workers/start/{port}", async (int port, WorkerManager manager) =>
    Results.Json(await manager.StartWorker(port)));

app.MapPost("/api/workers/stop/{id}", async (string id, WorkerManager manager) =>
    Results.Json(await manager.StopWorker(id)));

app.MapPost("/api/workers/stop-all", async (WorkerManager manager) =>
    Results.Json(await manager.StopAllWorkers()));

app.MapGet("/api/workers", (WorkerManager manager) =>
    Results.Json(manager.GetWorkers()));

// Генерация системы (сохраняется на сервере)
app.MapPost("/api/generate", (GenerateRequest request, SolutionSessionManager sessions) =>
{
    try
    {
        if (request.Size < 2 || request.Size > 5000)
            return Results.Json(new { success = false, error = "Размер от 2 до 5000" });

        var system = MatrixGenerator.GenerateDiagonallyDominant(request.Size, 2.0, request.Seed);
        var sessionId = sessions.Save(system);

        Console.WriteLine($"[API] Система сгенерирована: sessionId={sessionId}, size={system.Size}");

        return Results.Json(new {
            success = true,
            sessionId = sessionId,
            size = system.Size,
            message = $"Система {system.Size}x{system.Size} сгенерирована"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API] Ошибка генерации: {ex.Message}");
        return Results.Json(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
    }
});

// Получить информацию о системе (без матрицы)
app.MapGet("/api/system/{sessionId}", (string sessionId, SolutionSessionManager sessions) =>
{
    var system = sessions.Get(sessionId);
    if (system == null)
        return Results.Json(new { success = false, error = "Система не найдена" });
    
    return Results.Json(new {
        success = true,
        sessionId,
        size = system.Size,
        preview = GetMatrixPreview(system)
    });
});

// Последовательное решение
app.MapPost("/api/solve/sequential", async (SolveRequest request, SolutionSessionManager sessions) =>
{
    try
    {
        LinearSystem? system = null;

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            Console.WriteLine($"[API] Загрузка системы из sessionId={request.SessionId}");
            system = sessions.Get(request.SessionId);
            if (system != null)
                Console.WriteLine($"[API] Система загружена: size={system.Size}");
            else
                Console.WriteLine($"[API] Система НЕ загрушена из sessionId={request.SessionId}");
        }
        else if (request.Size > 0 && request.Matrix != null && request.Matrix.Length > 0)
        {
            system = request.ToLinearSystem();
        }

        if (system == null)
            return Results.Json(new { success = false, error = "Система не найдена" });

        var sw = Stopwatch.StartNew();
        var solution = CellGaussSolver.SolveSequential(system);
        sw.Stop();

        var residual = system.ComputeResidual(solution);

        return Results.Json(new {
            success = true,
            method = "sequential",
            timeMs = sw.ElapsedMilliseconds,
            residual = residual,
            systemSize = system.Size,
            solutionPreview = solution.Take(Math.Min(10, solution.Length)).ToArray(),
            solutionFull = system.Size <= 100 ? solution : null
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
    }
});

// Распределённое решение
app.MapPost("/api/solve/distributed", async (SolveRequest request, WorkerManager manager, SolutionSessionManager sessions) =>
{
    try
    {
        LinearSystem? system = null;

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            Console.WriteLine($"[API] Загрузка системы из sessionId={request.SessionId}");
            system = sessions.Get(request.SessionId);
            if (system != null)
                Console.WriteLine($"[API] Система загружена: size={system.Size}");
            else
                Console.WriteLine($"[API] Система НЕ загружена из sessionId={request.SessionId}");
        }
        else if (request.Size > 0 && request.Matrix != null && request.Matrix.Length > 0)
        {
            system = request.ToLinearSystem();
        }

        if (system == null)
            return Results.Json(new { success = false, error = "Система не найдена" });

        // Получаем воркеров: сначала из активных, иначе из файла workers.txt
        var workers = manager.GetActiveWorkers();
        if (!workers.Any())
        {
            // Пытаемся загрузить из файла workers.txt
            var workersFilePath = Path.Combine(AppContext.BaseDirectory, "workers.txt");
            if (File.Exists(workersFilePath))
            {
                var lines = File.ReadAllLines(workersFilePath);
                workers = lines
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l =>
                    {
                        var parts = l.Trim().Split(':');
                        return (Ip: parts[0].Trim(), Port: int.Parse(parts[1].Trim()));
                    })
                    .ToList();
                Console.WriteLine($"[API] Загружено воркеров из файла: {workers.Count()}");
            }
            
            if (!workers.Any())
                return Results.Json(new { success = false, error = "Нет активных воркеров и файл workers.txt не найден" });
        }

        // Используем случайный свободный порт для координатора
        var random = new Random();
        int coordinatorPort = 20000 + random.Next(0, 1000);

        var coordinator = new DistributedCoordinator(coordinatorPort);
        coordinator.StartListening();

        // Сохраняем воркеров во временный файл для координатора
        var workersFile = Path.Combine(Path.GetTempPath(), $"w_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(workersFile, string.Join("\n", workers.Select(w => $"{w.Ip}:{w.Port}")));
        coordinator.LoadWorkers(workersFile);

        Console.WriteLine($"[API] Запуск распределённого решения: size={system.Size}, workers={workers.Count()}");
        
        var sw = Stopwatch.StartNew();
        var solution = await coordinator.SolveDistributedAsync(system);  // Передаём систему напрямую!
        sw.Stop();

        Console.WriteLine($"[API] Решение найдено за {sw.ElapsedMilliseconds} мс");

        var residual = system.ComputeResidual(solution);

        coordinator.Stop();
        try { File.Delete(workersFile); } catch { }

        return Results.Json(new {
            success = true,
            method = "distributed",
            timeMs = sw.ElapsedMilliseconds,
            residual = residual,
            systemSize = system.Size,
            workersCount = workers.Count(),
            solutionPreview = solution.Take(Math.Min(10, solution.Length)).ToArray(),
            solutionFull = system.Size <= 100 ? solution : null
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API] Ошибка: {ex.Message}");
        return Results.Json(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
    }
});

// Сравнение методов (с генерацией новой системы)
app.MapPost("/api/solve/compare", async (int size, int? seed, WorkerManager manager) =>
{
    try
    {
        // Генерируем систему
        var actualSize = size > 0 ? size : 100;
        var system = MatrixGenerator.GenerateDiagonallyDominant(actualSize, 2.0, seed ?? 42);

        // Последовательное
        var sw1 = Stopwatch.StartNew();
        var solution1 = CellGaussSolver.SolveSequential(system);
        sw1.Stop();
        var residual1 = system.ComputeResidual(solution1);

        // Распределённое - получаем воркеров из активных или из файла
        var workers = manager.GetActiveWorkers();
        long time2 = 0;
        double[]? solution2 = null;
        double residual2 = 0;

        // Пытаемся получить воркеров из файла если нет активных
        if (!workers.Any())
        {
            var workersFilePath = Path.Combine(AppContext.BaseDirectory, "workers.txt");
            if (File.Exists(workersFilePath))
            {
                var lines = File.ReadAllLines(workersFilePath);
                workers = lines
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l =>
                    {
                        var parts = l.Trim().Split(':');
                        return (Ip: parts[0].Trim(), Port: int.Parse(parts[1].Trim()));
                    })
                    .ToList();
            }
        }

        if (workers.Any())
        {
            // Используем случайный свободный порт для координатора
            var random = new Random();
            int coordinatorPort = 20000 + random.Next(0, 1000);

            var coordinator = new DistributedCoordinator(coordinatorPort);
            coordinator.StartListening();

            // Передаём воркеров напрямую
            foreach (var w in workers)
            {
                coordinator.AddWorker(w.Ip, w.Port);
            }

            var sw2 = Stopwatch.StartNew();
            solution2 = await coordinator.SolveDistributedAsync(system);
            sw2.Stop();
            time2 = sw2.ElapsedMilliseconds;
            residual2 = system.ComputeResidual(solution2);

            coordinator.Stop();
        }

        double maxDiff = solution2 != null
            ? Enumerable.Range(0, system.Size).Max(i => Math.Abs(solution1[i] - solution2[i]))
            : 0;

        return Results.Json(new {
            success = true,
            systemSize = system.Size,
            sequential = new {
                timeMs = sw1.ElapsedMilliseconds,
                residual = residual1
            },
            distributed = workers.Any() ? new {
                timeMs = time2,
                residual = residual2,
                workersCount = workers.Count()
            } : null,
            comparison = new {
                speedup = time2 > 0 ? (double)sw1.ElapsedMilliseconds / time2 : 0,
                maxDifference = maxDiff
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
    }
});

app.MapGet("/api/history", (SolutionSessionManager sessions) =>
    Results.Json(sessions.GetHistory()));

app.MapDelete("/api/history", (SolutionSessionManager sessions) =>
{
    sessions.ClearHistory();
    return Results.Ok();
});

app.Run();

// Вспомогательные методы
static object GetMatrixPreview(LinearSystem system)
{
    int previewSize = Math.Min(5, system.Size);
    var preview = new double[previewSize][];
    for (int i = 0; i < previewSize; i++)
    {
        preview[i] = new double[previewSize];
        for (int j = 0; j < previewSize; j++)
        {
            preview[i][j] = system.Matrix[i, j];
        }
    }
    return new {
        size = previewSize,
        matrix = preview,
        vector = system.VectorB.Take(previewSize).ToArray()
    };
}

// Модели
public class GenerateRequest
{
    public int Size { get; set; }
    public int? Seed { get; set; }
}

public class SolveRequest
{
    public string? SessionId { get; set; }
    public int Size { get; set; }
    public double[][]? Matrix { get; set; }
    public double[]? Vector { get; set; }
    
    public LinearSystem ToLinearSystem()
    {
        var system = new LinearSystem(Size);
        if (Matrix != null && Vector != null)
        {
            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                    system.Matrix[i, j] = Matrix[i][j];
                system.VectorB[i] = Vector[i];
            }
        }
        return system;
    }
}

public class WorkerManager
{
    private readonly ConcurrentDictionary<string, (System.Diagnostics.Process Process, DateTime StartTime)> workers = new();
    private readonly string workerDllPath;
    
    public WorkerManager()
    {
        workerDllPath = Path.Combine(AppContext.BaseDirectory, "Worker_App", "DistributedSLAU.Worker.dll");
    }
    
    public async Task<object> StartWorker(int port)
    {
        try
        {
            string workerId = $"worker_{port}";
            if (workers.ContainsKey(workerId) && !workers[workerId].Process.HasExited)
                return new { success = false, error = $"Воркер на порту {port} уже запущен" };
            
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{workerDllPath}\" {port}",
                WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Worker_App"),
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
            };
            
            var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                workers[workerId] = (process, DateTime.Now);
                await Task.Delay(5000);
                
                return new
                {
                    success = true, id = workerId, port, pid = process.Id,
                    running = !process.HasExited,
                    message = $"Воркер запущен (PID: {process.Id})" + (!process.HasExited ? " ✅" : " ⚠️")
                };
            }
            
            return new { success = false, error = "Не удалось запустить" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }
    
    public Task<object> StopWorker(string id)
    {
        if (workers.TryRemove(id, out var worker))
        {
            try
            {
                if (!worker.Process.HasExited) worker.Process.Kill();
                return Task.FromResult<object>(new { success = true, message = $"Воркер {id} остановлен" });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new { success = false, error = ex.Message });
            }
        }
        return Task.FromResult<object>(new { success = false, error = "Воркер не найден" });
    }
    
    public async Task<object> StopAllWorkers()
    {
        int stopped = 0;
        foreach (var kvp in workers.ToList())
        {
            try
            {
                if (!kvp.Value.Process.HasExited) { kvp.Value.Process.Kill(); stopped++; }
            } catch { }
        }
        workers.Clear();
        return await Task.FromResult<object>(new { success = true, stopped, message = $"Остановлено: {stopped}" });
    }
    
    public IEnumerable<object> GetWorkers() => workers.Select(kvp => new
    {
        id = kvp.Key,
        port = kvp.Value.Process.StartInfo?.Arguments?.Split(' ').LastOrDefault() ?? "0",
        pid = kvp.Value.Process.Id,
        status = kvp.Value.Process.HasExited ? "stopped" : "running",
        startTime = kvp.Value.StartTime
    });
    
    public IEnumerable<(string Ip, int Port)> GetActiveWorkers() => workers
        .Where(w => !w.Value.Process.HasExited)
        .Select(kvp => ("127.0.0.1", int.Parse(kvp.Value.Process.StartInfo?.Arguments?.Split(' ').LastOrDefault() ?? "0")));
}

public class SolutionSessionManager
{
    private static readonly string SessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
    private static readonly ConcurrentBag<SolveHistoryItem> history = new();
    private readonly ILogger<SolutionSessionManager>? logger;
    
    public SolutionSessionManager(ILogger<SolutionSessionManager>? logger = null)
    {
        this.logger = logger;
        Directory.CreateDirectory(SessionsDir);
        
        // Очистка старых сессий при запуске
        try {
            foreach (var file in Directory.GetFiles(SessionsDir, "*.session"))
            {
                var creationTime = File.GetCreationTime(file);
                if ((DateTime.Now - creationTime).TotalHours > 1)
                {
                    File.Delete(file);
                }
            }
        } catch { }
    }
    
    public string Save(LinearSystem system)
    {
        var sessionId = Guid.NewGuid().ToString();
        var filePath = Path.Combine(SessionsDir, $"{sessionId}.session");

        var data = new {
            size = system.Size,
            matrix = Enumerable.Range(0, system.Size)
                .Select(i => Enumerable.Range(0, system.Size)
                    .Select(j => system.Matrix[i, j]).ToArray()).ToArray(),
            vector = system.VectorB
        };

        File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(data));
        Console.WriteLine($"[Session] Сохранено: {sessionId} в {filePath}");
        return sessionId;
    }

    public LinearSystem? Get(string sessionId)
    {
        var filePath = Path.Combine(SessionsDir, $"{sessionId}.session");
        Console.WriteLine($"[Session] Поиск файла: {filePath}");
        
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[Session] Файл не найден: {filePath}");
            logger?.LogWarning($"Сессия {sessionId} не найдена");
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            Console.WriteLine($"[Session] Чтение файла, длина JSON: {json.Length}");
            var data = System.Text.Json.JsonSerializer.Deserialize<MatrixData>(json);

            var system = new LinearSystem(data.size);
            for (int i = 0; i < data.size; i++)
            {
                for (int j = 0; j < data.size; j++)
                {
                    system.Matrix[i, j] = data.matrix[i][j];
                }
                system.VectorB[i] = data.vector[i];
            }

            Console.WriteLine($"[Session] Система загружена: size={system.Size}");
            logger?.LogInformation($"Сессия {sessionId} загружена");
            return system;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] Ошибка загрузки: {ex.Message}");
            logger?.LogError($"Ошибка загрузки сессии: {ex.Message}");
            return null;
        }
    }
    
    public void AddToHistory(string method, int size, long timeMs, double residual)
    {
        history.Add(new SolveHistoryItem {
            Method = method,
            Size = size,
            TimeMs = timeMs,
            Residual = residual,
            Timestamp = DateTime.Now
        });
    }
    
    public IEnumerable<SolveHistoryItem> GetHistory() => history.OrderByDescending(h => h.Timestamp).Take(50);
    public void ClearHistory() => history.Clear();
}

public class MatrixData
{
    public int size { get; set; }
    public double[][] matrix { get; set; } = Array.Empty<double[]>();
    public double[] vector { get; set; } = Array.Empty<double>();
}

public record SolveHistoryItem
{
    public string Method { get; init; } = "";
    public int Size { get; init; }
    public long TimeMs { get; init; }
    public double Residual { get; init; }
    public DateTime Timestamp { get; init; }
}
