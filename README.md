# Dwalia

A tiling window manager for Windows, inspired by dwm, i3, and GlazeWM.

Dwalia automatically arranges your windows into non-overlapping tiles, managed through workspaces and keyboard-driven workflows — no mouse required.

## Features

- **7 tiling layouts**: MasterStack, Monocle, Grid, HorizontalStack, Columns, VerticalStack, BSP
- **Virtual workspaces**: 5 configurable workspaces with independent window sets
- **Keyboard driven**: All operations via `Alt` + key chords (i3-style)
- **Dynamic gaps**: Adjustable inner/outer gaps in real time
- **Floating windows**: Toggle any window between tiled and floating
- **Window rules**: Auto-assign apps to workspaces or floating by process name
- **Focus highlighting**: Colored border + rounded corner overlay for the active window
- **Acrylic blur**: Optional acrylic backdrop for the desktop overlay
- **Color filter**: Subtle full-screen tint for visual cohesion across apps
- **Multi-monitor**: Covers the full virtual desktop
- **YAML config**: Human-readable, hot-reloadable configuration (`Alt+Shift+R`)
- **Launcher bar**: Quick-launch buttons for your favorite apps
- **Info bar**: System stats at a glance (clock, CPU, memory, battery)

## Quick Start

### Requirements

- Windows 10 or 11
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### Installation

```powershell
git clone https://github.com/Sunse666/Dwalia.git
cd Dwalia
dotnet build -c Release
```

Run `Dwalia.exe` from `bin/Release/net8.0-windows/`. On first launch, a default `config.yaml` is generated next to the executable.

### Basic Usage

| Action | Binding |
|---|---|
| Focus next / previous window | `Alt+J` / `Alt+K` |
| Swap window positions | `Alt+Shift+J` / `Alt+Shift+K` |
| Cycle tiling layout | `Alt+T` |
| Toggle floating | `Alt+Shift+Space` |
| Toggle fullscreen | `Alt+F` |
| Close window | `Alt+Q` |
| Adjust master ratio | `Alt+H` / `Alt+L` |
| Adjust gaps | `Alt+OemComma` / `Alt+OemPeriod` |
| Switch workspace | `Alt+Shift+1-5` |
| Move window to workspace | `Alt+Shift+N` / `Alt+Shift+M` |
| Launch terminal | `Alt+Enter` |
| Cycle bar mode | `Alt+Shift+Down` / `Alt+Shift+Up` |
| Toggle bar visibility | `Alt+U` |
| Reload config | `Alt+Shift+R` |
| Quit Dwalia | `Alt+Shift+Q` |

## Configuration

Dwalia reads `config.yaml` from the executable directory. Edit it and press `Alt+Shift+R` to apply changes without restarting.

```yaml
# Dwalia configuration — edit and press Alt+Shift+R to reload

general:
  launch_terminal: wt.exe
  excluded_processes:
    - SearchApp
    - TextInputHost
    - SystemSettings
    - ApplicationFrameHost
    - LockApp
    - shellexperiencehost

theme:
  background: "#1a1b26"
  foreground: "#c0caf5"
  accent: "#7aa2f7"
  muted: "#565f89"
  taskbar_background: "#5516161e"
  inactive_border: "#3b4261"
  active_border: "#7aa2f7"
  border_width: 2
  enable_acrylic: true
  color_filter: "#7aa2f7"
  color_filter_opacity: 0.0   # 0.0 = off, try 0.05 for subtle tint

layout:
  inner_gap: 4
  outer_gap: 2
  master_factor: 0.6          # 0.3 — 0.8
  enabled_layouts:
    - MasterStack
    - Monocle
    - Grid
    - HorizontalStack
    - Columns
    - VerticalStack
    - BSP

workspaces:
  - name: "1: Term"
  - name: "2: Code"
  - name: "3: Web"
  - name: "4: Comm"
  - name: "5: Misc"

window_rules:
  - process: chrome
    workspace: "3: Web"
  - process: code
    workspace: "2: Code"
  - process: discord
    workspace: "4: Comm"
    floating: false
  - process: snippingtool
    floating: true

keybindings:
  - command: focus_next
    binding: Alt+J
  - command: focus_previous
    binding: Alt+K
  # ... see config.yaml for the full list

launcher:
  - name: Terminal
    path: wt.exe
  - name: Chrome
    path: chrome.exe
  - name: VS Code
    path: code
  - name: Explorer
    path: explorer.exe
```

