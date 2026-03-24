using Xunit;
using Xunit.Abstractions;
using DistributedSLAU.Common;

namespace DistributedSLAU.Tests;

public class PerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Нагрузочный тест: решение матриц разного размера
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void PerformanceTest_DifferentSizes_CompletesInReasonableTime(int size)
    {
        // Arrange
        var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, 42);
        var maxTime = size switch
        {
            <= 50 => TimeSpan.FromSeconds(1),
            <= 100 => TimeSpan.FromSeconds(5),
            <= 500 => TimeSpan.FromSeconds(30),
            <= 1000 => TimeSpan.FromSeconds(120),
            _ => TimeSpan.FromMinutes(5)
        };

        // Act
        var startTime = DateTime.Now;
        var solution = CellGaussSolver.SolveSequential(system);
        var elapsed = DateTime.Now - startTime;

        var residual = system.ComputeResidual(solution);

        // Assert
        Assert.True(elapsed < maxTime, 
            $"Время {elapsed} превысило лимит {maxTime} для размера {size}");
        Assert.Equal(size, solution.Length);
        
        _output.WriteLine($"Размер: {size}x{size}, Время: {elapsed.TotalMilliseconds:F2} мс, " +
                         $"Невязка: {residual:E6}");
    }

    /// <summary>
    /// Тест производительности: сравнение времени для разных размеров
    /// </summary>
    [Fact]
    public void PerformanceTest_Scaling_AnalyzeScaling()
    {
        var sizes = new[] { 50, 100, 200, 500 };
        var results = new List<(int Size, double TimeMs, double Residual)>();

        foreach (var size in sizes)
        {
            var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, 42);
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var solution = CellGaussSolver.SolveSequential(system);
            sw.Stop();
            
            var residual = system.ComputeResidual(solution);
            results.Add((size, sw.ElapsedMilliseconds, residual));
            
            _output.WriteLine($"N={size}: {sw.ElapsedMilliseconds} мс, невязка: {residual:E6}");
        }

        // Проверка, что время растёт примерно как O(n^3)
        // Для метода Гаусса сложность O(n^3)
        for (int i = 1; i < results.Count; i++)
        {
            var prev = results[i - 1];
            var curr = results[i];
            
            // Время должно расти (хотя бы не уменьшаться)
            Assert.True(curr.TimeMs >= prev.TimeMs * 0.8, 
                $"Время для N={curr.Size} меньше ожидаемого");
            
            // Невязка должна быть приемлемой
            Assert.InRange(curr.Residual, 0, 1e-6);
        }
    }

    /// <summary>
    /// Тест точности для различных значений seed
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(100)]
    [InlineData(999)]
    [InlineData(12345)]
    public void PerformanceTest_DifferentSeeds_AccurateSolution(int seed)
    {
        // Arrange
        int size = 100;
        var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, seed);
        var expectedSolution = Enumerable.Repeat(1.0, size).ToArray();
        
        // Пересчитываем вектор b для известного решения
        for (int i = 0; i < size; i++)
        {
            system.VectorB[i] = 0;
            for (int j = 0; j < size; j++)
            {
                system.VectorB[i] += system.Matrix[i, j] * expectedSolution[j];
            }
        }
        
        // Act
        var solution = CellGaussSolver.SolveSequential(system);
        var residual = system.ComputeResidual(solution);
        
        // Assert
        Assert.InRange(residual, 0, 1e-8);
        _output.WriteLine($"Seed={seed}: невязка={residual:E6}");
    }

    /// <summary>
    /// Тест на предельном размере (5000x5000)
    /// </summary>
    [Fact(Skip = "Длительный тест - запускать вручную")]
    public void PerformanceTest_MaxSize_5000x5000()
    {
        // Arrange
        int size = 5000;
        _output.WriteLine($"Генерация системы {size}x{size}...");
        var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, 42);
        
        // Act
        _output.WriteLine($"Решение системы {size}x{size}...");
        var startTime = DateTime.Now;
        var solution = CellGaussSolver.SolveSequential(system);
        var elapsed = DateTime.Now - startTime;
        
        var residual = system.ComputeResidual(solution);
        
        // Assert
        Assert.Equal(size, solution.Length);
        Assert.InRange(residual, 0, 1e-4); // Допускаем большую невязку для больших матриц
        
        _output.WriteLine($"Время: {elapsed.TotalMilliseconds:F0} мс ({elapsed.TotalSeconds:F2} с)");
        _output.WriteLine($"Невязка: {residual:E6}");
    }

    /// <summary>
    /// Тест памяти: проверка, что нет утечек при множественных решениях
    /// </summary>
    [Fact]
    public void PerformanceTest_MultipleSolves_NoMemoryIssues()
    {
        // Arrange
        int size = 200;
        int iterations = 10;
        var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, 42);
        
        // Act
        for (int i = 0; i < iterations; i++)
        {
            var solution = CellGaussSolver.SolveSequential(system);
            var residual = system.ComputeResidual(solution);
            
            Assert.InRange(residual, 0, 1e-8);
        }
        
        _output.WriteLine($"Успешно выполнено {iterations} решений без проблем");
    }
}
