using System.Data;
using System.Text;
using ExcelDataReader;
using Domain.Application;
using Domain.Persistence.DTOs;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace Application.Services;

public interface IExcelFileService
{
    Task<SummarizedExcelData> PrepareExcelFileForLLMAsync(
        IFormFile file, 
        IProgress<(double ParseProgress, double SaveProgress)>? progress = null, 
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// ExcelFileService is responsible for preparing Excel files for the LLM
/// It reads the Excel file, extracts the data, and calculates summary statistics
/// The data and summary statistics are stored in the database for querying by the LLM model
/// The data is translated into vectors and stored in the database for querying by the LLM model
/// The vectors are compared against the vector that the LLM generates for the question to find the most relevant rows
/// It will use the most relevant rows to answer the question or provide the data
/// </summary>
public class ExcelFileService : IExcelFileService
{
    #region Concurrency Consts
    private const double LOADING_WEIGHT = 0.1;
    private const double SUMMARIZING_WEIGHT = 0.3;
    private const double PROCESSING_ROWS_WEIGHT = 0.6;
    #endregion

    /// <summary>
    /// Prepare the given Excel file for LLM by reading the file and extracting the data and summary statistics
    /// This method reads the Excel file, extracts the data into a list of rows, and calculates summary statistics for the data
    /// This data will be translated into vectors and stored in the database for querying by the LLM model. The vectors will be
    /// compared against the vector that the LLM generates for the question to find the most relevant rows. It will use the most relevant
    /// rows to answer the question or provide the data whatever it may be.
    /// Assuming we're only processing the first sheet
    /// </summary>
    /// <param name="file"></param>
    /// <param name="progress"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SummarizedExcelData> PrepareExcelFileForLLMAsync(
        IFormFile file,
        IProgress<(double ParseProgress, double SaveProgress)>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        progress ??= new Progress<(double, double)>(_ => { });
        var parallelOptions = CreateParallelOptions(cancellationToken);
        progress.Report((0, 0));        
        var table = await LoadExcelTableAsync(file);
        progress.Report((LOADING_WEIGHT, 0));
        if (table.Rows.Count is 0)
        {
            progress.Report((1.0, 0));
            return new()
            {
                Rows = [],
                Summary = await CalculateSummaryStatisticsAsync(table, parallelOptions)
            };
        }
        var columns = GetTableColumns(table);
        var currentProgress = LOADING_WEIGHT;
        var progressQueue = new ConcurrentQueue<double>();
        var scaledProgress = new Progress<(double, double)>(report =>
        {
            var scaledValue = LOADING_WEIGHT + (report.Item1 * PROCESSING_ROWS_WEIGHT);
            progressQueue.Enqueue(scaledValue);
            while (progressQueue.TryPeek(out var nextProgress) && nextProgress > currentProgress)
            {
                if (progressQueue.TryDequeue(out var value))
                {
                    currentProgress = value;
                    progress.Report((currentProgress, 0));
                }
            }
        });
        var rowsTask = ProcessRowsAsync(table, columns, parallelOptions, scaledProgress);
        progress.Report((LOADING_WEIGHT + SUMMARIZING_WEIGHT, 0));
        var summaryTask = CalculateSummaryStatisticsAsync(table, parallelOptions);
        await Task.WhenAll(rowsTask, summaryTask);
        progress.Report((1.0, 0));
        return new()
        {
            FileName = file.FileName,
            Rows = await rowsTask,
            Summary = await summaryTask
        };
    }

    #region Private Excel File Processing Methods
    private static DataColumn[] GetTableColumns(DataTable table) => table.Columns.Cast<DataColumn>().ToArray();

    private static async Task<DataTable> LoadExcelTableAsync(IFormFile file)
    {
        var dataSet = await LoadExcelDataAsync(file);
        var raw = dataSet.Tables.Count > 0 ? dataSet.Tables[0] : null;
        return raw is null ? new DataTable() : NormalizeTable(raw);
    }

    /// <summary>
    /// Turns the raw cell grid into a table with real column names.
    /// Handles the things that vary between real spreadsheets: blank spacer
    /// columns, blank or duplicated header cells, trailing empty rows, and
    /// sheets that have no header row at all.
    /// </summary>
    private static DataTable NormalizeTable(DataTable raw)
    {
        var headerIndex = FindHeaderRowIndex(raw);
        var sourceColumns = GetPopulatedColumnIndexes(raw);
        var normalized = new DataTable();
        if (sourceColumns.Count is 0) return normalized;

        var firstDataRow = headerIndex + 1;
        var names = new List<string>(sourceColumns.Count);
        foreach (var sourceIndex in sourceColumns)
        {
            var header = headerIndex >= 0 ? CellText(raw.Rows[headerIndex][sourceIndex]) : string.Empty;
            names.Add(MakeUniqueColumnName(header, sourceIndex, names));
        }

        for (var i = 0; i < names.Count; i++)
            normalized.Columns.Add(names[i], InferColumnType(raw, sourceColumns[i], firstDataRow));

        for (var rowIndex = firstDataRow; rowIndex < raw.Rows.Count; rowIndex++)
        {
            var source = raw.Rows[rowIndex];
            if (sourceColumns.All(c => string.IsNullOrWhiteSpace(CellText(source[c]))))
                continue;

            var target = normalized.NewRow();
            for (var i = 0; i < sourceColumns.Count; i++)
                target[i] = ConvertCell(source[sourceColumns[i]], normalized.Columns[i].DataType);

            normalized.Rows.Add(target);
        }
        return normalized;
    }

    /// <summary>
    /// The first populated row is the header only when every filled cell is text.
    /// A sheet whose first row already contains numbers or dates has no header,
    /// so -1 is returned and generated column names are used instead.
    /// </summary>
    private static int FindHeaderRowIndex(DataTable raw)
    {
        for (var rowIndex = 0; rowIndex < raw.Rows.Count; rowIndex++)
        {
            var populated = Enumerable
                .Range(0, raw.Columns.Count)
                .Select(c => raw.Rows[rowIndex][c])
                .Where(value => !string.IsNullOrWhiteSpace(CellText(value)))
                .ToArray();

            if (populated.Length is 0) continue;
            return populated.All(value => value is string) ? rowIndex : -1;
        }
        return -1;
    }

    private static List<int> GetPopulatedColumnIndexes(DataTable raw)
    {
        var indexes = new List<int>();
        for (var columnIndex = 0; columnIndex < raw.Columns.Count; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < raw.Rows.Count; rowIndex++)
            {
                if (string.IsNullOrWhiteSpace(CellText(raw.Rows[rowIndex][columnIndex]))) continue;
                indexes.Add(columnIndex);
                break;
            }
        }
        return indexes;
    }

