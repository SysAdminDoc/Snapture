using System.Drawing;
using System.IO;
using System.Text.Json;

namespace Snapture.App.Services;

public enum PinBoardLayoutKind
{
    Vertical,
    Horizontal,
    Grid
}

public sealed record PinBoardLayoutOptions(
    PinBoardLayoutKind Layout = PinBoardLayoutKind.Grid,
    int Gap = 16,
    int GridColumns = 2)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Layout))
            throw new ArgumentOutOfRangeException(nameof(Layout));
        if (Gap is < 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(Gap));
        if (GridColumns is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(GridColumns));
    }
}

public sealed record PinBoardPlacement(int Index, Rectangle Bounds);

public sealed record PinBoardArrangement(
    int Width,
    int Height,
    IReadOnlyList<PinBoardPlacement> Placements);

/// <summary>Calculates bounded snap layouts for a set of pinned image dimensions.</summary>
public static class PinBoardLayoutService
{
    private const long MaxBoardPixels = 100_000_000;

    public static PinBoardArrangement Arrange(
        IReadOnlyList<Size> imageSizes,
        PinBoardLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(imageSizes);
        options.Validate();
        if (imageSizes.Count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(imageSizes), "A board supports between 1 and 100 pins.");
        if (imageSizes.Any(size => size.Width < 1 || size.Height < 1))
            throw new ArgumentException("Every pin must have positive dimensions.", nameof(imageSizes));

        return options.Layout switch
        {
            PinBoardLayoutKind.Vertical => ArrangeVertical(imageSizes, options.Gap),
            PinBoardLayoutKind.Horizontal => ArrangeHorizontal(imageSizes, options.Gap),
            PinBoardLayoutKind.Grid => ArrangeGrid(imageSizes, options.Gap, options.GridColumns),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Layout))
        };
    }

    private static PinBoardArrangement ArrangeVertical(IReadOnlyList<Size> sizes, int gap)
    {
        int width = sizes.Max(size => size.Width);
        int height = CheckedDimension(sizes.Sum(size => (long)size.Height) + (long)gap * (sizes.Count - 1));
        EnsureBoard(width, height);
        int y = 0;
        var placements = new List<PinBoardPlacement>(sizes.Count);
        for (int index = 0; index < sizes.Count; index++)
        {
            var size = sizes[index];
            placements.Add(new PinBoardPlacement(index, new Rectangle((width - size.Width) / 2, y, size.Width, size.Height)));
            y = checked(y + size.Height + gap);
        }
        return new PinBoardArrangement(width, height, placements);
    }

    private static PinBoardArrangement ArrangeHorizontal(IReadOnlyList<Size> sizes, int gap)
    {
        int width = CheckedDimension(sizes.Sum(size => (long)size.Width) + (long)gap * (sizes.Count - 1));
        int height = sizes.Max(size => size.Height);
        EnsureBoard(width, height);
        int x = 0;
        var placements = new List<PinBoardPlacement>(sizes.Count);
        for (int index = 0; index < sizes.Count; index++)
        {
            var size = sizes[index];
            placements.Add(new PinBoardPlacement(index, new Rectangle(x, (height - size.Height) / 2, size.Width, size.Height)));
            x = checked(x + size.Width + gap);
        }
        return new PinBoardArrangement(width, height, placements);
    }

    private static PinBoardArrangement ArrangeGrid(IReadOnlyList<Size> sizes, int gap, int requestedColumns)
    {
        int columns = Math.Min(requestedColumns, sizes.Count);
        int rows = (sizes.Count + columns - 1) / columns;
        var columnWidths = new int[columns];
        var rowHeights = new int[rows];
        for (int index = 0; index < sizes.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            columnWidths[column] = Math.Max(columnWidths[column], sizes[index].Width);
            rowHeights[row] = Math.Max(rowHeights[row], sizes[index].Height);
        }
        int width = CheckedDimension(columnWidths.Sum(value => (long)value) + (long)gap * (columns - 1));
        int height = CheckedDimension(rowHeights.Sum(value => (long)value) + (long)gap * (rows - 1));
        EnsureBoard(width, height);
        var xOffsets = PrefixOffsets(columnWidths, gap);
        var yOffsets = PrefixOffsets(rowHeights, gap);
        var placements = new List<PinBoardPlacement>(sizes.Count);
        for (int index = 0; index < sizes.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            var size = sizes[index];
            placements.Add(new PinBoardPlacement(index, new Rectangle(
                xOffsets[column] + (columnWidths[column] - size.Width) / 2,
                yOffsets[row] + (rowHeights[row] - size.Height) / 2,
                size.Width,
                size.Height)));
        }
        return new PinBoardArrangement(width, height, placements);
    }

    private static int[] PrefixOffsets(IReadOnlyList<int> sizes, int gap)
    {
        var offsets = new int[sizes.Count];
        for (int index = 1; index < sizes.Count; index++)
            offsets[index] = checked(offsets[index - 1] + sizes[index - 1] + gap);
        return offsets;
    }

    private static int CheckedDimension(long value) => value is < 1 or > int.MaxValue
        ? throw new InvalidDataException("The board dimensions are too large.")
        : (int)value;

    private static void EnsureBoard(int width, int height)
    {
        if ((long)width * height > MaxBoardPixels)
            throw new InvalidDataException("The board exceeds the 100 million pixel safety limit.");
    }
}

public sealed record PinBoardSavedLayout(string Name, PinBoardLayoutOptions Options);

/// <summary>Persists small named board-layout presets without storing image pixels.</summary>
public sealed class PinBoardLayoutStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PinBoardLayoutStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _path = Path.Combine(Path.GetFullPath(dataDirectory), "pin-boards", "layouts.json");
    }

    public IReadOnlyList<PinBoardSavedLayout> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return Array.Empty<PinBoardSavedLayout>();
            var layouts = JsonSerializer.Deserialize<List<PinBoardSavedLayout>>(File.ReadAllText(_path), _jsonOptions);
            return layouts?
                .Where(layout => !string.IsNullOrWhiteSpace(layout.Name))
                .Select(layout => layout with { Name = layout.Name.Trim() })
                .ToArray()
                ?? Array.Empty<PinBoardSavedLayout>();
        }
        catch (JsonException) { return Array.Empty<PinBoardSavedLayout>(); }
        catch (IOException) { return Array.Empty<PinBoardSavedLayout>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<PinBoardSavedLayout>(); }
    }

    public void Save(string name, PinBoardLayoutOptions options)
    {
        string normalizedName = NormalizeName(name);
        options.Validate();
        var layouts = Load().ToList();
        layouts.RemoveAll(layout => string.Equals(layout.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        layouts.Add(new PinBoardSavedLayout(normalizedName, options));
        layouts = layouts.OrderBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(layouts, _jsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    private static string NormalizeName(string name)
    {
        string normalized = name.Trim();
        if (normalized.Length is < 1 or > 80)
            throw new ArgumentException("Layout names must contain 1 to 80 characters.", nameof(name));
        return normalized;
    }
}
