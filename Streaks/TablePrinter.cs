using System.Text;

namespace Streaks;

internal enum OutputColor
{
    Default = 1,
    Green,
    Red
}

internal sealed class RowOptions
{
    public OutputColor Color { get; set; } = OutputColor.Default;
}

internal sealed class Table
{
    private readonly int _columnCount;
    private KeyValuePair<RowOptions, string[]> _headers;
    private readonly List<KeyValuePair<RowOptions, string[]>> _rows = new List<KeyValuePair<RowOptions, string[]>>();

    public Table(int columnCount)
    {
        _columnCount = columnCount;
    }

    public void AddHeader(IEnumerable<string> values)
    {
        if (values.Count() != _columnCount)
            throw new ArgumentException("Count mismatch.", nameof(values));

        _headers = new(new RowOptions(), values.ToArray());
    }

    public void AddRow(IEnumerable<string> values, RowOptions rowOptions)
    {
        if (values.Count() != _columnCount)
            throw new ArgumentException("Count mismatch.", nameof(values));

        _rows.Add(new(rowOptions, values.ToArray()));
    }

    public int ColumnCount => _columnCount;
    public List<KeyValuePair<RowOptions, string[]>> RowsIncludingHeader => new KeyValuePair<RowOptions, string[]>[] { _headers }.Concat(_rows).ToList();
}

internal static class TableExtensions
{
    public static void AddHeader(this Table table, params string[] values)
    {
        table.AddHeader(values);
    }

    public static void AddRow(this Table table, params string[] values)
        => AddRow(table, new RowOptions(), values);

    public static void AddRow(this Table table, RowOptions rowOptions, params string[] values)
    {
        table.AddRow(values, rowOptions);
    }

    public static void AddRow(this Table table, params object[] values)
        => AddRow(table, new RowOptions(), values);

    public static void AddRow(this Table table, RowOptions rowOptions, params object[] values)
    {
        table.AddRow(values.Select(x => x.ToString()), rowOptions);
    }

    public static void AddEmptyRow(this Table table)
    {
        table.AddRow(Enumerable.Repeat(string.Empty, table.ColumnCount), new RowOptions());
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
    public Dictionary<int, int> ColumnLeftPadding { get; }
        = new Dictionary<int, int>();
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
            _output.SetColor(row.Key.Color);

            DrawPadding(options.TablePadding);

            var lineBuilder = new StringBuilder();
            for (var i = 0; i < columnSizes.Count; i++)
            {
                lineBuilder.Append(new string(' ', options.ValuePadding));

                if (options.ColumnLeftPadding.ContainsKey(i + 1))
                {
                    var additionalPadding = options.ColumnLeftPadding[i + 1];
                    lineBuilder.Append(new string(' ', additionalPadding));
                }

                lineBuilder.Append(row.Value[i].PadRight(columnSizes[i]));

                lineBuilder.Append(new string(' ', options.ValuePadding));
            }

            _output.Write(lineBuilder.ToString());

            DrawPadding(options.TablePadding);
            _output.WriteLine();

            _output.SetColor(OutputColor.Default);
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
            for (var i = 0; i < rowValues.Value.Length; i++)
            {
                if (rowValues.Value[i].Length > sizes[i])
                    sizes[i] = rowValues.Value[i].Length;
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
    void SetColor(OutputColor color);
}

internal sealed class ConsoleOutput : IOutput
{
    public void Write(string value) => Console.Write(value);
    public void WriteLine() => Console.WriteLine();
    public void SetColor(OutputColor color)
    {
        switch (color)
        {
            case OutputColor.Default:
                Console.ForegroundColor = ConsoleColor.White;
                break;
            case OutputColor.Green:
                Console.ForegroundColor = ConsoleColor.Green;
                break;
            case OutputColor.Red:
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            default:
                throw new NotSupportedException("Unsupported color.");
        }
    }
}

internal static class OutputExtensions
{
    public static void WriteLine(this IOutput output, string value)
    {
        output.Write(value);
        output.WriteLine();
    }
}

internal sealed class LoggingOutputDecorator : IOutput
{
    private readonly IOutput _fileOutput;
    private readonly IOutput _output;

    public LoggingOutputDecorator(
        IOutput fileOutput,
        IOutput output)
    {
        _fileOutput = fileOutput;
        _output = output;
    }

    public void SetColor(OutputColor color)
    {
        _output.SetColor(color);
        _fileOutput.SetColor(color);
    }

    public void Write(string value)
    {
        _output.Write(value);
        _fileOutput.Write($"{DateTimeOffset.Now}\t\t{value}");
    }

    public void WriteLine()
    {
        _output.WriteLine();
        _fileOutput.WriteLine();
    }
}

internal sealed class FileOutput : IOutput
{
    private readonly string _fileName = "output.log";
    private readonly object _lock = new object();
    private OutputColor _previousColor = OutputColor.Default;

    public void SetColor(OutputColor color)
    {
        if (_previousColor != color)
        {
            var content = new StringBuilder(File.ReadAllText(GetFileName()));
            content.Append($"(((COLOR:{color.ToString()})))");
            File.WriteAllText(GetFileName(), content.ToString());

            _previousColor = color;
        }
    }

    public void Write(string value)
    {
        lock (_lock)
        {
            var content = new StringBuilder(File.ReadAllText(GetFileName()));
            content.Append(value);
            File.WriteAllText(GetFileName(), content.ToString());
        }
    }

    public void WriteLine()
    {
        lock (_lock)
        {
            if (!File.Exists(GetFileName()))
                File.WriteAllText(GetFileName(), string.Empty);

            var content = new StringBuilder(File.ReadAllText(GetFileName()));
            content.AppendLine();
            File.WriteAllText(GetFileName(), content.ToString());
        }
    }

    private string GetFileName()
    {
        return $"{_fileName}-{DateTimeOffset.Now.ToString("yyyy-MM-dd-HH")}";
    }
}
