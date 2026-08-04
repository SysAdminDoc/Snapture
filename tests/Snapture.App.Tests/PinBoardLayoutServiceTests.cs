using System.Drawing;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PinBoardLayoutServiceTests
{
    [TestMethod]
    public void ArrangesPinsVerticallyHorizontallyAndAsGrid()
    {
        var sizes = new[] { new Size(20, 10), new Size(10, 30), new Size(15, 15) };

        var vertical = PinBoardLayoutService.Arrange(sizes, new PinBoardLayoutOptions(PinBoardLayoutKind.Vertical, 2));
        Assert.AreEqual(20, vertical.Width);
        Assert.AreEqual(59, vertical.Height);
        Assert.AreEqual(new Rectangle(0, 0, 20, 10), vertical.Placements[0].Bounds);

        var horizontal = PinBoardLayoutService.Arrange(sizes, new PinBoardLayoutOptions(PinBoardLayoutKind.Horizontal, 2));
        Assert.AreEqual(49, horizontal.Width);
        Assert.AreEqual(30, horizontal.Height);

        var grid = PinBoardLayoutService.Arrange(sizes, new PinBoardLayoutOptions(PinBoardLayoutKind.Grid, 2, 2));
        Assert.AreEqual(32, grid.Width);
        Assert.AreEqual(47, grid.Height);
        Assert.HasCount(3, grid.Placements);
    }

    [TestMethod]
    public void SavesAndReloadsNamedLayoutPresetsWithoutPixels()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.PinBoardTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PinBoardLayoutStore(root);
            var options = new PinBoardLayoutOptions(PinBoardLayoutKind.Horizontal, 24, 3);
            store.Save("Release board", options);

            var loaded = store.Load();

            Assert.HasCount(1, loaded);
            Assert.AreEqual("Release board", loaded[0].Name);
            Assert.AreEqual(options, loaded[0].Options);
            Assert.IsTrue(File.Exists(Path.Combine(root, "pin-boards", "layouts.json")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
