using Dwalia.Infrastructure;
using Dwalia.Models;

namespace Dwalia.Managers;

public class WorkspaceManager
{
    private readonly List<Workspace> _workspaces = new();
    private readonly Dictionary<int, int> _monitorActiveWs = new();
    private int _currentMonitorId;

    public IReadOnlyList<Workspace> Workspaces => _workspaces;
    public int ActiveWorkspaceId => _monitorActiveWs.GetValueOrDefault(_currentMonitorId, 0);

    public int CurrentMonitorId
    {
        get => _currentMonitorId;
        set => _currentMonitorId = value;
    }

    public int GetActiveWorkspaceIdForMonitor(int monitorId) =>
        _monitorActiveWs.GetValueOrDefault(monitorId, 0);

    public event EventHandler<int>? WorkspaceChanged;

    public WorkspaceManager()
    {
        var defaultNames = new[] { "1:Term", "2:Code", "3:Web", "4:Comm", "5:Misc" };
        for (int i = 0; i < defaultNames.Length; i++)
        {
            _workspaces.Add(new Workspace(i, defaultNames[i]));
        }
        _monitorActiveWs[0] = 0;
        _currentMonitorId = 0;
    }

    public void Initialize(string[] names)
    {
        _workspaces.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            _workspaces.Add(new Workspace(i, names[i]));
        }
    }

    public void InitializeForMonitor(int monitorId)
    {
        if (!_monitorActiveWs.ContainsKey(monitorId))
            _monitorActiveWs[monitorId] = 0;
    }

    public void SwitchToWorkspace(int id)
    {
        SwitchToWorkspaceOnMonitor(_currentMonitorId, id);
    }

    public void SwitchToWorkspaceOnMonitor(int monitorId, int id)
    {
        if (id < 0 || id >= _workspaces.Count) return;
        if (_monitorActiveWs.GetValueOrDefault(monitorId, 0) == id) return;

        Logger.Info($"Switching monitor {monitorId} to workspace {id}: {_workspaces[id].Name}");
        _monitorActiveWs[monitorId] = id;
        WorkspaceChanged?.Invoke(this, id);
    }

    public void NextWorkspace()
    {
        var current = _monitorActiveWs.GetValueOrDefault(_currentMonitorId, 0);
        var next = (current + 1) % _workspaces.Count;
        SwitchToWorkspace(next);
    }

    public void PreviousWorkspace()
    {
        var current = _monitorActiveWs.GetValueOrDefault(_currentMonitorId, 0);
        var prev = (current - 1 + _workspaces.Count) % _workspaces.Count;
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
        {
            workspaceId = _monitorActiveWs.GetValueOrDefault(mw.MonitorId, 0);
        }

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

    public Workspace GetActiveWorkspace()
    {
        return _workspaces[_monitorActiveWs.GetValueOrDefault(_currentMonitorId, 0)];
    }

    public Workspace GetActiveWorkspaceForMonitor(int monitorId)
    {
        return _workspaces[_monitorActiveWs.GetValueOrDefault(monitorId, 0)];
    }
}
