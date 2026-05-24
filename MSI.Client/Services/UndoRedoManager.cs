namespace MSI.Client.Services;

public sealed class UndoRedoManager : IDisposable
{
    private const int MaxHistory = 3;
    private readonly LinkedList<HistoryEntry> _undoStack = new();
    private readonly LinkedList<HistoryEntry> _redoStack = new();
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? CurrentUndoLabel => _undoStack.Last?.Value.Label;
    public string? CurrentRedoLabel => _redoStack.Last?.Value.Label;

    public void Push(Bitmap snapshot, string label, string sessionId)
    {
        _redoStack.Clear();
        _undoStack.AddLast(new HistoryEntry(CopyBitmap(snapshot), label, sessionId));
        while (_undoStack.Count > MaxHistory)
        {
            _undoStack.First!.Value.Dispose();
            _undoStack.RemoveFirst();
        }
    }

    public HistoryEntry? Undo(Bitmap current, string currentLabel, string sessionId)
    {
        if (!CanUndo) return null;
        var entry = _undoStack.Last!.Value;
        _undoStack.RemoveLast();
        _redoStack.AddLast(new HistoryEntry(CopyBitmap(current), currentLabel, sessionId));
        return entry;
    }

    public HistoryEntry? Redo(Bitmap current, string currentLabel, string sessionId)
    {
        if (!CanRedo) return null;
        var entry = _redoStack.Last!.Value;
        _redoStack.RemoveLast();
        _undoStack.AddLast(new HistoryEntry(CopyBitmap(current), currentLabel, sessionId));
        return entry;
    }

    public void Clear()
    {
        foreach (var e in _undoStack) e.Dispose();
        foreach (var e in _redoStack) e.Dispose();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private static Bitmap CopyBitmap(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, src.PixelFormat);
        dst.SetResolution(src.HorizontalResolution, src.VerticalResolution);

        using (var g = Graphics.FromImage(dst))
        {
            g.DrawImage(src, 0, 0, src.Width, src.Height);
        }

        return dst;
    }
    public void Dispose() => Clear();
}

public sealed class HistoryEntry : IDisposable
{
    public Bitmap Snapshot { get; }
    public string Label { get; }
    public string SessionId { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public HistoryEntry(Bitmap snapshot, string label, string sessionId)
    {
        Snapshot = snapshot;
        Label = label;
        SessionId = sessionId;
    }

    public void Dispose() => Snapshot.Dispose();
}
