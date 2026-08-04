using SkiaSharp;

namespace Snapture.App.Services;

/// <summary>A reconstructed OCR table cell with image-space bounds.</summary>
public sealed record OcrTableCell(string Text, SKRect BoundingBox);

/// <summary>A reconstructed OCR table row.</summary>
public sealed record OcrTableRow(IReadOnlyList<OcrTableCell> Cells);

/// <summary>A table reconstructed from positioned OCR word geometry.</summary>
public sealed record OcrTableResult(
    IReadOnlyList<OcrTableRow> Rows,
    int ColumnCount,
    OcrEngineKind Engine)
{
    public bool IsEmpty => Rows.Count == 0 || ColumnCount == 0;
}

/// <summary>Reconstructs rows and columns from normalized OCR word boxes.</summary>
public static class OcrTableBuilder
{
    public static OcrTableResult Build(OcrRecognitionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = result.Lines
            .Select((line, index) => CreateRowCandidate(line, index))
            .Where(row => row.Cells.Count > 0)
            .OrderBy(row => row.Top)
            .ThenBy(row => row.Index)
            .ToList();

        if (rows.Count == 0)
            return new OcrTableResult(Array.Empty<OcrTableRow>(), 0, result.Engine);

        var medianWordHeight = Median(rows.SelectMany(row => row.Words).Select(word => MathF.Max(1, word.BoundingBox.Height)));
        var columnTolerance = MathF.Max(12, medianWordHeight * 2.0f);
        var columns = ClusterColumns(rows.SelectMany(row => row.Cells), columnTolerance);
        var tableRows = rows
            .Select(row => AssignColumns(row.Cells, columns))
            .Select(cells => new OcrTableRow(cells))
            .ToArray();

        return new OcrTableResult(tableRows, columns.Count, result.Engine);
    }

    public static string ToTsv(OcrTableResult table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return string.Join(
            Environment.NewLine,
            table.Rows.Select(row => string.Join('\t', row.Cells.Select(cell =>
                cell.Text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')))));
    }

    private static RowCandidate CreateRowCandidate(OcrLineResult line, int index)
    {
        var words = line.Words
            .Where(word => !string.IsNullOrWhiteSpace(word.Text) && !word.BoundingBox.IsEmpty)
            .OrderBy(word => word.BoundingBox.Left)
            .ThenBy(word => word.BoundingBox.Top)
            .ToArray();
        if (words.Length == 0)
            return new RowCandidate(index, float.MaxValue, Array.Empty<OcrWordResult>(), Array.Empty<CellCandidate>());

        var gapTolerance = MathF.Max(4, Median(words.Select(word => MathF.Max(1, word.BoundingBox.Height))) * 0.75f);
        var cells = new List<CellCandidate>();
        var currentWords = new List<OcrWordResult>();
        foreach (var word in words)
        {
            if (currentWords.Count > 0)
            {
                var previous = currentWords[^1];
                var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
                if (gap > gapTolerance)
                {
                    cells.Add(CreateCell(currentWords));
                    currentWords.Clear();
                }
            }
            currentWords.Add(word);
        }
        if (currentWords.Count > 0) cells.Add(CreateCell(currentWords));

        return new RowCandidate(index, words.Min(word => word.BoundingBox.Top), words, cells);
    }

    private static CellCandidate CreateCell(IReadOnlyList<OcrWordResult> words)
    {
        var text = string.Join(' ', words.Select(word => word.Text.Trim()).Where(text => text.Length > 0));
        var left = words.Min(word => word.BoundingBox.Left);
        var top = words.Min(word => word.BoundingBox.Top);
        var right = words.Max(word => word.BoundingBox.Right);
        var bottom = words.Max(word => word.BoundingBox.Bottom);
        return new CellCandidate(text, new SKRect(left, top, right, bottom));
    }

    private static List<ColumnCluster> ClusterColumns(IEnumerable<CellCandidate> cells, float tolerance)
    {
        var columns = new List<ColumnCluster>();
        foreach (var cell in cells.OrderBy(cell => cell.BoundingBox.Left))
        {
            var nearest = columns
                .Select((column, index) => (column, index, distance: MathF.Abs(column.Anchor - cell.BoundingBox.Left)))
                .Where(candidate => candidate.distance <= tolerance)
                .OrderBy(candidate => candidate.distance)
                .FirstOrDefault();
            if (nearest.column is null)
            {
                columns.Add(new ColumnCluster(cell.BoundingBox.Left));
                continue;
            }

            nearest.column.Add(cell.BoundingBox.Left);
        }

        columns.Sort((left, right) => left.Anchor.CompareTo(right.Anchor));
        return columns;
    }

    private static IReadOnlyList<OcrTableCell> AssignColumns(
        IReadOnlyList<CellCandidate> sourceCells,
        IReadOnlyList<ColumnCluster> columns)
    {
        var cells = Enumerable.Range(0, columns.Count)
            .Select(_ => new OcrTableCell(string.Empty, SKRect.Empty))
            .ToArray();
        foreach (var source in sourceCells)
        {
            var column = Enumerable.Range(0, columns.Count)
                .OrderBy(index => MathF.Abs(columns[index].Anchor - source.BoundingBox.Left))
                .First();
            var existing = cells[column];
            var text = existing.Text.Length == 0 ? source.Text : $"{existing.Text} {source.Text}";
            cells[column] = new OcrTableCell(text, Union(existing.BoundingBox, source.BoundingBox));
        }
        return cells;
    }

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return 1;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static SKRect Union(SKRect left, SKRect right)
    {
        if (left.IsEmpty) return right;
        if (right.IsEmpty) return left;
        return new SKRect(
            MathF.Min(left.Left, right.Left),
            MathF.Min(left.Top, right.Top),
            MathF.Max(left.Right, right.Right),
            MathF.Max(left.Bottom, right.Bottom));
    }

    private sealed record RowCandidate(
        int Index,
        float Top,
        IReadOnlyList<OcrWordResult> Words,
        IReadOnlyList<CellCandidate> Cells);

    private sealed record CellCandidate(string Text, SKRect BoundingBox);

    private sealed class ColumnCluster(float initialAnchor)
    {
        private readonly List<float> _anchors = new() { initialAnchor };

        public float Anchor => _anchors.Average();

        public void Add(float anchor) => _anchors.Add(anchor);
    }
}

