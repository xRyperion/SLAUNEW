namespace DistributedSLAU.Common;

/// <summary>
/// Разбиение матрицы на клетки (блоки) для распределённой обработки
/// </summary>
public static class MatrixPartitioner
{
    /// <summary>
    /// Разбивает матрицу на квадратные клетки (блоковое разбиение)
    /// </summary>
    /// <param name="system">Исходная СЛАУ</param>
    /// <param name="gridSize">Размер сетки (gridSize x gridSize блоков)</param>
    /// <returns>Двумерный массив блоков</returns>
    public static MatrixBlock[,] PartitionIntoBlocks(LinearSystem system, int gridSize)
    {
        int n = system.Size;
        int blockSize = (n + gridSize - 1) / gridSize; // округление вверх

        var blocks = new MatrixBlock[gridSize, gridSize];

        for (int blockRow = 0; blockRow < gridSize; blockRow++)
        {
            for (int blockCol = 0; blockCol < gridSize; blockCol++)
            {
                int startRow = blockRow * blockSize;
                int startCol = blockCol * blockSize;
                int rowCount = Math.Min(blockSize, n - startRow);
                int colCount = Math.Min(blockSize, n - startCol);

                if (rowCount <= 0 || colCount <= 0) continue;

                var block = new MatrixBlock(startRow, startCol, rowCount, colCount, n);

                // Копируем данные из глобальной матрицы в блок
                for (int i = 0; i < rowCount; i++)
                {
                    for (int j = 0; j < colCount; j++)
                    {
                        block.Data[i, j] = system.Matrix[startRow + i, startCol + j];
                    }
                    block.LocalB[i] = system.VectorB[startRow + i];
                }

                blocks[blockRow, blockCol] = block;
            }
        }

        return blocks;
    }

    /// <summary>
    /// Разбивает матрицу на горизонтальные полосы (для сравнения)
    /// </summary>
    public static MatrixBlock[] PartitionByRows(LinearSystem system, int nodeCount)
    {
        int n = system.Size;
        var blocks = new List<MatrixBlock>();

        int rowsPerNode = n / nodeCount;
        int remainder = n % nodeCount;

        int currentRow = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            int rowsForNode = rowsPerNode + (i < remainder ? 1 : 0);
            if (rowsForNode <= 0) continue;

            var block = new MatrixBlock(currentRow, 0, rowsForNode, n, n);

            for (int r = 0; r < rowsForNode; r++)
            {
                int globalRow = currentRow + r;
                for (int col = 0; col < n; col++)
                {
                    block.Data[r, col] = system.Matrix[globalRow, col];
                }
                block.LocalB[r] = system.VectorB[globalRow];
            }

            blocks.Add(block);
            currentRow += rowsForNode;
        }

        return blocks.ToArray();
    }

    /// <summary>
    /// Собирает решение из блоков
    /// </summary>
    public static double[] GatherSolution(MatrixBlock[,] blocks, int gridSize)
    {
        int n = blocks[0, 0].GlobalSize;
        var solution = new double[n];

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                if (blocks[i, j] == null) continue;
                // Решение собирается из диагональных блоков
                if (i == j)
                {
                    var block = blocks[i, j];
                    for (int r = 0; r < block.RowCount; r++)
                    {
                        // Будет перезаписано при получении решения
                    }
                }
            }
        }

        return solution;
    }

    /// <summary>
    /// Собирает решение из словаря частичных результатов
    /// </summary>
    public static double[] GatherSolutionFromParts(Dictionary<int, double[]> partialResults, int totalSize)
    {
        var solution = new double[totalSize];

        foreach (var kvp in partialResults)
        {
            int startRow = kvp.Key;
            double[] localSolution = kvp.Value;

            for (int i = 0; i < localSolution.Length; i++)
            {
                solution[startRow + i] = localSolution[i];
            }
        }

        return solution;
    }
}
