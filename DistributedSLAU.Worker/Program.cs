using DistributedSLAU.Worker;

namespace DistributedSLAU.Worker;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Вычислительный узел СЛАУ (Worker) ===");
        Console.WriteLine();

        int port = 11001;
        if (args.Length > 0 && int.TryParse(args[0], out int customPort))
        {
            port = customPort;
        }

        Console.WriteLine($"Порт: {port}");
        Console.WriteLine("Worker запущен и слушает UDP порт...");
        Console.WriteLine("Нажмите 'q' для выхода или закройте окно");
        Console.WriteLine();

        var worker = new DistributedWorker(port);
        worker.StartListening();

        Console.WriteLine($"✅ Worker готов к работе на порту {port}");
        Console.WriteLine($"Время запуска: {DateTime.Now:HH:mm:ss}");
        Console.WriteLine();

        // Ждём ввода или закрытия
        try
        {
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        Console.WriteLine("\nОстановка воркера...");
                        worker.Stop();
                        break;
                    }
                }
                // Показываем статус каждые 10 секунд
                if (DateTime.Now.Second % 10 == 0)
                {
                    Console.Write(".");
                }
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nОшибка: {ex.Message}");
            worker.Stop();
        }
        
        Console.WriteLine("Воркер остановлен. Нажмите любую клавишу...");
        Console.ReadKey();
    }
}
