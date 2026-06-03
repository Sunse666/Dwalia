using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Dwalia.Infrastructure;

namespace Dwalia.Managers;

public class IpcServer : IDisposable
{
    private const string PipeName = "DwaliaIpcPipe";
    private CancellationTokenSource? _cts;
    private readonly Dispatcher _dispatcher;
    private bool _running;

    public IpcServer(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => RunLoop(token), token);
        Logger.Info("IPC server started");
    }

    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token);

                var buffer = new byte[4096];
                using var ms = new MemoryStream();
                int bytesRead;
                while ((bytesRead = await server.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    ms.Write(buffer, 0, bytesRead);
                    if (bytesRead < buffer.Length) break;
                }

                var text = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                string response;
                if (!string.IsNullOrEmpty(text))
                {
                    Logger.Info($"IPC command: {text}");
                    response = await ProcessCommand(text);
                }
                else
                {
                    response = JsonSerializer.Serialize(new { status = "error", message = "Empty command" });
                }

                var responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                await server.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                await server.FlushAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
            catch (Exception ex) { Logger.Warn($"IPC server error: {ex.Message}"); }
        }
    }

    public static string? SendCommandAndExit(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(3000);
            client.Write(Encoding.UTF8.GetBytes(command));
            client.WaitForPipeDrain();

            using var reader = new StreamReader(client, Encoding.UTF8);
            return reader.ReadLine() ?? JsonSerializer.Serialize(new { status = "error", message = "Empty response" });
        }
        catch (TimeoutException)
        {
            return JsonSerializer.Serialize(new { status = "error", message = "Connection timed out" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    private async Task<string> ProcessCommand(string line)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var arg = parts.Length > 1 ? parts[1] : null;

        try
        {
            return cmd switch
            {
                "query" => await Query(arg ?? "workspaces"),
                "switch" => await DispatchAction(() => ExecuteSwitch(arg)),
                "set_layout" => await DispatchAction(() => ExecuteSetLayout(arg)),
                "toggle_float" => await DispatchAction(ExecuteToggleFloat),
                "toggle_fullscreen" => await DispatchAction(ExecuteToggleFullscreen),
                "close" => await DispatchAction(ExecuteCloseWindow),
                "reload" => await DispatchAction(ExecuteReloadConfig),
                "run" => await DispatchAction(() => ExecuteRun(arg)),
                "quit" => await DispatchAction(ExecuteQuit),
                _ => JsonSerializer.Serialize(new { status = "error", message = $"Unknown command: {cmd}" })
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    private async Task<string> Query(string target)
    {
        var tcs = new TaskCompletionSource<string>();
        _ = _dispatcher.BeginInvoke(() =>
        {
            try
            {
                var result = target switch
                {
                    "workspaces" => QueryWorkspaces(),
                    "windows" => QueryWindows(),
                    "layout" => QueryLayout(),
                    _ => JsonSerializer.Serialize(new { status = "error", message = $"Unknown query: {target}" })
                };
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return await tcs.Task;
    }

    private async Task<string> DispatchAction(Func<string> action)
    {
        var tcs = new TaskCompletionSource<string>();
        _ = _dispatcher.BeginInvoke(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return await tcs.Task;
    }

    private static string QueryWorkspaces()
    {
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
            return JsonSerializer.Serialize(new { status = "error", message = "WorkspaceManager not available" });

        var data = wsm.Workspaces.Select(ws => new
        {
            id = ws.Id,
            name = ws.Name,
            is_active = ws.Id == wsm.ActiveWorkspaceId,
            window_count = ws.Windows.Count,
            layout = ws.Layout.ToString(),
            windows = ws.Windows.Select(w => new { title = w.Title, process = w.ProcessName, state = w.State.ToString() })
        });
        return JsonSerializer.Serialize(new { status = "ok", data });
    }

    private static string QueryWindows()
    {
        if (!ServiceLocator.TryResolve<WindowManager>(out var wm))
            return JsonSerializer.Serialize(new { status = "error", message = "WindowManager not available" });

        var data = wm.ManagedWindows.Values.Select(w => new
        {
            hwnd = w.Hwnd.ToInt64(),
            title = w.Title,
            process = w.ProcessName,
            workspace_id = w.WorkspaceId,
            state = w.State.ToString(),
            is_active = w.IsActive,
            bounds = new { x = w.LayoutBounds.X, y = w.LayoutBounds.Y, width = w.LayoutBounds.Width, height = w.LayoutBounds.Height }
        });
        return JsonSerializer.Serialize(new { status = "ok", data });
    }

    private static string QueryLayout()
    {
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm))
            return JsonSerializer.Serialize(new { status = "error", message = "LayoutManager not available" });

        return JsonSerializer.Serialize(new
        {
            status = "ok",
            data = new
            {
                area = new { x = lm.Area.X, y = lm.Area.Y, width = lm.Area.Width, height = lm.Area.Height },
                master_factor = lm.CurrentMasterFactor
            }
        });
    }

    private static string ExecuteSwitch(string? arg)
    {
        if (!ServiceLocator.TryResolve<WorkspaceManager>(out var wsm))
            return JsonSerializer.Serialize(new { status = "error", message = "WorkspaceManager not available" });
        if (int.TryParse(arg, out int wsId) && wsId >= 1 && wsId <= 5)
        {
            wsm.SwitchToWorkspace(wsId - 1);
            return OkResult();
        }
        return JsonSerializer.Serialize(new { status = "error", message = "Usage: switch <1-5>" });
    }

    private static string ExecuteSetLayout(string? arg)
    {
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm))
            return JsonSerializer.Serialize(new { status = "error", message = "LayoutManager not available" });
        if (arg != null && Enum.TryParse<LayoutType>(arg, true, out var lt))
        {
            var ws = ServiceLocator.Resolve<WorkspaceManager>().GetActiveWorkspace();
            ws.Layout = lt;
            lm.Relayout();
            return OkResult();
        }
        return JsonSerializer.Serialize(new { status = "error", message = $"Usage: set_layout <{string.Join("|", Enum.GetNames<LayoutType>())}>" });
    }

    private static string ExecuteToggleFloat()
    {
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm))
            return JsonSerializer.Serialize(new { status = "error", message = "LayoutManager not available" });
        if (!ServiceLocator.TryResolve<FocusManager>(out var fm) || fm.ActiveWindow == null)
            return JsonSerializer.Serialize(new { status = "error", message = "No active window" });
        lm.ToggleFloating(fm.ActiveWindow.Hwnd);
        return OkResult();
    }

    private static string ExecuteToggleFullscreen()
    {
        if (!ServiceLocator.TryResolve<LayoutManager>(out var lm))
            return JsonSerializer.Serialize(new { status = "error", message = "LayoutManager not available" });
        if (!ServiceLocator.TryResolve<FocusManager>(out var fm) || fm.ActiveWindow == null)
            return JsonSerializer.Serialize(new { status = "error", message = "No active window" });
        lm.ToggleFullscreen(fm.ActiveWindow.Hwnd);
        return OkResult();
    }

    private static string ExecuteCloseWindow()
    {
        if (!ServiceLocator.TryResolve<FocusManager>(out var fm) || fm.ActiveWindow == null)
            return JsonSerializer.Serialize(new { status = "error", message = "No active window" });
        Win32.NativeMethods.PostMessage(fm.ActiveWindow.Hwnd, Win32.WindowStyles.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return OkResult();
    }

    private static string ExecuteReloadConfig()
    {
        if (ServiceLocator.TryResolve<Configuration.ConfigManager>(out var cm))
        {
            var c = cm.Load();
            ServiceLocator.Register(c);
        }
        return OkResult();
    }

    private static string ExecuteRun(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return JsonSerializer.Serialize(new { status = "error", message = "Usage: run <program>" });
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = arg,
                UseShellExecute = true
            });
            return OkResult();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    private static string ExecuteQuit()
    {
        System.Windows.Application.Current.Shutdown();
        return OkResult();
    }

    private static string OkResult() =>
        JsonSerializer.Serialize(new { status = "ok" });

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Logger.Info("IPC server stopped");
    }

    public void Dispose()
    {
        Stop();
    }
}
