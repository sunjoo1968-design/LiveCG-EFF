namespace LiveCG.App;

public sealed record EditorState(string Json,string? File,Guid? Region,Guid? Clip,double Time,Guid[]? SelectedClips=null,Guid? Mask=null);

public sealed class EditHistory
{
    private readonly List<EditorState> _undo=[],_redo=[];
    public EditorState? Current { get; private set; }
    public bool CanUndo=>_undo.Count>0;
    public bool CanRedo=>_redo.Count>0;
    public int UndoCount=>_undo.Count;
    public void Reset(EditorState state){_undo.Clear();_redo.Clear();Current=state;}
    public void Record(EditorState state)
    {
        if(Current is null){Reset(state);return;}
        if(Current.Json==state.Json){Current=state;return;}
        _undo.Add(Current);Current=state;_redo.Clear();
        while(_undo.Count>50||_undo.Sum(s=>(long)s.Json.Length)*2>32*1024*1024)_undo.RemoveAt(0);
    }
    public void UpdateSelection(EditorState state){if(Current?.Json==state.Json)Current=state;}
    public EditorState? Undo(){if(!CanUndo)return null;_redo.Add(Current!);Current=_undo[^1];_undo.RemoveAt(_undo.Count-1);return Current;}
    public EditorState? Redo(){if(!CanRedo)return null;_undo.Add(Current!);Current=_redo[^1];_redo.RemoveAt(_redo.Count-1);return Current;}
}
