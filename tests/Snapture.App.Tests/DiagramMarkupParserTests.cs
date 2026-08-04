using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class DiagramMarkupParserTests
{
    [TestMethod]
    public void MermaidFlowchartParsesNodesConnectionsAndLabels()
    {
        const string markup = "flowchart LR\n A[Start] -->|calls| B{Decision}\n B -- next --> C[Done]";

        var definition = DiagramMarkupParser.Parse(markup);

        Assert.AreEqual(DiagramMarkupKind.Mermaid, definition.Kind);
        Assert.HasCount(3, definition.Nodes);
        Assert.AreEqual("Start", definition.Nodes.Single(node => node.Id == "A").Label);
        Assert.AreEqual("Decision", definition.Nodes.Single(node => node.Id == "B").Label);
        Assert.HasCount(2, definition.Edges);
        Assert.AreEqual("calls", definition.Edges[0].Label);
        Assert.AreEqual("next", definition.Edges[1].Label);
        Assert.IsGreaterThan(0, definition.Width);
        Assert.IsGreaterThan(0, definition.Height);
    }

    [TestMethod]
    public void PlantUmlParsesAliasesDeclarationsAndConnections()
    {
        const string markup = "@startuml\nparticipant \"Client\" as c\nrectangle API\nc -> API : Request\nAPI --> c : Response\n@enduml";

        var definition = DiagramMarkupParser.Parse(markup);

        Assert.AreEqual(DiagramMarkupKind.PlantUml, definition.Kind);
        Assert.HasCount(2, definition.Nodes);
        Assert.AreEqual("Client", definition.Nodes.Single(node => node.Id == "c").Label);
        Assert.AreEqual("API", definition.Nodes.Single(node => node.Id == "API").Label);
        Assert.HasCount(2, definition.Edges);
        Assert.AreEqual("Request", definition.Edges[0].Label);
        Assert.AreEqual("Response", definition.Edges[1].Label);
    }

    [TestMethod]
    public void UnsupportedMarkupReturnsActionableError()
    {
        var parsed = DiagramMarkupParser.TryParse("sequenceDiagram\nAlice->>Bob: Hi", out var definition, out var error);

        Assert.IsFalse(parsed);
        Assert.IsNull(definition);
        StringAssert.Contains(error, "Mermaid flowchart/graph");
    }

    [TestMethod]
    public void DiagramShapeRoundTripsAndRendersAsOneEditableShape()
    {
        const string markup = "flowchart TD\nA[One] --> B[Two]";
        var definition = DiagramMarkupParser.Parse(markup);
        var shape = DiagramShape.FromDefinition(definition, markup, 10, 12);
        shape.Sloppiness = 0.35f;

        using var background = new SKBitmap(420, 220);
        var document = new AnnotationDocument(background);
        document.Shapes.Add(shape);
        using var restoredBackground = new SKBitmap(420, 220);
        var restored = new AnnotationDocument(restoredBackground);
        restored.DeserializeShapes(document.SerializeShapes());
        using var canvas = new SKCanvas(restored.Background);
        restored.Render(canvas, flattenForExport: true);

        var restoredShape = (DiagramShape)restored.Shapes.Single();
        Assert.AreEqual(DiagramMarkupKind.Mermaid, restoredShape.DiagramKind);
        Assert.AreEqual(markup, restoredShape.Markup);
        Assert.HasCount(2, restoredShape.Nodes);
        Assert.HasCount(1, restoredShape.Edges);
        Assert.AreNotEqual(SKColors.Transparent, restored.Background.GetPixel(20, 30));
    }
}
