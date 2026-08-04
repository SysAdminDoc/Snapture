namespace Snapture.App.Editor;

/// <summary>
/// One annotation operation. Concrete subtypes hold the data needed to undo / redo
/// without serialising the whole document.
/// </summary>
public abstract class AnnotationCommand
{
    public abstract void Apply(AnnotationDocument doc);
    public abstract void Revert(AnnotationDocument doc);
}

public sealed class AddShapeCommand(Shape shape) : AnnotationCommand
{
    private readonly Shape _shape = shape;

    public override void Apply(AnnotationDocument doc) => doc.Shapes.Add(_shape);
    public override void Revert(AnnotationDocument doc) => doc.Shapes.Remove(_shape);
}

public sealed class RemoveShapeCommand(Shape shape) : AnnotationCommand
{
    private readonly Shape _shape = shape;
    private int _restoreIndex;

    public override void Apply(AnnotationDocument doc)
    {
        _restoreIndex = doc.Shapes.IndexOf(_shape);
        doc.Shapes.Remove(_shape);
    }

    public override void Revert(AnnotationDocument doc)
    {
        if (_restoreIndex < 0 || _restoreIndex > doc.Shapes.Count)
            doc.Shapes.Add(_shape);
        else
            doc.Shapes.Insert(_restoreIndex, _shape);
    }
}

public sealed class SetShapeColorCommand : AnnotationCommand
{
    private readonly Shape[] _shapes;
    private readonly Dictionary<Shape, uint> _originalColors;
    private readonly uint _newColor;

    public SetShapeColorCommand(IEnumerable<Shape> shapes, uint newColor)
    {
        _shapes = shapes.Distinct().ToArray();
        _originalColors = _shapes.ToDictionary(shape => shape, shape => shape.StrokeColorArgb);
        _newColor = newColor;
    }

    public override void Apply(AnnotationDocument doc)
    {
        foreach (var shape in _shapes)
            shape.StrokeColorArgb = _newColor;
    }

    public override void Revert(AnnotationDocument doc)
    {
        foreach (var shape in _shapes)
            shape.StrokeColorArgb = _originalColors[shape];
    }
}

public sealed class SetShapeCategoryCommand : AnnotationCommand
{
    private readonly Shape[] _shapes;
    private readonly Dictionary<Shape, AnnotationCategory> _originalCategories;
    private readonly AnnotationCategory _newCategory;

    public SetShapeCategoryCommand(IEnumerable<Shape> shapes, AnnotationCategory newCategory)
    {
        _shapes = shapes.Distinct().ToArray();
        _originalCategories = _shapes.ToDictionary(shape => shape, shape => shape.Category);
        _newCategory = newCategory;
    }

    public override void Apply(AnnotationDocument doc)
    {
        foreach (var shape in _shapes)
            shape.Category = _newCategory;
    }

    public override void Revert(AnnotationDocument doc)
    {
        foreach (var shape in _shapes)
            shape.Category = _originalCategories[shape];
    }
}

/// <summary>
/// Groups multiple commands into a single undoable/redoable operation.
/// </summary>
public sealed class CompositeCommand : AnnotationCommand
{
    private readonly List<AnnotationCommand> _children;

    public CompositeCommand(IEnumerable<AnnotationCommand> children)
    {
        _children = children.ToList();
    }

    public override void Apply(AnnotationDocument doc)
    {
        foreach (var cmd in _children) cmd.Apply(doc);
    }

    public override void Revert(AnnotationDocument doc)
    {
        // Revert in reverse order to maintain consistency.
        for (int i = _children.Count - 1; i >= 0; i--)
            _children[i].Revert(doc);
    }
}

public sealed class CommandStack
{
    private readonly Stack<AnnotationCommand> _undo = new();
    private readonly Stack<AnnotationCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Do(AnnotationDocument doc, AnnotationCommand cmd)
    {
        cmd.Apply(doc);
        _undo.Push(cmd);
        _redo.Clear();
    }

    public void Undo(AnnotationDocument doc)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        cmd.Revert(doc);
        _redo.Push(cmd);
    }

    public void Redo(AnnotationDocument doc)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        cmd.Apply(doc);
        _undo.Push(cmd);
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); }
}
