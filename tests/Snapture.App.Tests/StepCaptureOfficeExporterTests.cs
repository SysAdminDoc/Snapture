using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using Snapture.App.Services;
using DrawingText = DocumentFormat.OpenXml.Drawing.Text;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace Snapture.App.Tests;

[TestClass]
public sealed class StepCaptureOfficeExporterTests
{
    [TestMethod]
    public void ExportDocxContainsEditableCaptionsAndEmbeddedImages()
    {
        var root = CreateFixtureRoot();
        try
        {
            var imagePath = CreateImage(root, "step.png");
            var output = Path.Combine(root, "guide.docx");
            var entries = new[]
            {
                new StepCaptureExporter.StepEntry(
                    1,
                    imagePath,
                    "Click Settings",
                    new[] { new StepCaptureKeyStroke("CTRL+S", DateTime.UtcNow) },
                    new[] { new StepCaptureClick(120, 240, StepCaptureClickButton.Left, DateTime.UtcNow) }),
                new StepCaptureExporter.StepEntry(2, imagePath, "Choose the capture mode")
            };

            StepCaptureOfficeExporter.ExportDocx(output, "Capture guide", entries);

            using var document = WordprocessingDocument.Open(output, false);
            var mainPart = document.MainDocumentPart ?? throw new AssertFailedException("DOCX main part missing.");
            var body = mainPart.Document?.Body ?? throw new AssertFailedException("DOCX body missing.");
            Assert.IsTrue(body.Descendants<WordText>().Any(text => text.Text == "Capture guide"));
            Assert.IsTrue(body.Descendants<WordText>().Any(text => text.Text == "Click Settings"));
            Assert.IsTrue(body.Descendants<WordText>().Any(text =>
                text.Text == "Input track: Keys: CTRL+S · Clicks: left (120, 240)"));
            Assert.HasCount(2, body.Descendants<Drawing>());
            Assert.HasCount(2, mainPart.ImageParts);
            var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).ToArray();
            Assert.IsEmpty(validationErrors.Select(error => error.Description), string.Join(" | ", validationErrors.Select(error => error.Description)));
            using (var archive = ZipFile.OpenRead(output))
                Assert.IsTrue(archive.Entries.Any(entry =>
                    entry.FullName.Contains("media", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteFixtureRoot(root);
        }
    }

    [TestMethod]
    public void ExportPptxContainsTitleSlideEditableTextAndStepImages()
    {
        var root = CreateFixtureRoot();
        try
        {
            var imagePath = CreateImage(root, "step.png");
            var output = Path.Combine(root, "guide.pptx");
            var entries = new[]
            {
                new StepCaptureExporter.StepEntry(
                    1,
                    imagePath,
                    "Open Settings",
                    new[] { new StepCaptureKeyStroke("ENTER", DateTime.UtcNow) },
                    new[] { new StepCaptureClick(40, 80, StepCaptureClickButton.Right, DateTime.UtcNow) }),
                new StepCaptureExporter.StepEntry(2, imagePath, "Save the change")
            };

            StepCaptureOfficeExporter.ExportPptx(output, "Capture guide", entries);

            using var document = PresentationDocument.Open(output, false);
            var presentationPart = document.PresentationPart ?? throw new AssertFailedException("PPTX presentation part missing.");
            var presentation = presentationPart.Presentation ?? throw new AssertFailedException("PPTX presentation missing.");
            var slideIdList = presentation.SlideIdList ?? throw new AssertFailedException("PPTX slide list missing.");
            var slideIds = slideIdList.Elements<SlideId>().ToArray();
            Assert.HasCount(3, slideIds);
            var slideParts = slideIds
                .Select(id => (SlidePart)presentationPart.GetPartById(id.RelationshipId!))
                .ToArray();
            var titleSlide = slideParts[0].Slide ?? throw new AssertFailedException("PPTX title slide missing.");
            var firstStepSlide = slideParts[1].Slide ?? throw new AssertFailedException("PPTX step slide missing.");
            Assert.IsTrue(titleSlide.Descendants<DrawingText>().Any(text => text.Text == "Capture guide"));
            Assert.IsTrue(firstStepSlide.Descendants<DrawingText>().Any(text => text.Text == "Open Settings"));
            Assert.IsTrue(firstStepSlide.Descendants<DrawingText>().Any(text =>
                text.Text == "Input track: Keys: ENTER · Clicks: right (40, 80)"));
            Assert.HasCount(0, slideParts[0].ImageParts);
            Assert.HasCount(1, slideParts[1].ImageParts);
            Assert.HasCount(1, slideParts[2].ImageParts);
            var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).ToArray();
            Assert.IsEmpty(validationErrors.Select(error => error.Description), string.Join(" | ", validationErrors.Select(error => error.Description)));
            using (var archive = ZipFile.OpenRead(output))
                Assert.IsTrue(archive.Entries.Any(entry => entry.FullName == "ppt/slides/slide3.xml"));
        }
        finally
        {
            DeleteFixtureRoot(root);
        }
    }

    private static string CreateFixtureRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Snapture-StepCaptureOfficeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateImage(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        using var bitmap = new SKBitmap(160, 100);
        bitmap.Erase(new SKColor(42, 48, 64));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static void DeleteFixtureRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
