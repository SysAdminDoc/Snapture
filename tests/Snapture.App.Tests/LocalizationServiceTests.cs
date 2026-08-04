using System.Globalization;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class LocalizationServiceTests
{
    [TestMethod]
    public void BaseCatalogContainsRepresentativeUiCopy()
    {
        LocalizationService.Initialize("en-US");

        Assert.IsTrue(LocalizationService.HasResource("Settings"));
        Assert.AreEqual("Settings", LocalizationService.Get("Settings"));
        Assert.IsTrue(LocalizationService.HasResource("Capture engine"));
    }

    [TestMethod]
    public void UnknownCopyFallsBackWithoutThrowing()
    {
        LocalizationService.Initialize("en-US");

        const string source = "A plugin supplied message that is not in the base catalog.";
        Assert.IsFalse(LocalizationService.HasResource(source));
        Assert.AreEqual(source, LocalizationService.Get(source));
    }

    [TestMethod]
    public void ResourceKeysAreStableAndCultureListIsExplicit()
    {
        Assert.AreEqual(
            LocalizationService.ResourceKey("Settings"),
            LocalizationService.ResourceKey("Settings"));
        CollectionAssert.Contains(LocalizationService.PhaseOneCultures.ToArray(), "en-US");
        CollectionAssert.Contains(LocalizationService.PhaseOneCultures.ToArray(), "ar");
        Assert.AreEqual("en-US", CultureInfo.GetCultureInfo("en-US").Name);
    }
}