### Available Commands

| Command | Description |
|---|---|
| `focus_next` / `focus_previous` | Focus next / previous window in workspace |
| `swap_next` / `swap_previous` | Swap with next / previous window |
| `toggle_float` | Toggle focused window between tiled and floating |
| `toggle_fullscreen` | Toggle focused window fullscreen |
| `close_window` | Close focused window |
| `quit` | Exit Dwalia (restores all windows) |
| `reload_config` | Hot-reload config.yaml |
| `launch_terminal` | Launch configured terminal |
| `focus_1` — `focus_9` | Focus window by index in workspace |
| `workspace_1` — `workspace_5` | Switch to workspace |
| `workspace_next` / `workspace_previous` | Switch to next / previous workspace |
| `move_to_workspace_next` / `move_to_workspace_previous` | Move focused window to next / previous workspace |
| `cycle_layout` | Cycle through enabled layouts |
| `inc_master` / `dec_master` | Increase / decrease master area ratio |
| `inc_gap` / `dec_gap` | Increase / decrease window gaps |
| `bar_next` / `bar_previous` | Cycle bar mode (Docker → Info → Launcher) |
| `toggle_bar` | Show / hide the top bar |

### Bar Modes

Dwalia's top bar has three modes, cycled with `Alt+Shift+Down` / `Alt+Shift+Up`:

| Mode | Content |
|---|---|
| **Docker** | Workspace pills, window tabs, layout indicator |
| **Info** | Clock, CPU usage, memory usage, battery status |
| **Launcher** | Quick-launch buttons (configured under `launcher:`) |

### Layer Stack

Dwalia's desktop composition (bottom to top):

1. **Acrylic backdrop** — blurred desktop background
2. **Focus highlights** — rounded corner overlays around tiled windows
3. **Managed windows** — your applications, tiled and positioned
4. **Color filter** — optional full-screen tint for visual cohesion

## Building from Source

```powershell
git clone https://github.com/Sunse666/Dwalia.git
cd Dwalia
dotnet build -c Release
```

No external dependencies beyond .NET 8 SDK and the YamlDotNet NuGet package (restored automatically).

## How It Works

Dwalia uses:

- **WinEvent hooks** (`SetWinEventHook`) to track window creation, destruction, focus, and title changes
- **Low-level keyboard hook** (`WH_KEYBOARD_LL`) to intercept `Alt+key` chords system-wide
- **Win32 window management** (`SetWindowPos`, `ShowWindow`) to position and arrange windows
- **DWM attributes** (`DwmSetWindowAttribute`) for colored window borders and acrylic effects
- **WPF** for the overlay UI (taskbar, focus backgrounds, color filter)

When you press `Alt+J` (focus next), Dwalia intercepts the chord before any application sees it, moves focus to the next window, repaints the focus highlight, and updates the DWM border colors — all in a single frame.

## Comparison

| | Dwalia | GlazeWM | Komorebi | bug.n |
|---|---|---|---|---|
| **Language** | C# (.NET) | C# (.NET) | Rust | AHK |
| **Config** | YAML | YAML | YAML / JSON | AHK script |
| **Hot reload** | Yes (`Alt+Shift+R`) | File watcher | CLI command | Re-run script |
| **Bar** | Built-in (3 modes) | Built-in | External (yasb) | Built-in |
| **Layouts** | 7 | 3 | 3 | 4 |
| **Focus highlight** | Rounded corners | Border only | Border only | Border only |
| **Color filter** | Yes | No | No | No |
| **Windows** | 10/11 only | 10/11 only | 10/11 only | 10/11 only |

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE).

## Inspiration

- [dwm](https://dwm.suckless.org/) — the original dynamic window manager
- [i3](https://i3wm.org/) — keyboard-driven tiling with tree-based layouts
- [GlazeWM](https://github.com/glzr-io/glazewm) — clean YAML config, C# implementation
- [Komorebi](https://github.com/LGUG2Z/komorebi) — Rust-powered, bspwm for Windows
