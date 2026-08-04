using System.Text.RegularExpressions;

namespace Snapture.App.Editor;

public enum DiagramMarkupKind
{
    Mermaid,
    PlantUml
}

public sealed class DiagramNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 150;
    public float Height { get; set; } = 44;
}

public sealed class DiagramEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class DiagramDefinition
{
    public DiagramMarkupKind Kind { get; init; }
    public List<DiagramNode> Nodes { get; } = new();
    public List<DiagramEdge> Edges { get; } = new();
    public float Width { get; internal set; }
    public float Height { get; internal set; }
}

/// <summary>Parses the local, dependency-free Mermaid flowchart and PlantUML edge subset.</summary>
public static class DiagramMarkupParser
{
    private const int MaxNodes = 80;
    private const int MaxEdges = 160;
    private static readonly Regex PlantDeclaration = new(
        @"^(?:participant|actor|boundary|control|entity|database|collections|queue|class|component|interface|rectangle|node|cloud|folder|frame|package)\s+(?<body>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlantEdge = new(
        @"^(?<left>.+?)\s+(?<arrow><[-.]+>|[-.]+>)\s+(?<right>.+?)(?:\s*:\s*(?<label>.*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DiagramDefinition Parse(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
            throw new FormatException("Paste a Mermaid flowchart or PlantUML diagram first.");

        var first = markup.Replace('\r', ' ').Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal) && !line.StartsWith("'", StringComparison.Ordinal));
        if (first is null)
            throw new FormatException("The diagram markup is empty.");

        DiagramDefinition definition;
        if (first.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase))
            definition = ParsePlantUml(markup);
        else if (first.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
                 first.StartsWith("graph", StringComparison.OrdinalIgnoreCase))
            definition = ParseMermaid(markup);
        else
            throw new FormatException("Use Mermaid flowchart/graph syntax or PlantUML @startuml syntax.");

        if (definition.Nodes.Count == 0)
            throw new FormatException("No diagram nodes were found.");
        Layout(definition);
        return definition;
    }

    public static bool TryParse(string? markup, out DiagramDefinition? definition, out string error)
    {
        try
        {
            definition = Parse(markup ?? "");
            error = "";
            return true;
        }
        catch (FormatException ex)
        {
            definition = null;
            error = ex.Message;
            return false;
        }
    }

    private static DiagramDefinition ParseMermaid(string markup)
    {
        var definition = new DiagramDefinition { Kind = DiagramMarkupKind.Mermaid };
        foreach (var rawLine in markup.Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Split("%%", 2, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0 || line.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("graph", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("style ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var statement in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ParseMermaidStatement(statement, definition);
        }

        return definition;
    }

    private static void ParseMermaidStatement(string statement, DiagramDefinition definition)
    {
        var arrowIndex = FindMermaidArrow(statement, out var arrowLength);
        if (arrowIndex < 0)
        {
            AddNode(definition, ParseMermaidEndpoint(statement));
            return;
        }

        string leftText = statement[..arrowIndex].Trim();
        string rightText = statement[(arrowIndex + arrowLength)..].Trim();
        string label = "";
        if (rightText.StartsWith('|'))
        {
            int endLabel = rightText.IndexOf('|', 1);
            if (endLabel > 1)
            {
                label = CleanLabel(rightText[1..endLabel]);
                rightText = rightText[(endLabel + 1)..].Trim();
            }
        }

        var embeddedLabel = Regex.Match(leftText, @"--\s*(?<label>[^-]+?)\s*$");
        if (label.Length == 0 && embeddedLabel.Success)
        {
            label = CleanLabel(embeddedLabel.Groups["label"].Value);
            leftText = leftText[..embeddedLabel.Index].Trim();
        }

        var right = ParseMermaidEndpoint(rightText);
        foreach (var leftPart in leftText.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var left = ParseMermaidEndpoint(leftPart);
            AddNode(definition, left);
            AddNode(definition, right);
            AddEdge(definition, left.Id, right.Id, label);
        }
    }

    private static int FindMermaidArrow(string statement, out int length)
    {
        foreach (var arrow in new[] { "-.->", "==>", "-->", "---" })
        {
            int index = statement.IndexOf(arrow, StringComparison.Ordinal);
            if (index >= 0)
            {
                length = arrow.Length;
                return index;
            }
        }

        length = 0;
        return -1;
    }

    private static DiagramDefinition ParsePlantUml(string markup)
    {
        var definition = new DiagramDefinition { Kind = DiagramMarkupKind.PlantUml };
        bool inside = false;
        foreach (var rawLine in markup.Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("'", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase)) { inside = true; continue; }
            if (line.StartsWith("@enduml", StringComparison.OrdinalIgnoreCase)) break;
            if (!inside || line.StartsWith("@", StringComparison.Ordinal) ||
                line.StartsWith("skinparam", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("hide ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("show ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("title ", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("left to right direction", StringComparison.OrdinalIgnoreCase))
                continue;

            var declaration = PlantDeclaration.Match(line);
            if (declaration.Success)
            {
                AddNode(definition, ParsePlantEndpoint(declaration.Groups["body"].Value));
                continue;
            }

            var edge = PlantEdge.Match(line);
            if (!edge.Success) continue;
            var left = ParsePlantEndpoint(edge.Groups["left"].Value);
            var right = ParsePlantEndpoint(edge.Groups["right"].Value);
            if (edge.Groups["arrow"].Value.StartsWith('<'))
                (left, right) = (right, left);
            AddNode(definition, left);
            AddNode(definition, right);
            AddEdge(definition, left.Id, right.Id, CleanLabel(edge.Groups["label"].Value));
        }

        return definition;
    }

    private static DiagramNode ParseMermaidEndpoint(string source)
    {
        source = source.Trim().TrimStart('&');
        int open = source.IndexOfAny(new[] { '[', '(', '{', '<' });
        if (open <= 0)
        {
            string plainId = source.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return new DiagramNode { Id = plainId, Label = CleanLabel(plainId) };
        }

        string nodeId = source[..open].Trim();
        char close = source[open] switch { '[' => ']', '(' => ')', '{' => '}', '<' => ']', _ => ']' };
        int end = source.LastIndexOf(close);
        string label = end > open ? source[(open + 1)..end] : source[(open + 1)..];
        return new DiagramNode { Id = nodeId, Label = CleanLabel(label.Length == 0 ? nodeId : label) };
    }

    private static DiagramNode ParsePlantEndpoint(string source)
    {
        source = source.Trim();
        int alias = source.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        if (alias > 0)
        {
            string label = CleanLabel(source[..alias]);
            string id = source[(alias + 4)..].Trim();
            return new DiagramNode { Id = id, Label = label.Length == 0 ? id : label };
        }

        string cleaned = CleanLabel(source);
        return new DiagramNode { Id = cleaned.Replace(' ', '_'), Label = cleaned };
    }

    private static void AddNode(DiagramDefinition definition, DiagramNode node)
    {
        if (node.Id.Length == 0 || definition.Nodes.Any(existing => existing.Id == node.Id)) return;
        if (definition.Nodes.Count >= MaxNodes) throw new FormatException($"Diagrams are limited to {MaxNodes} nodes.");
        definition.Nodes.Add(node);
    }

    private static void AddEdge(DiagramDefinition definition, string from, string to, string label)
    {
        if (definition.Edges.Count >= MaxEdges) throw new FormatException($"Diagrams are limited to {MaxEdges} connections.");
        definition.Edges.Add(new DiagramEdge { From = from, To = to, Label = label });
    }

    private static void Layout(DiagramDefinition definition)
    {
        int columns = Math.Min(4, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(definition.Nodes.Count))));
        const float horizontalGap = 32;
        const float verticalGap = 34;
        const float nodeWidth = 150;
        const float nodeHeight = 44;
        for (int i = 0; i < definition.Nodes.Count; i++)
        {
            var node = definition.Nodes[i];
            node.X = 20 + (i % columns) * (nodeWidth + horizontalGap);
            node.Y = 20 + (i / columns) * (nodeHeight + verticalGap);
            node.Width = nodeWidth;
            node.Height = nodeHeight;
        }

        definition.Width = (columns * (nodeWidth + horizontalGap)) + 20 - horizontalGap;
        definition.Height = ((int)Math.Ceiling(definition.Nodes.Count / (double)columns) * (nodeHeight + verticalGap)) + 20 - verticalGap;
    }

    private static string CleanLabel(string label) =>
        label.Trim().Trim('"', '\'').Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase).Trim();
}
