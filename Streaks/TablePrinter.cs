using System.Text;
using System.Windows.Markup;

namespace Streaks;

internal sealed class Table
{
    private readonly int _columnCount;
    private string[] _headers;
    private readonly List<string[]> _rows = new List<string[]>();

    public Table(int columnCount)
    {
        _columnCount = columnCount;
    }

    public void AddHeader(IEnumerable<string> values)
    {
        if (values.Count() != _columnCount)
            throw new ArgumentException("Count mismatch.", nameof(values));

        _headers = values.ToArray();
    }

    public void AddRow(IEnumerable<string> values)
    {
        if (values.Count() != _columnCount)
            throw new ArgumentException("Count mismatch.", nameof(values));

        _rows.Add(values.ToArray());
    }

    public int ColumnCount => _columnCount;
    public string[] OnlyHeaderRow => _headers;
    public List<string[]> RowsIncludingHeader => new[] { _headers }.Concat(_rows).ToList();
    public List<string[]> OnlyValueRows => _rows;
}

internal static class TableExtensions
{
    public static void AddHeader(this Table table, params string[] values)
    {
        table.AddHeader(values);
    }

    public static void AddRow(this Table table, params string[] values)
    {
        table.AddRow(values);
    }

    public static void AddRow(this Table table, params object[] values)
    {
        table.AddRow(values.Select(x => x.ToString()));
    }

    public static void AddEmptyRow(this Table table)
    {
        table.AddRow(Enumerable.Repeat(string.Empty, table.ColumnCount));
    }
}

internal interface ITablePrinter
{
    void Print(Table table, TablePrinterOptions options);
}

internal sealed class TablePrinterOptions
{
    public int TableMargin { get; set; } = 1;
    public int TablePadding { get; set; } = 1;
    public int ValuePadding { get; set; } = 1;
    public bool Striped { get; set; }
}

internal sealed class TablePrinter : ITablePrinter
{
    private readonly IOutput _output;

    public TablePrinter(IOutput output)
    {
        _output = output;
    }

    public void Print(Table table, TablePrinterOptions options)
    {
        DrawMargin(options.TableMargin);

        var columnSizes = DetectColumnSizes(table);

        foreach (var row in table.RowsIncludingHeader)
        {
            DrawPadding(options.TablePadding);

            var lineBuilder = new StringBuilder();
            for (var i = 0; i < columnSizes.Count; i++)
            {
                lineBuilder.Append(new string(' ', options.ValuePadding));

                lineBuilder.Append(row[i].PadRight(columnSizes[i]));

                lineBuilder.Append(new string(' ', options.ValuePadding));
            }

            _output.Write(lineBuilder.ToString());

            DrawPadding(options.TablePadding);
            _output.WriteLine();
        }

        DrawMargin(options.TableMargin);
    }

    private List<int> DetectColumnSizes(Table table)
    {
        var sizes = new List<int>(table.ColumnCount);
        sizes.AddRange(Enumerable.Repeat(0, table.ColumnCount));

        var allRows = table.RowsIncludingHeader;

        foreach (var rowValues in allRows)
        {
            for (var i = 0; i < rowValues.Length; i++)
            {
                if (rowValues[i].Length > sizes[i])
                    sizes[i] = rowValues[i].Length;
            }
        }

        return sizes;
    }

    private void DrawMargin(int size)
    {
        for (var i = 0; i < size; i++)
        {
            _output.WriteLine();
        }
    }

    private void DrawPadding(int size)
    {
        _output.Write(new string(' ', size));
    }
}

internal interface IOutput
{
    void Write(string value);
    void WriteLine();
}

internal sealed class ConsoleOutput : IOutput
{
    public void Write(string value) => Console.Write(value);
    public void WriteLine() => Console.WriteLine();
}

internal static class OutputExtensions
{
    public static void WriteLine(this IOutput output, string value)
    {
        output.Write(value);
        output.WriteLine();
    }
}
