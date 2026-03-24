using Xunit;
using DistributedSLAU.Common;

namespace DistributedSLAU.Tests;

public class UnitTests
{
    /// <summary>
    /// Тест решения простой системы 3x3
    /// </summary>
    [Fact]
    public void SolveSequential_Simple3x3_ReturnsCorrectSolution()
    {
        // Arrange: Система 3x3 с известным решением [1, 2, 3]
        var system = new LinearSystem(3);
        
        // Матрица: диагонально доминантная
        system.Matrix[0, 0] = 4; system.Matrix[0, 1] = 1; system.Matrix[0, 2] = 1;
        system.Matrix[1, 0] = 1; system.Matrix[1, 1] = 4; system.Matrix[1, 2] = 1;
        system.Matrix[2, 0] = 1; system.Matrix[2, 1] = 1; system.Matrix[2, 2] = 4;
        
        // Вектор b такой, что решение = [1, 2, 3]
        system.VectorB[0] = 4*1 + 1*2 + 1*3; // = 9
        system.VectorB[1] = 1*1 + 4*2 + 1*3; // = 12
        system.VectorB[2] = 1*1 + 1*2 + 4*3; // = 15
        
        // Act
        var solution = CellGaussSolver.SolveSequential(system);
        
        // Assert
        Assert.Equal(3, solution.Length);
        Assert.Equal(1.0, solution[0], 3); // Точность до 10^-3
        Assert.Equal(2.0, solution[1], 3);
        Assert.Equal(3.0, solution[2], 3);
    }

    /// <summary>
    /// Тест решения системы с единичным решением
    /// </summary>
    [Fact]
    public void SolveSequential_UnitSolution_ReturnsCorrectSolution()
    {
        // Arrange: Система 4x4 с решением [1, 1, 1, 1]
        var system = MatrixGenerator.GenerateDiagonallyDominant(4, 2.0, 42);
        var expectedSolution = new double[] { 1, 1, 1, 1 };
        
        // Пересчитываем вектор b для известного решения
        for (int i = 0; i < 4; i++)
        {
            system.VectorB[i] = 0;
            for (int j = 0; j < 4; j++)
            {
                system.VectorB[i] += system.Matrix[i, j] * expectedSolution[j];
            }
        }
        
        // Act
        var solution = CellGaussSolver.SolveSequential(system);
        
        // Assert
        Assert.Equal(4, solution.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(expectedSolution[i], solution[i], 6);
        }
    }

    /// <summary>
    /// Тест вычисления невязки
    /// </summary>
    [Fact]
    public void ComputeResidual_CorrectSolution_ReturnsNearZero()
    {
        // Arrange
        var system = new LinearSystem(3);
        system.Matrix[0, 0] = 2; system.Matrix[0, 1] = 1; system.Matrix[0, 2] = 0;
        system.Matrix[1, 0] = 1; system.Matrix[1, 1] = 2; system.Matrix[1, 2] = 1;
        system.Matrix[2, 0] = 0; system.Matrix[2, 1] = 1; system.Matrix[2, 2] = 2;
        system.VectorB[0] = 3; system.VectorB[1] = 4; system.VectorB[2] = 3;
        
        var correctSolution = new double[] { 1, 1, 1 };
        
        // Act
        var residual = system.ComputeResidual(correctSolution);
        
        // Assert
        Assert.InRange(residual, 0, 1e-10); // Невязка должна быть близка к 0
    }

    /// <summary>
    /// Тест генерации диагонально доминантной матрицы
    /// </summary>
    [Fact]
    public void GenerateDiagonallyDominant_ReturnsValidMatrix()
    {
        // Arrange
        int size = 10;
        int seed = 42;
        
        // Act
        var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, seed);
        
        // Assert
        Assert.Equal(size, system.Size);
        Assert.Equal(size, system.Matrix.GetLength(0));
        Assert.Equal(size, system.Matrix.GetLength(1));
        Assert.Equal(size, system.VectorB.Length);
        
        // Проверка диагонального преобладания
        for (int i = 0; i < size; i++)
        {
            double diagonal = Math.Abs(system.Matrix[i, i]);
            double sumOffDiagonal = 0;
            
            for (int j = 0; j < size; j++)
            {
                if (i != j)
                    sumOffDiagonal += Math.Abs(system.Matrix[i, j]);
            }
            
            // Диагональный элемент должен быть больше суммы остальных
            Assert.True(diagonal > sumOffDiagonal, 
                $"Строка {i}: диагональ {diagonal} <= сумме остальных {sumOffDiagonal}");
        }
    }

    /// <summary>
    /// Тест решения большой матрицы (проверка производительности и точности)
    /// </summary>
    [Fact]
    public void SolveSequential_LargeMatrix_ReturnsAccurateSolution()
    {
        // Arrange
        int size = 100;
        var system = MatrixGenerator.GenerateDiagonallyDominant(size, 2.0, 123);
        var expectedSolution = Enumerable.Repeat(1.0, size).ToArray();
        
        // Пересчитываем вектор b
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
        Assert.Equal(size, solution.Length);
        Assert.InRange(residual, 0, 1e-8); // Невязка должна быть очень мала
    }

    /// <summary>
    /// Тест клонирования системы
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new LinearSystem(3);
        original.Matrix[0, 0] = 1; original.VectorB[0] = 5;
        
        // Act
        var clone = original.Clone();
        clone.Matrix[0, 0] = 999;
        clone.VectorB[0] = 999;
        
        // Assert
        Assert.Equal(1, original.Matrix[0, 0]);
        Assert.Equal(5, original.VectorB[0]);
    }
}
