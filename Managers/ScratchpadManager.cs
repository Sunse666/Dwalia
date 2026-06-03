using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public class ScratchpadManager
{
    private readonly Stack<ManagedWindow> _scratchpadWindows = new();

    public int Count => _scratchpadWindows.Count;
    public bool HasWindows => _scratchpadWindows.Count > 0;

    public event EventHandler? ScratchpadUpdated;

    public void ToggleScratchpad(ManagedWindow? activeWindow,
        WorkspaceManager ws, LayoutManager lm, FocusManager fm)
    {
        if (activeWindow != null && activeWindow.IsScratchpad)
        {
            RestoreScratchpad(activeWindow, ws, lm, fm);
        }
        else if (activeWindow != null && !activeWindow.IsScratchpad)
        {
            SendToScratchpad(activeWindow, ws, lm);
        }
        else if (_scratchpadWindows.Count > 0)
        {
            var top = _scratchpadWindows.Pop();
            RestoreScratchpad(top, ws, lm, fm);
        }
    }

    private void SendToScratchpad(ManagedWindow mw, WorkspaceManager ws, LayoutManager lm)
    {
        ws.RemoveWindow(mw);
        mw.IsScratchpad = true;
        ShowWindow(mw.Hwnd, SW_HIDE);
        _scratchpadWindows.Push(mw);
        Logger.Info($"Scratchpad: sent '{mw.Title}' to scratchpad");
        lm.Relayout();
        ScratchpadUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreScratchpad(ManagedWindow mw,
        WorkspaceManager ws, LayoutManager lm, FocusManager fm)
    {
        mw.IsScratchpad = false;
        mw.State = WindowLayoutState.Floating;

        var wsId = ws.ActiveWorkspaceId;
        ws.AddWindow(mw, wsId);

        var area = lm.Area;
        int cx = (int)(area.X + area.Width / 2 - 400);
        int cy = (int)(area.Y + area.Height / 2 - 300);
        mw.LayoutBounds = new System.Windows.Rect(cx, cy, 800, 600);

        SetWindowPos(mw.Hwnd, IntPtr.Zero, cx, cy, 800, 600,
            SWP_NOZORDER | SWP_SHOWWINDOW);

        ShowWindow(mw.Hwnd, SW_RESTORE);
        fm.SetActiveWindow(mw);
        mw.Focus();

        Logger.Info($"Scratchpad: restored '{mw.Title}'");
        lm.Relayout();
        ScratchpadUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void HideAll()
    {
        while (_scratchpadWindows.Count > 0)
        {
            var mw = _scratchpadWindows.Pop();
            ShowWindow(mw.Hwnd, SW_HIDE);
        }
    }
}
