using Dwalia.Infrastructure;
using Dwalia.Models;

namespace Dwalia.Managers;

public class WorkspaceManager
{
    private readonly List<Workspace> _workspaces = new();
    private int _activeWorkspaceId;

    public IReadOnlyList<Workspace> Workspaces => _workspaces;
    public int ActiveWorkspaceId => _activeWorkspaceId;

    public event EventHandler<int>? WorkspaceChanged;

    public WorkspaceManager()
    {
        var defaultNames = new[] { "1:Term", "2:Code", "3:Web", "4:Comm", "5:Misc" };
        for (int i = 0; i < defaultNames.Length; i++)
        {
            _workspaces.Add(new Workspace(i, defaultNames[i]));
        }
        _activeWorkspaceId = 0;
    }

    public void Initialize(string[] names)
    {
        _workspaces.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            _workspaces.Add(new Workspace(i, names[i]));
        }
    }

    public void SwitchToWorkspace(int id)
    {
        if (id < 0 || id >= _workspaces.Count) return;

        if (_activeWorkspaceId == id) return;

        Logger.Info($"Switching to workspace {id}: {_workspaces[id].Name}");
        _activeWorkspaceId = id;
        WorkspaceChanged?.Invoke(this, id);
    }

    public void NextWorkspace()
    {
        var next = (_activeWorkspaceId + 1) % _workspaces.Count;
        SwitchToWorkspace(next);
    }

    public void PreviousWorkspace()
    {
        var prev = (_activeWorkspaceId - 1 + _workspaces.Count) % _workspaces.Count;
        SwitchToWorkspace(prev);
    }

    public void MoveWindowToWorkspace(ManagedWindow mw, int workspaceId)
    {
        if (workspaceId < 0 || workspaceId >= _workspaces.Count) return;

        Logger.Info($"Moving window '{mw.Title}' to workspace {workspaceId}");

        var oldWs = GetWorkspace(mw.WorkspaceId);
        oldWs?.Windows.Remove(mw);

        var newWs = _workspaces[workspaceId];
        newWs.Windows.Add(mw);
        mw.WorkspaceId = workspaceId;
    }

    public void AddWindow(ManagedWindow mw, int workspaceId)
    {
        if (workspaceId < 0 || workspaceId >= _workspaces.Count)
            workspaceId = _activeWorkspaceId;

        var ws = _workspaces[workspaceId];
        ws.Windows.Add(mw);
        mw.WorkspaceId = workspaceId;
    }

    public void RemoveWindow(ManagedWindow mw)
    {
        var ws = GetWorkspace(mw.WorkspaceId);
        ws?.Windows.Remove(mw);
    }

    public Workspace? GetWorkspace(int id)
    {
        if (id < 0 || id >= _workspaces.Count) return null;
        return _workspaces[id];
    }

    public Workspace GetActiveWorkspace() => _workspaces[_activeWorkspaceId];
}
