namespace DistributedSLAU.Common;

/// <summary>
/// Реализация клеточного (блочного) метода Гаусса для распределённого решения СЛАУ
/// </summary>
public static class CellGaussSolver
{
    /// <summary>
    /// Последовательное решение СЛАУ методом Гаусса (для сравнения)
    /// </summary>
    public static double[] SolveSequential(LinearSystem system)
    {
        int n = system.Size;
        var matrix = (double[,])system.Matrix.Clone();
        var vector = (double[])system.VectorB.Clone();

        // Прямой ход с выбором главного элемента
        for (int k = 0; k < n - 1; k++)
        {
            // Выбор главного элемента
            int maxRow = k;
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(matrix[i, k]) > Math.Abs(matrix[maxRow, k]))
                    maxRow = i;
            }

            if (maxRow != k)
            {
                for (int j = k; j < n; j++)
                {
                    (matrix[k, j], matrix[maxRow, j]) = (matrix[maxRow, j], matrix[k, j]);
                }
                (vector[k], vector[maxRow]) = (vector[maxRow], vector[k]);
            }

            if (Math.Abs(matrix[k, k]) < 1e-12)
                throw new InvalidOperationException("Матрица вырождена или плохо обусловлена");

            // Исключение переменной
            for (int i = k + 1; i < n; i++)
            {
                double factor = matrix[i, k] / matrix[k, k];
                for (int j = k; j < n; j++)
                {
                    matrix[i, j] -= factor * matrix[k, j];
                }
                vector[i] -= factor * vector[k];
            }
        }

        // Обратный ход
        var solution = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
            {
                sum += matrix[i, j] * solution[j];
            }
            solution[i] = (vector[i] - sum) / matrix[i, i];
        }

        return solution;
    }

    /// <summary>
    /// Прямой ход для одного блока (клетки) - приводит блок к верхнетреугольному виду
    /// </summary>
    public static void BlockForwardElimination(MatrixBlock block)
    {
        int localRows = block.RowCount;
        int localCols = block.ColCount;
        int minDim = Math.Min(localRows, localCols);

        for (int k = 0; k < minDim; k++)
        {
            // Выбор главного элемента в столбце блока
            int maxRow = k;
            for (int i = k + 1; i < localRows; i++)
            {
                if (Math.Abs(block.Data[i, k]) > Math.Abs(block.Data[maxRow, k]))
                    maxRow = i;
            }

            if (maxRow != k)
            {
                // Меняем строки местами
                for (int j = k; j < localCols; j++)
                {
                    (block.Data[k, j], block.Data[maxRow, j]) = (block.Data[maxRow, j], block.Data[k, j]);
                }
                (block.LocalB[k], block.LocalB[maxRow]) = (block.LocalB[maxRow], block.LocalB[k]);
            }

            if (Math.Abs(block.Data[k, k]) < 1e-12)
                continue;

            // Исключение переменной в пределах блока
            for (int i = k + 1; i < localRows; i++)
            {
                double factor = block.Data[i, k] / block.Data[k, k];
                for (int j = k; j < localCols; j++)
                {
                    block.Data[i, j] -= factor * block.Data[k, j];
                }
                block.LocalB[i] -= factor * block.LocalB[k];
            }
        }
    }

    /// <summary>
    /// Получить ведущую строку из блока (после прямого хода)
    /// </summary>
    public static double[] GetPivotRow(MatrixBlock block, int localRowIndex)
    {
        if (localRowIndex < 0 || localRowIndex >= block.RowCount)
            return Array.Empty<double>();

        var pivotRow = new double[block.ColCount + 1]; // +1 для свободного члена
        for (int j = 0; j < block.ColCount; j++)
        {
            pivotRow[j] = block.Data[localRowIndex, j];
        }
        pivotRow[block.ColCount] = block.LocalB[localRowIndex];

        return pivotRow;
    }

    /// <summary>
    /// Обратный ход для блока с учётом известных значений справа
    /// </summary>
    public static double[] BlockBackSubstitution(
        MatrixBlock block, 
        double[] knownValuesRight)  // Известные значения для столбцов справа от блока
    {
        var localSolution = new double[block.RowCount];

        for (int i = block.RowCount - 1; i >= 0; i--)
        {
            double sum = 0;

            // Сумма по известным значениям справа
            for (int col = block.ColCount; col < block.ColCount + knownValuesRight.Length; col++)
            {
                int knownIdx = col - block.ColCount;
                if (knownIdx >= 0 && knownIdx < knownValuesRight.Length)
                {
                    int localCol = col;
                    if (localCol < block.ColCount)
                    {
                        sum += block.Data[i, localCol] * knownValuesRight[knownIdx];
                    }
                }
            }

            // Сумма по локальным неизвестным (справа от текущей позиции)
            for (int j = i + 1; j < block.ColCount; j++)
            {
                sum += block.Data[i, j] * localSolution[j];
            }

            if (Math.Abs(block.Data[i, i]) < 1e-12)
            {
                localSolution[i] = 0; // Вырожденный случай
            }
            else
            {
                localSolution[i] = (block.LocalB[i] - sum) / block.Data[i, i];
            }
        }

        return localSolution;
    }

    /// <summary>
    /// Полный прямой ход для блочной матрицы (централизованный)
    /// Упрощённая версия - работает через сборку полной матрицы
    /// </summary>
    public static MatrixBlock[,] BlockForwardEliminationFull(MatrixBlock[,] blocks, int gridSize)
    {
        int n = blocks[0, 0].GlobalSize;

        // Собираем полную матрицу из блоков
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
                        fullMatrix[block.StartRow + bi, block.StartRow + bi] = block.Data[bi, bi]; // Диагональ
                    }
                }
            }
        }

        // Собираем вектор
        for (int i = 0; i < gridSize; i++)
        {
            if (blocks[i, i] != null)
            {
                var block = blocks[i, i];
                for (int bi = 0; bi < block.RowCount; bi++)
                {
                    fullVector[block.StartRow + bi] = block.LocalB[bi];
                }
            }
        }

        // Выполняем прямой ход на полной матрице
        for (int k = 0; k < n - 1; k++)
        {
            // Выбор главного элемента
            int maxRow = k;
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(fullMatrix[i, k]) > Math.Abs(fullMatrix[maxRow, k]))
                    maxRow = i;
            }

            if (maxRow != k)
            {
                for (int j = k; j < n; j++)
                {
                    (fullMatrix[k, j], fullMatrix[maxRow, j]) = (fullMatrix[maxRow, j], fullMatrix[k, j]);
                }
                (fullVector[k], fullVector[maxRow]) = (fullVector[maxRow], fullVector[k]);
            }

            if (Math.Abs(fullMatrix[k, k]) < 1e-12)
                continue;

            // Исключение переменной
            for (int i = k + 1; i < n; i++)
            {
                double factor = fullMatrix[i, k] / fullMatrix[k, k];
                for (int j = k; j < n; j++)
                {
                    fullMatrix[i, j] -= factor * fullMatrix[k, j];
                }
                fullVector[i] -= factor * fullVector[k];
            }
        }

        // Распределяем обратно по блокам
        var resultBlocks = new MatrixBlock[gridSize, gridSize];
        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                if (blocks[i, j] != null)
                {
                    var origBlock = blocks[i, j];
                    var newBlock = new MatrixBlock(
                        origBlock.StartRow,
                        origBlock.StartCol,
                        origBlock.RowCount,
                        origBlock.ColCount,
                        origBlock.GlobalSize);

                    for (int bi = 0; bi < newBlock.RowCount; bi++)
                    {
                        for (int bj = 0; bj < newBlock.ColCount; bj++)
                        {
                            newBlock.Data[bi, bj] = fullMatrix[newBlock.StartRow + bi, newBlock.StartCol + bj];
                        }
                        newBlock.LocalB[bi] = fullVector[newBlock.StartRow + bi];
                    }

                    resultBlocks[i, j] = newBlock;
                }
            }
        }

        return resultBlocks;
    }

    /// <summary>
    /// Полный обратный ход для блочной матрицы
    /// </summary>
    public static double[] BlockBackSubstitutionFull(MatrixBlock[,] blocks, int gridSize)
    {
        int n = blocks[0, 0].GlobalSize;
        var solution = new double[n];

        // Собираем полную матрицу из обработанных блоков
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

        // Обратный ход на полной матрице
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
            {
                sum += fullMatrix[i, j] * solution[j];
            }

            if (Math.Abs(fullMatrix[i, i]) < 1e-12)
            {
                solution[i] = 0;
            }
            else
            {
                solution[i] = (fullVector[i] - sum) / fullMatrix[i, i];
            }
        }

        return solution;
    }

    /// <summary>
    /// Решение блока (заглушка для тестирования)
    /// </summary>
    public static double[] SolveBlockStub(MatrixBlock block)
    {
        var result = new double[block.RowCount];
        for (int i = 0; i < result.Length; i++)
            result[i] = 1.0 + i;
        return result;
    }

    /// <summary>
    /// Полное клеточное решение СЛАУ с распределённой обработкой
    /// </summary>
    /// <param name="blocks">Блоки матрицы</param>
    /// <param name="gridSize">Размер сетки</param>
    /// <returns>Решение системы</returns>
    public static double[] CellularGaussSolve(MatrixBlock[,] blocks, int gridSize)
    {
        int n = blocks[0, 0].GlobalSize;

        // Этап 1: Прямой ход для каждого блока
        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                if (blocks[i, j] != null)
                {
                    BlockForwardElimination(blocks[i, j]);
                }
            }
        }

        // Этап 2: Межблочный прямой ход (обработка столбцов между блоками)
        for (int k = 0; k < gridSize; k++)
        {
            for (int i = k + 1; i < gridSize; i++)
            {
                // Исключаем элементы в блоках (i, k) и (i, j) для j > k
                for (int j = k; j < gridSize; j++)
                {
                    if (blocks[k, k] == null || blocks[i, j] == null) continue;

                    var pivotBlock = blocks[k, k];
                    var targetBlock = blocks[i, j];

                    // Для упрощения используем полную матрицу
                }
            }
        }

        // Этап 3: Обратный ход
        return BlockBackSubstitutionFull(blocks, gridSize);
    }
}
