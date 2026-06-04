using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public static class CommandDispatcher
{
    public static void Execute(DwaliaCommand cmd,
        WorkspaceManager ws, FocusManager fm, LayoutManager lm,
        string launchTerminal, Action? reloadConfig = null, Action? quit = null,
        Action<int>? cycleBar = null, Action? toggleBar = null)
    {
        switch (cmd)
        {
            case DwaliaCommand.FocusNext:
            case DwaliaCommand.FocusDown: { var tiled = GetTiledWindows(ws); if (tiled != null) fm.FocusDown(tiled); break; }
            case DwaliaCommand.FocusPrevious:
            case DwaliaCommand.FocusUp: { var tiled = GetTiledWindows(ws); if (tiled != null) fm.FocusUp(tiled); break; }
            case DwaliaCommand.FocusLeft: { var tiled = GetTiledWindows(ws); if (tiled != null) fm.FocusLeft(tiled); break; }
            case DwaliaCommand.FocusRight: { var tiled = GetTiledWindows(ws); if (tiled != null) fm.FocusRight(tiled); break; }
            case DwaliaCommand.ToggleFloat: if (fm.ActiveWindow != null) lm.ToggleFloating(fm.ActiveWindow.Hwnd); break;
            case DwaliaCommand.ToggleFullscreen: if (fm.ActiveWindow != null) lm.ToggleFullscreen(fm.ActiveWindow.Hwnd); break;
            case DwaliaCommand.CloseWindow: if (fm.ActiveWindow != null) PostMessage(fm.ActiveWindow.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
            case DwaliaCommand.QuitDwalia: quit?.Invoke(); break;
            case DwaliaCommand.FocusWindow1: FocusWindow(0, fm, lm); break;
            case DwaliaCommand.FocusWindow2: FocusWindow(1, fm, lm); break;
            case DwaliaCommand.FocusWindow3: FocusWindow(2, fm, lm); break;
            case DwaliaCommand.FocusWindow4: FocusWindow(3, fm, lm); break;
            case DwaliaCommand.FocusWindow5: FocusWindow(4, fm, lm); break;
            case DwaliaCommand.FocusWindow6: FocusWindow(5, fm, lm); break;
            case DwaliaCommand.FocusWindow7: FocusWindow(6, fm, lm); break;
            case DwaliaCommand.FocusWindow8: FocusWindow(7, fm, lm); break;
            case DwaliaCommand.FocusWindow9: FocusWindow(8, fm, lm); break;
            case DwaliaCommand.Workspace1: ws.SwitchToWorkspace(0); break;
            case DwaliaCommand.Workspace2: ws.SwitchToWorkspace(1); break;
            case DwaliaCommand.Workspace3: ws.SwitchToWorkspace(2); break;
            case DwaliaCommand.Workspace4: ws.SwitchToWorkspace(3); break;
            case DwaliaCommand.Workspace5: ws.SwitchToWorkspace(4); break;
            case DwaliaCommand.WorkspaceNext: ws.NextWorkspace(); break;
            case DwaliaCommand.WorkspacePrevious: ws.PreviousWorkspace(); break;
            case DwaliaCommand.MoveToWorkspaceNext: MoveActiveRelative(1, fm, ws); break;
            case DwaliaCommand.MoveToWorkspacePrevious: MoveActiveRelative(-1, fm, ws); break;
            case DwaliaCommand.LaunchTerminal: LaunchTerminal(launchTerminal); break;
            case DwaliaCommand.ReloadConfig: reloadConfig?.Invoke(); break;
            case DwaliaCommand.CycleLayout: lm.CycleLayout(); break;
            case DwaliaCommand.CycleLayoutPrevious: lm.CycleLayoutPrevious(); break;
            case DwaliaCommand.IncGap: lm.ResizeGap(1); break;
            case DwaliaCommand.DecGap: lm.ResizeGap(-1); break;
            case DwaliaCommand.IncInnerGap: lm.ResizeInnerGap(2); break;
            case DwaliaCommand.DecInnerGap: lm.ResizeInnerGap(-2); break;
            case DwaliaCommand.IncOuterGap: lm.ResizeOuterGap(2); break;
            case DwaliaCommand.DecOuterGap: lm.ResizeOuterGap(-2); break;
            case DwaliaCommand.ResizeLeft: lm.ResizeLeft(); break;
            case DwaliaCommand.ResizeDown: lm.ResizeDown(); break;
            case DwaliaCommand.ResizeUp: lm.ResizeUp(); break;
            case DwaliaCommand.ResizeRight: lm.ResizeRight(); break;
            case DwaliaCommand.SwapNext:
            case DwaliaCommand.SwapDown: lm.SwapDown(); break;
            case DwaliaCommand.SwapPrevious:
            case DwaliaCommand.SwapUp: lm.SwapUp(); break;
            case DwaliaCommand.SwapLeft: lm.SwapLeft(); break;
            case DwaliaCommand.SwapRight: lm.SwapRight(); break;
            case DwaliaCommand.BarNext: cycleBar?.Invoke(1); break;
            case DwaliaCommand.BarPrevious: cycleBar?.Invoke(-1); break;
            case DwaliaCommand.ToggleBar: toggleBar?.Invoke(); break;
            case DwaliaCommand.ToggleScratchpad:
                if (ServiceLocator.TryResolve<ScratchpadManager>(out var sp))
                    sp.ToggleScratchpad(fm.ActiveWindow, ws, lm, fm);
                break;
            case DwaliaCommand.ActivateWindow:
                fm.ActivateActiveWindow();
                break;
            case DwaliaCommand.ToggleSticky:
                if (fm.ActiveWindow != null)
                {
                    ws.ToggleSticky(fm.ActiveWindow);
                    lm.Relayout();
                }
                break;
            case DwaliaCommand.EnterResizeMode:
                if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm))
                    hkm.EnterResizeMode();
                break;
            case DwaliaCommand.ExitResizeMode:
                if (ServiceLocator.TryResolve<HotKeyManager>(out var hkm2))
                    hkm2.ExitResizeMode();
                break;
        }
    }

    private static List<ManagedWindow>? GetTiledWindows(WorkspaceManager ws)
    {
        return ws.GetActiveWorkspace()?.Windows
            .Where(w => w.State == WindowLayoutState.Tiled)
            .ToList();
    }

    private static void FocusWindow(int index, FocusManager fm, LayoutManager lm)
    {
        var ordered = lm.GetOrderedWindows();
        if (index < 0 || index >= ordered.Count)
        {
            Logger.Warn($"FocusWindow{index + 1}: only {ordered.Count} windows in workspace");
            return;
        }
        fm.SetActiveWindow(ordered[index]);
    }

    private static void MoveActiveRelative(int direction, FocusManager fm, WorkspaceManager ws)
    {
        if (fm.ActiveWindow == null) return;
        var count = ws.Workspaces.Count;
        var newWs = (fm.ActiveWindow.WorkspaceId + direction + count) % count;
        ws.MoveWindowToWorkspace(fm.ActiveWindow, newWs);
    }

    private static void LaunchTerminal(string cmd)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = cmd, UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error($"Launch terminal failed: {ex.Message}"); }
    }
}
