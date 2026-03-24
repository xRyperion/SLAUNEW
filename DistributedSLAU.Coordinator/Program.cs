using DistributedSLAU.Common;
using DistributedSLAU.Coordinator;

namespace DistributedSLAU.Coordinator;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Распределённое решение СЛАУ (метод Гаусса) ===");
        Console.WriteLine();

        var coordinator = new DistributedCoordinator(11000);
        coordinator.StartListening();

        try
        {
            string workingDir = Path.Combine(AppContext.BaseDirectory);
            Console.WriteLine($"Рабочая папка: {workingDir}");

            string workersPath = Path.Combine(workingDir, "workers.txt");
            string matrixPath = Path.Combine(workingDir, "matrix.txt");
            string vectorPath = Path.Combine(workingDir, "vector.txt");

            // Проверяем наличие файлов
            if (!File.Exists(workersPath))
            {
                Console.WriteLine("Файл workers.txt не найден. Создаю пример...");
                await CreateSampleFiles(workingDir);
            }

            Console.WriteLine("Загрузка списка вычислительных узлов...");
            coordinator.LoadWorkers(workersPath);
            coordinator.PrintWorkers();

            // Выбор режима
            string? choice = "3";
            if (args.Length > 0)
            {
                choice = args[0];
            }
            else
            {
                Console.WriteLine("\n--- Выбор режима ---");
                Console.WriteLine("1. Распределённое решение (требуется запуск Worker)");
                Console.WriteLine("2. Распределённое решение (клетки)");
                Console.WriteLine("3. Последовательное решение (демо)");
                Console.WriteLine("4. Сравнение методов");
                Console.WriteLine("Использую режим 3 (демо). Для выбора введите аргумент.");
            }

            Console.WriteLine("\nЗагрузка системы...");
            var system = coordinator.LoadSystem(matrixPath, vectorPath, 0);
            system.Print(5);

            double[]? distributedSolution = null;
            double[]? sequentialSolution = null;
            TimeSpan distributedTime = TimeSpan.Zero;
            TimeSpan sequentialTime = TimeSpan.Zero;

            switch (choice)
            {
                case "1":
                    Console.WriteLine("\n--- Запуск распределённого решения ---");
                    Console.WriteLine("Убедитесь, что Worker'ы запущены!");
                    await Task.Delay(2000);
                    
                    distributedSolution = await coordinator.SolveDistributedAsync();
                    distributedTime = DateTime.Now - coordinator.StartTime;
                    PrintSolution("Распределённое", distributedSolution, system, distributedTime);
                    break;

                case "2":
                    Console.WriteLine("\nРежим клеточного решения в разработке");
                    break;

                case "3":
                    Console.WriteLine("\n--- Запуск последовательного решения ---");
                    (sequentialSolution, sequentialTime) = coordinator.SolveSequential();
                    PrintSolution("Последовательное", sequentialSolution, system, sequentialTime);
                    break;

                case "4":
                    // Сравнение
                    Console.WriteLine("\n--- Последовательное решение ---");
                    (sequentialSolution, sequentialTime) = coordinator.SolveSequential();
                    PrintSolution("Последовательное", sequentialSolution, system, sequentialTime);
                    
                    Console.WriteLine("\n--- Распределённое решение ---");
                    Console.WriteLine("Запустите Worker'ов в отдельных окнах:");
                    Console.WriteLine("  dotnet run --project DistributedSLAU.Worker");
                    Console.WriteLine("  dotnet run --project DistributedSLAU.Worker -- 11002");
                    Console.WriteLine();
                    Console.WriteLine("Нажмите Enter для продолжения или подождите 5 секунд...");
                    
                    var cts = new CancellationTokenSource();
                    var delayTask = Task.Delay(5000, cts.Token);
                    var keyTask = Task.Run(() => Console.ReadKey());
                    
                    var completed = await Task.WhenAny(delayTask, keyTask);
                    if (completed == keyTask)
                    {
                        cts.Cancel();
                    }
                    
                    distributedSolution = await coordinator.SolveDistributedAsync();
                    distributedTime = DateTime.Now - coordinator.StartTime;
                    PrintSolution("Распределённое", distributedSolution, system, distributedTime);
                    
                    Console.WriteLine("\n=== СРАВНЕНИЕ ===");
                    Console.WriteLine($"Последовательное: {sequentialTime.TotalMilliseconds:F2} мс");
                    Console.WriteLine($"Распределённое: {distributedTime.TotalMilliseconds:F2} мс");
                    
                    if (distributedSolution != null && sequentialSolution != null)
                    {
                        double maxDiff = 0;
                        for (int i = 0; i < system.Size; i++)
                        {
                            double diff = Math.Abs(distributedSolution[i] - sequentialSolution[i]);
                            maxDiff = Math.Max(maxDiff, diff);
                        }
                        Console.WriteLine($"Макс. разница между решениями: {maxDiff:E6}");
                    }
                    break;

                default:
                    Console.WriteLine("Неверный выбор");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nОшибка: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            coordinator.Stop();
            Console.WriteLine("\nКоординатор остановлен.");
        }
    }

    static void PrintSolution(string name, double[] solution, LinearSystem system, TimeSpan time)
    {
        Console.WriteLine($"\n=== {name} решение ===");
        Console.WriteLine($"Время: {time.TotalMilliseconds:F2} мс");
        
        int displayCount = Math.Min(solution.Length, 10);
        Console.Write("Первые значения: ");
        for (int i = 0; i < displayCount; i++)
        {
            Console.Write($"{solution[i]:F6} ");
        }
        if (solution.Length > displayCount)
            Console.Write($"... (всего {solution.Length})");
        Console.WriteLine();

        double residual = system.ComputeResidual(solution);
        Console.WriteLine($"Невязка ||Ax - b||: {residual:E6}");
    }

    static async Task CreateSampleFiles(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "workers.txt"), 
            "127.0.0.1:11001\n127.0.0.1:11002\n127.0.0.1:11003\n127.0.0.1:11004");
        
        string matrix = @"4
4 1 1 1
1 4 1 1
1 1 4 1
1 1 1 4";
        await File.WriteAllTextAsync(Path.Combine(dir, "matrix.txt"), matrix);
        
        string vector = "7 7 7 7";
        await File.WriteAllTextAsync(Path.Combine(dir, "vector.txt"), vector);
        
        Console.WriteLine("Созданы тестовые файлы");
    }
}
