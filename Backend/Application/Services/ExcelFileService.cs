using System.Data;
using System.Text;
using ExcelDataReader;
using Domain.Extensions;
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

    private static async Task<DataTable> LoadExcelTableAsync(IFormFile file) => (await LoadExcelDataAsync(file)).Tables[0] ?? new();

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
        await Task.Run(
            () =>
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
            parallelOptions.CancellationToken
        );
        return rows;
    }

    private static async Task<ConcurrentDictionary<string, object>> ProcessRowAsync(
        DataRow row, 
        DataColumn[] columns, 
        ParallelOptions parallelOptions
    )
    {
        var dict = new ConcurrentDictionary<string, object>(columns.Length, columns.Length);
        await columns.ForEachAsync(
            cancellationToken: parallelOptions.CancellationToken,
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
        return await Task.Run(
            () => reader.AsDataSet(
                new ExcelDataSetConfiguration()
                {
                    ConfigureDataTable = 
                        _ => new ExcelDataTableConfiguration()
                        {
                            UseHeaderRow = true,
                            ReadHeaderRow = rowReader =>
                            {
                                var fieldCount = rowReader.FieldCount;
                                var columnNames = new string[fieldCount];
                                for (int i = 0; i < fieldCount; i++) 
                                {
                                    columnNames[i] = rowReader.GetValue(i)?.ToString() ?? $"Column{i + 1}";
                                }
                            }
                        }
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
        await columns.ForEachAsync(
            cancellationToken: parallelOptions.CancellationToken,
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
        await table.AsEnumerable().ForEachAsync(
            cancellationToken: parallelOptions.CancellationToken, 
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
            MaxDegreeOfParallelism = Math.Max(-1, Environment.ProcessorCount - 4)
        };
    #endregion
}