    private static string MakeUniqueColumnName(string header, int columnIndex, List<string> taken)
    {
        var name = header.Trim();
        if (name.Length is 0) name = $"Column {columnIndex + 1}";

        var candidate = name;
        var suffix = 2;
        while (taken.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            candidate = $"{name} {suffix++}";

        return candidate;
    }

    private static Type InferColumnType(DataTable raw, int columnIndex, int firstDataRow)
    {
        var values = new List<object>();
        for (var rowIndex = Math.Max(0, firstDataRow); rowIndex < raw.Rows.Count; rowIndex++)
        {
            var value = raw.Rows[rowIndex][columnIndex];
            if (value is null or DBNull) continue;
            if (value is string text && string.IsNullOrWhiteSpace(text)) continue;
            values.Add(value);
        }

        if (values.Count is 0) return typeof(string);
        if (values.All(v => v is double or int or long or float or decimal)) return typeof(double);
        if (values.All(v => v is DateTime)) return typeof(DateTime);
        return typeof(string);
    }

    private static object ConvertCell(object? value, Type targetType)
    {
        if (value is null or DBNull) return DBNull.Value;
        if (value is string text && string.IsNullOrWhiteSpace(text)) return DBNull.Value;

        if (targetType == typeof(double))
            return value is IConvertible ? Convert.ToDouble(value) : DBNull.Value;
        if (targetType == typeof(DateTime))
            return value is DateTime date ? date : DBNull.Value;

        return CellText(value);
    }

    /// <summary>Renders a cell for text use; dates lose a meaningless midnight time.</summary>
    private static string CellText(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd")
            : date.ToString("yyyy-MM-dd HH:mm:ss"),
        _ => value.ToString() ?? string.Empty
    };

    private static async Task<ConcurrentBag<ConcurrentDictionary<string, object>>> ProcessRowsAsync(
        DataTable table,
        DataColumn[] columns,
        ParallelOptions parallelOptions,
        IProgress<(double ParseProgress, double SaveProgress)>? progress
    )
    {
        var processedRows = 0;
        var lastReportedProgress = 0.0;
        var totalRows = Math.Max(1, table.Rows.Count);
        var rows = new ConcurrentBag<ConcurrentDictionary<string, object>>();
        await Task.Run(() =>
        {
            Parallel.For(
                0, 
                table.Rows.Count, 
                parallelOptions, 
                async i =>
                {
                    rows.Add(await ProcessRowAsync(table.Rows[i], columns, parallelOptions));
                    var currentProcessed = Interlocked.Increment(ref processedRows);
                    var currentProgress = currentProcessed / (double)totalRows;
                    // Only report if progress has increased significantly
                    if (currentProgress - lastReportedProgress >= 0.1)
                    {
                        var oldProgress = Interlocked.Exchange(ref lastReportedProgress, currentProgress);
                        if (currentProgress > oldProgress) // Ensure we only report increasing progress
                            progress?.Report((currentProgress, 0));
                    }
                }
            );
        }, 
        parallelOptions.CancellationToken);
        return rows;
    }

    private static async Task<ConcurrentDictionary<string, object>> ProcessRowAsync(
        DataRow row, 
        DataColumn[] columns, 
        ParallelOptions parallelOptions
    )
    {
        var dict = new ConcurrentDictionary<string, object>(columns.Length, columns.Length);
        await Parallel.ForEachAsync(
            columns,
            parallelOptions,
            async (column, token) =>
            {
                var value = row[column];
                dict[column.ColumnName] = (value is DBNull or null ? null : value) ?? string.Empty;
                await Task.Yield();
            }
        );
        return dict;
    }

    /// <summary>
    /// Load the data from the given Excel file
    /// This method reads the Excel file and returns the data as a DataSet
    /// Utilizes the ExcelDataReader library known for its speed and efficiency
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    private static async Task<DataSet> LoadExcelDataAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        using var reader = ExcelReaderFactory.CreateReader(stream);
        // Read the untouched grid. Header detection happens in NormalizeTable so
        // that blank and duplicate header cells are handled the same way everywhere.
        return await Task.Run(
            () => reader.AsDataSet(
                new ExcelDataSetConfiguration()
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
                }
            )
        );
    }

    /// <summary>
    /// Calculate summary statistics for the given table
    /// This method calculates the sum, average, min, and max for each numeric column in the table.
    /// </summary>
    /// <param name="table"></param>
    /// <param name="parallelOptions"></param>
    /// <returns></returns>
    private static async Task<ConcurrentDictionary<string, object>> CalculateSummaryStatisticsAsync(
        DataTable table,
        ParallelOptions parallelOptions
    )
    {
        var summary = new ExcelFileSummary
        {
            RowCount = table.Rows.Count,
            ColumnCount = table.Columns.Count,
            Columns = table
                .Columns
                .Cast<DataColumn>()
                .Select(c => c.ColumnName)
                .ToList()
        };
        var columns = table
            .Columns
            .Cast<DataColumn>()
            .ToArray();
        await Parallel.ForEachAsync(
            columns,
            parallelOptions,
            async (column, token) =>
            {
                if (IsNumericColumn(column, table))
                    await CalcNumColStatsAsync(
                        table, 
                        column, 
                        summary
                    );
                else if (IsStringColumn(column))
                    await CalcStringColHashesAsync(
                        table, 
                        column, 
                        summary, 
                        token
                    );
            }
        );
        var result = new ConcurrentDictionary<string, object>();
        result["Summary"] = summary;
        return result;
    }

    /// <summary>
    /// Check if the given column is of a numeric type
    /// </summary>
    /// <param name="column"></param>
    /// <param name="table"></param>
    /// <returns></returns>
    private static bool IsNumericColumn(
        DataColumn column, 
        DataTable table
    )
    {
        if (column.DataType == typeof(int) || column.DataType == typeof(double) ||
            column.DataType == typeof(float) || column.DataType == typeof(decimal) ||
            column.DataType == typeof(long))
            return true;

        return table.AsEnumerable()
            .Where(row => row[column] != DBNull.Value)
            .All(row => double.TryParse(row[column].ToString(), out _));
    }

    /// <summary>
    /// Calculate the sum, average, min, and max for each numeric column in the given table
    /// </summary>
    /// <param name="table"></param>
    /// <param name="column"></param>
    /// <param name="summary"></param>
    private static async Task CalcNumColStatsAsync(
        DataTable table, 
        DataColumn column, 
        ExcelFileSummary summary
    )
    {
        var columnName = column.ColumnName;
        var values = table.AsEnumerable()
            .Where(row => row[column] != DBNull.Value && double.TryParse(row[column].ToString(), out _))
            .Select(row => Convert.ToDouble(row[column]))
            .ToArray();

        if (values.Length > 0)
        {
            summary.Sums[columnName] = values.Sum();
            summary.Mins[columnName] = values.Min();
            summary.Maxs[columnName] = values.Max();
            summary.Averages[columnName] = values.Average();
        }
        await Task.Yield();
    }

    private static bool IsStringColumn(DataColumn column) => column.DataType == typeof(string);

    /// <summary>
    /// Calculate the hash values of each string column's values
    /// So for each column that is considered a string column, 
    /// we are taking the whole column and calculating the hash of each value
    /// </summary>
    /// <param name="table"></param>
    /// <param name="column"></param>
    /// <param name="summary"></param>
    private async static Task CalcStringColHashesAsync(
        DataTable table, 
        DataColumn column, 
        ExcelFileSummary summary, 
        CancellationToken cancellationToken = default
    )
    {
        var columnName = column.ColumnName;
        var hashedValues = new ConcurrentDictionary<string, string>();
        var parallelOptions = CreateParallelOptions(cancellationToken);
        await Parallel.ForEachAsync(
            table.AsEnumerable(), 
            parallelOptions, 
            async (row, token) =>
            {
                var value = row[column]?.ToString();
                if (!string.IsNullOrEmpty(value))
                    hashedValues.TryAdd(value, ComputeHash(value));

                await Task.CompletedTask;
            }
        );
        summary.HashedStrings[columnName] = hashedValues;
        await Task.Yield();
    }

    /// <summary>
    /// Compute the SHA256 hash of the given input
    /// Should change it the same way every time
    /// </summary>
    /// <param name="input"></param>
    /// <returns>
    ///     The SHA256 hash of the input
    /// </returns>
    private static string ComputeHash(string input) => string.Concat(SHA256.HashData(Encoding.UTF8.GetBytes(input)).Select(b => b.ToString("x2")));

    private static ParallelOptions CreateParallelOptions(CancellationToken cancellationToken) =>
        new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(-1, (Environment.ProcessorCount - 4) > 0 ? Environment.ProcessorCount - 4 : 1)
        };
    #endregion
}
