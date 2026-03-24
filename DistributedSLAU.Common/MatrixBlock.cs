using MessagePack;

namespace DistributedSLAU.Common;

/// <summary>
/// Блок матрицы (клетка) для распределённой обработки.
/// Содержит подматрицу и соответствующую часть вектора свободных членов.
/// </summary>
[MessagePackObject]
public class MatrixBlock
{
    /// <summary>
    /// Номер начальной строки блока в глобальной матрице
    /// </summary>
    [Key(0)]
    public int StartRow { get; set; }

    /// <summary>
    /// Номер начального столбца блока в глобальной матрице
    /// </summary>
    [Key(1)]
    public int StartCol { get; set; }

    /// <summary>
    /// Количество строк в блоке
    /// </summary>
    [Key(2)]
    public int RowCount { get; set; }

    /// <summary>
    /// Количество столбцов в блоке
    /// </summary>
    [Key(3)]
    public int ColCount { get; set; }

    /// <summary>
    /// Данные блока (подматрица)
    /// </summary>
    [Key(4)]
    public double[,] Data { get; set; } = new double[0, 0];

    /// <summary>
    /// Часть вектора свободных членов для этого блока
    /// </summary>
    [Key(5)]
    public double[] LocalB { get; set; } = new double[0];

    /// <summary>
    /// Полный размер глобальной системы
    /// </summary>
    [Key(6)]
    public int GlobalSize { get; set; }

    public MatrixBlock() { }

    public MatrixBlock(int startRow, int startCol, int rowCount, int colCount, int globalSize)
    {
        StartRow = startRow;
        StartCol = startCol;
        RowCount = rowCount;
        ColCount = colCount;
        GlobalSize = globalSize;
        Data = new double[rowCount, colCount];
        LocalB = new double[rowCount];
    }

    /// <summary>
    /// Получить элемент блока по локальным индексам
    /// </summary>
    public double GetElement(int localRow, int localCol) => Data[localRow, localCol];

    /// <summary>
    /// Установить элемент блока по локальным индексам
    /// </summary>
    public void SetElement(int localRow, int localCol, double value) => Data[localRow, localCol] = value;

    /// <summary>
    /// Получить глобальный индекс строки
    /// </summary>
    public int ToGlobalRow(int localRow) => StartRow + localRow;

    /// <summary>
    /// Получить глобальный индекс столбца
    /// </summary>
    public int ToGlobalCol(int localCol) => StartCol + localCol;
}
