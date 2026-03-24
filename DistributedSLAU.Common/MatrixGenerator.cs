namespace DistributedSLAU.Common;

/// <summary>
/// Генератор тестовых матриц для СЛАУ
/// </summary>
public static class MatrixGenerator
{
    private static readonly Random DefaultRandom = new(42);

    /// <summary>
    /// Генерация случайной диагонально-доминантной матрицы (хорошо обусловленной)
    /// </summary>
    public static LinearSystem GenerateDiagonallyDominant(int size, double diagonalFactor = 2.0, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : DefaultRandom;
        var system = new LinearSystem(size);

        for (int i = 0; i < size; i++)
        {
            double rowSum = 0;
            
            for (int j = 0; j < size; j++)
            {
                if (i != j)
                {
                    system.Matrix[i, j] = (random.NextDouble() - 0.5) * 10;
                    rowSum += Math.Abs(system.Matrix[i, j]);
                }
            }
            
            // Диагональное преобладание для устойчивости
            system.Matrix[i, i] = rowSum * diagonalFactor + random.NextDouble() * 5 + 1;
            system.VectorB[i] = (random.NextDouble() - 0.5) * 100;
        }

        return system;
    }

    /// <summary>
    /// Генерация матрицы с известным решением (для проверки точности)
    /// </summary>
    public static (LinearSystem system, double[] knownSolution) GenerateWithKnownSolution(int size, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : DefaultRandom;
        
        // Генерируем известное решение
        var knownSolution = new double[size];
        for (int i = 0; i < size; i++)
        {
            knownSolution[i] = (random.NextDouble() - 0.5) * 20;
        }

        // Генерируем диагонально-доминантную матрицу
        var system = GenerateDiagonallyDominant(size, 2.0, seed);

        // Вычисляем вектор b = A * x_known
        for (int i = 0; i < size; i++)
        {
            double sum = 0;
            for (int j = 0; j < size; j++)
            {
                sum += system.Matrix[i, j] * knownSolution[j];
            }
            system.VectorB[i] = sum;
        }

        return (system, knownSolution);
    }

    /// <summary>
    /// Генерация плотной матрицы без специальных свойств
    /// </summary>
    public static LinearSystem GenerateDense(int size, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : DefaultRandom;
        var system = new LinearSystem(size);

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                system.Matrix[i, j] = (random.NextDouble() - 0.5) * 20;
            }
            // Добавляем немного к диагонали для избежания вырожденности
            system.Matrix[i, i] += size * 2;
            system.VectorB[i] = (random.NextDouble() - 0.5) * 100;
        }

        return system;
    }

    /// <summary>
    /// Генерация ленточной матрицы (для тестирования производительности)
    /// </summary>
    public static LinearSystem GenerateBanded(int size, int bandwidth, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : DefaultRandom;
        var system = new LinearSystem(size);

        for (int i = 0; i < size; i++)
        {
            int startCol = Math.Max(0, i - bandwidth);
            int endCol = Math.Min(size - 1, i + bandwidth);

            for (int j = 0; j < size; j++)
            {
                if (j >= startCol && j <= endCol)
                {
                    system.Matrix[i, j] = (random.NextDouble() - 0.5) * 10;
                }
            }
            
            system.Matrix[i, i] += bandwidth * 2 + 5;
            system.VectorB[i] = (random.NextDouble() - 0.5) * 50;
        }

        return system;
    }

    /// <summary>
    /// Сохранение матрицы в файл
    /// </summary>
    public static void SaveToFile(LinearSystem system, string matrixPath, string vectorPath, bool append = false)
    {
        using var matrixWriter = new StreamWriter(matrixPath, append);
        using var vectorWriter = new StreamWriter(vectorPath, append);

        if (append)
        {
            matrixWriter.WriteLine();
            vectorWriter.WriteLine();
        }

        matrixWriter.WriteLine(system.Size);
        for (int i = 0; i < system.Size; i++)
        {
            var row = new List<string>();
            for (int j = 0; j < system.Size; j++)
            {
                row.Add(system.Matrix[i, j].ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            }
            matrixWriter.WriteLine(string.Join(" ", row));
        }

        var vectorValues = new List<string>();
        for (int i = 0; i < system.Size; i++)
        {
            vectorValues.Add(system.VectorB[i].ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
        }
        vectorWriter.WriteLine(string.Join(" ", vectorValues));
    }

    /// <summary>
    /// Загрузка матрицы из файла
    /// </summary>
    public static LinearSystem LoadFromFile(string matrixPath, string vectorPath, int matrixIndex = 0)
    {
        string matrixContent = File.ReadAllText(matrixPath);
        string vectorContent = File.ReadAllText(vectorPath);

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
            string[] parts = matrixLines[i + 1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int j = 0; j < size; j++)
            {
                system.Matrix[i, j] = double.Parse(parts[j].Replace(',', '.'), culture);
            }
        }

        string[] vectorParts = vectorBlocks[matrixIndex].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < size; i++)
        {
            system.VectorB[i] = double.Parse(vectorParts[i].Replace(',', '.'), culture);
        }

        return system;
    }
}
