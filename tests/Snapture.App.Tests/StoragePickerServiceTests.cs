using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class StoragePickerServiceTests
{
    [TestMethod]
    public void NormalizeExtensionsProducesPickerSafeUniqueExtensions()
    {
        var extensions = StoragePickerService.NormalizeExtensions(
            new[] { ".PNG", "jpg", "*.jpg", "*.*", string.Empty });

        CollectionAssert.AreEqual(
            new[] { ".png", ".jpg" },
            extensions.ToArray());
    }

    [TestMethod]
    public void BuildFilterMirrorsModernFileTypeChoicesForFallback()
    {
        var filter = StoragePickerService.BuildFilter(
            new[]
            {
                new StoragePickerService.FileTypeChoice("PNG", new[] { ".png" }),
                new StoragePickerService.FileTypeChoice("JPEG", new[] { ".jpg", ".jpeg" })
            });

        Assert.AreEqual(
            "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            filter);
    }

    [TestMethod]
    public void EmptyFileTypesFallbackToAllFiles()
    {
        Assert.AreEqual(
            "All files (*.*)|*.*",
            StoragePickerService.BuildFilter(Array.Empty<StoragePickerService.FileTypeChoice>()));
    }
}
