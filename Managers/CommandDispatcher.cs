using Dwalia.Infrastructure;
using Dwalia.Models;
using static Dwalia.Win32.NativeMethods;
using static Dwalia.Win32.WindowStyles;

namespace Dwalia.Managers;

public static class CommandDispatcher
{
    public static void Execute(DwaliaCommand cmd,
        WorkspaceManager ws, FocusManager fm, LayoutManager lm,
        string launchTerminal, Action? reloadConfig = null, Action? openSettings = null, Action? quit = null,
        Action? toggleTaskBar = null)
    {
        var aws = ws.GetActiveWorkspace();

        switch (cmd)
        {
            case DwaliaCommand.FocusNext: fm.FocusNext(aws.Windows); break;
            case DwaliaCommand.FocusPrevious: fm.FocusPrevious(aws.Windows); break;
            case DwaliaCommand.ToggleFloat: if (fm.ActiveWindow != null) lm.ToggleFloating(fm.ActiveWindow.Hwnd); break;
            case DwaliaCommand.ToggleFullscreen: if (fm.ActiveWindow != null) lm.ToggleFullscreen(fm.ActiveWindow.Hwnd); break;
            case DwaliaCommand.CloseWindow: if (fm.ActiveWindow != null) PostMessage(fm.ActiveWindow.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
            case DwaliaCommand.QuitDwalia: quit?.Invoke(); break;
            case DwaliaCommand.FocusWindow1: FocusWindow(0, fm, aws); break;
            case DwaliaCommand.FocusWindow2: FocusWindow(1, fm, aws); break;
            case DwaliaCommand.FocusWindow3: FocusWindow(2, fm, aws); break;
            case DwaliaCommand.FocusWindow4: FocusWindow(3, fm, aws); break;
            case DwaliaCommand.FocusWindow5: FocusWindow(4, fm, aws); break;
            case DwaliaCommand.FocusWindow6: FocusWindow(5, fm, aws); break;
            case DwaliaCommand.FocusWindow7: FocusWindow(6, fm, aws); break;
            case DwaliaCommand.FocusWindow8: FocusWindow(7, fm, aws); break;
            case DwaliaCommand.FocusWindow9: FocusWindow(8, fm, aws); break;
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
            case DwaliaCommand.OpenSettings: openSettings?.Invoke(); break;
            case DwaliaCommand.CycleLayout: lm.CycleLayout(); break;
            case DwaliaCommand.IncMaster: lm.ResizeMaster(0.05); break;
            case DwaliaCommand.DecMaster: lm.ResizeMaster(-0.05); break;
            case DwaliaCommand.IncGap: lm.ResizeGap(1); break;
            case DwaliaCommand.DecGap: lm.ResizeGap(-1); break;
            case DwaliaCommand.SwapNext: lm.SwapNext(); break;
            case DwaliaCommand.SwapPrevious: lm.SwapPrevious(); break;
            case DwaliaCommand.ToggleTaskBar: toggleTaskBar?.Invoke(); break;
        }
    }

    private static void FocusWindow(int index, FocusManager fm, Workspace aws)
    {
        if (index < 0 || index >= aws.Windows.Count)
        {
            Logger.Warn($"FocusWindow{index + 1}: only {aws.Windows.Count} windows in workspace");
            return;
        }
        fm.SetActiveWindow(aws.Windows[index]);
        aws.Windows[index].Focus();
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
