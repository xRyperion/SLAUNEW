using MessagePack;

namespace DistributedSLAU.Common;

/// <summary>
/// Представляет систему линейных алгебраических уравнений (СЛАУ) Ax = b
/// </summary>
[MessagePackObject]
public class LinearSystem
{
    [Key(0)]
    public double[,] Matrix { get; set; } = new double[0, 0];

    [Key(1)]
    public double[] VectorB { get; set; } = new double[0];

    [Key(2)]
    public int Size { get; set; }

    public LinearSystem() { }

    public LinearSystem(int size)
    {
        Size = size;
        Matrix = new double[size, size];
        VectorB = new double[size];
    }

    /// <summary>
    /// Глубокое копирование системы
    /// </summary>
    public LinearSystem Clone()
    {
        var clone = new LinearSystem(Size);
        Array.Copy(Matrix, clone.Matrix, Matrix.Length);
        Array.Copy(VectorB, clone.VectorB, VectorB.Length);
        return clone;
    }

    /// <summary>
    /// Вычисление невязки ||Ax - b||
    /// </summary>
    public double ComputeResidual(double[] solution)
    {
        double residual = 0;
        for (int i = 0; i < Size; i++)
        {
            double sum = 0;
            for (int j = 0; j < Size; j++)
            {
                sum += Matrix[i, j] * solution[j];
            }
            residual += Math.Abs(sum - VectorB[i]);
        }
        return residual;
    }

    public void Print(int maxRows = 10)
    {
        int displayRows = Math.Min(Size, maxRows);
        Console.WriteLine($"Матрица {Size}x{Size}:");
        
        for (int i = 0; i < displayRows; i++)
        {
            for (int j = 0; j < displayRows; j++)
            {
                Console.Write($"{Matrix[i, j],8:F4} ");
            }
            if (Size > displayRows) Console.Write("... ");
            Console.Write($"| {VectorB[i],8:F4}");
            Console.WriteLine();
        }
        if (Size > displayRows)
            Console.WriteLine($"... (ещё {Size - displayRows} строк)");
    }
}
