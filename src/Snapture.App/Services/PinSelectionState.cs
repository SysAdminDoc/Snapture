namespace Snapture.App.Services;

/// <summary>Reference-based selection state shared by the pin overlay windows.</summary>
internal sealed class PinSelectionState<T> where T : class
{
    private readonly HashSet<T> _selected = new(ReferenceEqualityComparer.Instance);

    public int Count => _selected.Count;

    public bool Contains(T item) => _selected.Contains(item);

    public IReadOnlyList<T> Items => _selected.ToArray();

    public void SelectOnly(T item)
    {
        _selected.Clear();
        _selected.Add(item);
    }

    public bool Toggle(T item)
    {
        if (_selected.Remove(item))
            return false;

        _selected.Add(item);
        return true;
    }

    public void SelectAll(IEnumerable<T> items)
    {
        _selected.Clear();
        foreach (var item in items)
            _selected.Add(item);
    }

    public void Clear() => _selected.Clear();

    public void Remove(T item) => _selected.Remove(item);

    public IReadOnlyList<T> TargetsFor(T requested)
    {
        if (_selected.Count > 0 && _selected.Contains(requested))
            return Items;

        return new[] { requested };
    }
}
