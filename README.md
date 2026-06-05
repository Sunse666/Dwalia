# Dwalia

A tiling window manager for Windows.

Dwalia automatically arranges your windows into non-overlapping tiles, managed through workspaces and keyboard-driven workflows — no mouse required.

## Features

- **8 tiling layouts**: Dynamic, MasterStack, Monocle, Grid, HorizontalStack, Columns, VerticalStack, BSP
- **Resize mode** (`Alt+R`): Drag rounded rectangles to swap windows, click split edges to resize, or use `HJKL` / arrow keys
- **Virtual workspaces**: 5 configurable workspaces with independent window sets
- **Keyboard driven**: All operations via `Alt` + key chords
- **Dynamic gaps**: Adjustable inner/outer gaps in real time
- **Floating windows**: Toggle any window between tiled and floating
- **Window rules**: Auto-assign apps to workspaces, floating, fullscreen, or layout by process name / title
- **Focus highlighting**: Colored border + rounded corner overlay for the active window
- **Acrylic blur**: Optional acrylic backdrop for the desktop overlay
- **Color filter**: Subtle full-screen tint for visual cohesion across apps
- **Configurable bar**: Font, height, position (top/bottom), info panel options
- **Multi-monitor**: Covers the full virtual desktop
- **Zero config**: All keybindings work out of the box — no config file needed
- **YAML config**: Human-readable; edit and save to auto-reload, or press `Alt+Shift+R`
- **Launcher bar**: Quick-launch buttons for your favorite apps
- **Info bar**: System stats at a glance (clock, CPU, memory, battery) — individually toggleable

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

Run `Dwalia.exe` from `bin/Release/net8.0-windows/`. All keybindings work immediately — no configuration required. A `config.yaml` is generated on first launch for customization.

### Basic Usage

| Action | Binding |
|---|---|
| Focus window below / above | `Alt+J` / `Alt+K` |
| Focus window left / right | `Alt+H` / `Alt+L` |
| Swap with window below / above | `Alt+Shift+J` / `Alt+Shift+K` |
| Swap with window left / right | `Alt+Shift+H` / `Alt+Shift+L` |
| Cycle tiling layout | `Alt+T` |
| Toggle floating | `Alt+Shift+Space` |
| Toggle fullscreen | `Alt+F` |
| Close window | `Alt+Q` |
| Enter resize mode | `Alt+R` |
| Adjust master ratio | `Alt+Ctrl+H` / `Alt+Ctrl+J` / `Alt+Ctrl+K` / `Alt+Ctrl+L` |
| Adjust gaps | `Alt+OemComma` / `Alt+OemPeriod` |
| Switch workspace | `Alt+Shift+1-5` |
| Move window to workspace | `Alt+Shift+N` / `Alt+Shift+M` |
| Launch terminal | `Alt+Enter` |
| Cycle bar mode | `Alt+Shift+Down` / `Alt+Shift+Up` |
| Toggle bar visibility | `Alt+U` |
| Reload config | `Alt+Shift+R` |
| Quit Dwalia | `Alt+Shift+Q` |

### Resize Mode

Press `Alt+R` to enter resize mode. In this mode:

- **Drag rounded rectangles** to swap window positions (drag a window onto another)
- **Click and drag split edges** to resize — look for the cursor change at window boundaries
- **Keyboard resize**: `H`/`J`/`K`/`L` or arrow keys (configurable via `resize_mode` config)
- Press `Esc` or `Enter` to exit resize mode

## Configuration

Dwalia reads `config.yaml` from the executable directory. All keybindings have built-in defaults — the config file is only for customization. Edit and save to auto-reload, or press `Alt+Shift+R` to manually reload.

```yaml
# Dwalia configuration — edit and save to auto-reload

general:
  launch_terminal: wt.exe          # terminal command
  enable_logging: false            # set to true to write dwalia.log
  enable_swallowing: true          # auto-group child windows with parent
  animation_enabled: true          # enable layout transition animations
  animation_duration: 150          # animation duration in ms
  bar_height: 40                   # bar height in px (16-80)
  bar_position: top                # top or bottom
  default_layout: Dynamic          # initial layout for new workspaces
  startup_workspace: 0             # workspace to activate on launch (0 = none)
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
  focus_active_opacity: 0.27      # focus highlight opacity for active window
  focus_inactive_opacity: 0.09    # focus highlight opacity for inactive windows
  focus_radius: 8                 # corner radius for focus highlight
  focus_fill: true                # true = filled highlight, false = border only
  focus_follows_mouse: false      # auto-focus window under cursor
  color_filter: "#7aa2f7"
  color_filter_opacity: 0.0       # 0.0 = off, try 0.05 for subtle tint
  font_size: 11                   # bar font size (8-24)
  bar_font: Segoe UI              # bar font family
  status_show_clock: true         # show clock in info bar
  status_show_cpu: true           # show CPU usage in info bar
  status_show_mem: true           # show memory usage in info bar
  status_show_battery: true       # show battery in info bar
  drag_source_color: ""           # drag source highlight (empty = use accent)
  drag_target_color: ""           # drag target highlight (empty = white)
  context_menu_background: "#2d2d2d"
  context_menu_foreground: "#cccccc"
  context_menu_border: "#444444"
  task_button_background: "#24283e"
  task_button_hover_background: ""
  monitor_bar_background: "#5516161e"
  monitor_bar_border: ""          # empty = use muted color
  workspace_pill_inactive_color: ""  # empty = use muted color
  workspace_pill_empty_color: ""     # empty = half-muted
  workspace_pill_show_count: false   # show window count beside pills
  widget_pill_background: "#1a1a3e"  # default widget pill background
  widget_cpu_color: "#00ff88"
  widget_mem_color: "#00ccff"
  widget_battery_color: "#88ff00"
  widget_network_down_color: "#00ff88"
  widget_network_up_color: "#ff4444"
  widget_media_color: "#ff88ff"
  widget_separator_color: "#334466"
  media_dot_playing_color: "#00ff88"
  media_dot_paused_color: "#334466"
  bar_border_color: "#3b4261"
  pill_corner_radius: 14
  pill_height: 30
  task_pill_corner_radius: 14
  task_pill_height: 32
  task_hover_brighten: 20
  progress_filled_char: "▰"
  progress_empty_char: "▱"
  marquee_speed: 25              # media text scroll speed
  media_script: ""               # custom media info script path
  media_script_interval: 3       # script polling interval (seconds)
  date_format: "HH:mm:ss  yyyy-MM-dd"
  status_show_network: true
  status_show_media: true

layout:
  inner_gap: 4
  outer_gap: 2
  master_factor: 0.6              # 0.3 — 0.8
  smart_gaps: false               # remove gaps when only one window
  enabled_layouts:
    - Dynamic
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
  # Advanced rule examples:
  # - process: firefox
  #   title: YouTube
  #   title_match_mode: contains    # Exact (default), Contains, StartsWith, Regex
  #   workspace: "3: Web"
  #   fullscreen: true              # launch in fullscreen
  #   layout: Monocle               # set layout for this window's workspace
  #   monitor: 1                    # assign to specific monitor

keybindings:
- command: focus_down
  binding: Alt+J
- command: focus_up
  binding: Alt+K
- command: focus_left
  binding: Alt+H
- command: focus_right
  binding: Alt+L
- command: swap_down
  binding: Alt+Shift+J
- command: swap_up
  binding: Alt+Shift+K
- command: swap_left
  binding: Alt+Shift+H
- command: swap_right
  binding: Alt+Shift+L
- command: toggle_fullscreen
  binding: Alt+F
- command: cycle_layout
  binding: Alt+T
- command: toggle_float
  binding: Alt+Shift+Space
- command: close_window
  binding: Alt+Q
- command: quit
  binding: Alt+Shift+Q
- command: dec_master
  binding: Alt+OemOpenBrackets
- command: inc_master
  binding: Alt+OemCloseBrackets
- command: dec_gap
  binding: Alt+OemComma
- command: inc_gap
  binding: Alt+OemPeriod
- command: focus_1
  binding: Alt+1
- command: focus_2
  binding: Alt+2
- command: focus_3
  binding: Alt+3
- command: focus_4
  binding: Alt+4
- command: focus_5
  binding: Alt+5
- command: focus_6
  binding: Alt+6
- command: focus_7
  binding: Alt+7
- command: focus_8
  binding: Alt+8
- command: focus_9
  binding: Alt+9
- command: workspace_1
  binding: Alt+Shift+1
- command: workspace_2
  binding: Alt+Shift+2
- command: workspace_3
  binding: Alt+Shift+3
- command: workspace_4
  binding: Alt+Shift+4
- command: workspace_5
  binding: Alt+Shift+5
- command: workspace_next
  binding: Alt+Shift+Right
- command: workspace_previous
  binding: Alt+Shift+Left
- command: move_to_workspace_next
  binding: Alt+Shift+N
- command: move_to_workspace_previous
  binding: Alt+Shift+M
- command: launch_terminal
  binding: Alt+Enter
- command: toggle_bar
  binding: Alt+U
- command: bar_next
  binding: Alt+Shift+Down
- command: bar_previous
  binding: Alt+Shift+Up
- command: reload_config
  binding: Alt+Shift+R

launcher:
  - name: Terminal
    path: wt.exe
  - name: Chrome
    path: chrome.exe
  - name: VS Code
    path: code
  - name: Explorer
    path: explorer.exe

autostart:
  - name: Terminal
    command: wt.exe

# Resize mode key customization
resize_mode:
  resize_left: H                  # resize left key (also arrow keys always work)
  resize_down: J                  # resize down key
  resize_up: K                    # resize up key
  resize_right: L                 # resize right key

# Multi-monitor settings
monitor:
  monitor_bar_enabled: true       # show per-monitor bars

widgets:
  - type: workspace               # BarPage: All = shown on every page
    bar_page: All
    align: left
    order: 1
  - type: layout                  # BarPage: Docker = only on Docker page
    bar_page: Docker
    align: right
    order: 2
  - type: clock
    bar_page: Basic
    align: center
    order: 1
  # Widget options:
  #   bar_page: All | Docker | Basic | Advanced
  #   align: left | center | right
  #   order: sort priority (lower = first)
  #   width / height / font_size: custom dimensions
  #   pill_color / text_color: custom colors
  #   format / args: widget-specific options
  #   enabled: true | false
```

### Available Commands

| Command | Description |
|---|---|
| `focus_down` / `focus_up` | Focus window below / above current |
| `focus_left` / `focus_right` | Focus window left / right of current |
| `swap_down` / `swap_up` | Swap with window below / above |
| `swap_left` / `swap_right` | Swap with window left / right |
| `toggle_float` | Toggle focused window between tiled and floating |
| `toggle_fullscreen` | Toggle focused window fullscreen |
| `toggle_scratchpad` | Toggle scratchpad visibility |
| `toggle_sticky` | Toggle window stickiness across workspaces |
| `close_window` | Close focused window |
| `quit` | Exit Dwalia (restores all windows) |
| `reload_config` | Hot-reload config.yaml |
| `launch_terminal` | Launch configured terminal |
| `activate_window` | Open window switcher / launcher |
| `enter_resize_mode` / `exit_resize_mode` | Enter / exit resize mode |
| `resize_left` / `resize_down` / `resize_up` / `resize_right` | Resize in direction |
| `focus_1` — `focus_9` | Focus window by index in workspace |
| `workspace_1` — `workspace_5` | Switch to workspace |
| `workspace_next` / `workspace_previous` | Switch to next / previous workspace |
| `move_to_workspace_next` / `move_to_workspace_previous` | Move focused window to next / previous workspace |
| `cycle_layout` / `cycle_layout_previous` | Cycle through enabled layouts |
| `inc_master` / `dec_master` | Increase / decrease master area ratio |
| `inc_gap` / `dec_gap` | Increase / decrease window gaps |
| `inc_inner_gap` / `dec_inner_gap` | Adjust inner gap only |
| `inc_outer_gap` / `dec_outer_gap` | Adjust outer gap only |
| `bar_next` / `bar_previous` | Cycle bar mode (Docker → Info → Launcher) |
| `toggle_bar` | Show / hide the top bar |

### Bar Modes

Dwalia's bar is widget-driven and has 3 pages, cycled with `Alt+Shift+Down` / `Alt+Shift+Up`:

| Page | Content |
|---|---|
| **Docker** | Workspace pills, window tabs, layout indicator |
| **Basic** | Active window title, clock, CPU, memory, battery, network, media |
| **Advanced** | Window count, CPU, memory, network, GPU, disk usage, disk I/O |

Each page is composed of widgets configured under `widgets:`. The built-in widget types:

| Widget | Description |
|---|---|
| `workspace` | Workspace indicator pills |
| `window_tabs` | Per-window tabs with title and close button |
| `active_window` | Active window title |
| `layout` | Current layout name / master factor |
| `clock` | Date and time |
| `time_only` | Time only |
| `date_only` | Date only |
| `cpu` | CPU usage with progress bar |
| `memory` | Memory usage with progress bar |
| `battery` | Battery level / AC status |
| `network` | Network download / upload speed |
| `media` | Now-playing media info with marquee |
| `gpu` | GPU usage |
| `disk` | Disk read / write speed |
| `disk_usage` | Disk free / total space |
| `uptime` | System uptime |
| `wifi_ssid` | Wi-Fi SSID |
| `ip_address` | Local IP address |
| `public_ip` | Public IP (from ipify) |
| `window_count` | Window count in active workspace |
| `world_clock` | Multiple timezone clocks (comma-separated `args`) |
| `countdown` | Countdown to a target date (`args`) |
| `launcher` | Quick-launch app buttons |
| `label` | Static text label (`args`) |
| `button` | Clickable button with text (`args`) |
| `script` | Custom script output |

Configure under `theme:`:
- `font_size` / `bar_font` — customize bar typography
- `bar_height` / `bar_position` — customize bar dimensions and placement
- `widget_pill_background` — default widget pill background
- `widget_cpu_color` / `widget_mem_color` / `widget_battery_color` — widget text colors
- `widget_network_down_color` / `widget_network_up_color` / `widget_media_color` — network & media colors
- `pill_corner_radius` / `pill_height` / `task_pill_corner_radius` / `task_pill_height` — pill styling
- `progress_filled_char` / `progress_empty_char` — CPU/memory progress bar characters
- `marquee_speed` — media text scroll speed
- `media_script` — custom media info script path
- `workspace_pill_show_count` — show window count next to workspace pills
- `status_show_network` / `status_show_media` — toggle network and media widgets
- `date_format` — clock date/time format string

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
- **Low-level mouse hook** (`WH_MOUSE_LL`) for resize mode — drag-to-swap and click-to-resize
- **Win32 window management** (`SetWindowPos`, `ShowWindow`) to position and arrange windows
- **DWM attributes** (`DwmSetWindowAttribute`) for colored window borders and acrylic effects
- **WPF** for the overlay UI (taskbar, focus backgrounds, color filter)

When you press `Alt+J` (focus below), Dwalia intercepts the chord before any application sees it, moves focus to the window directly below, repaints the focus highlight, and updates the DWM border colors — all in a single frame.

## Comparison

| | Dwalia | GlazeWM | Komorebi | bug.n |
|---|---|---|---|---|
| **Language** | C# (.NET) | C# (.NET) | Rust | AHK |
| **Config** | YAML | YAML | YAML / JSON | AHK script |
| **Hot reload** | Auto (file watcher) + manual | File watcher | CLI command | Re-run script |
| **Bar** | Built-in (3 modes) | Built-in | External (yasb) | Built-in |
| **Layouts** | 8 | 3 | 3 | 4 |
| **Resize mode** | Mouse + keyboard | Keyboard only | Keyboard only | Keyboard only |
| **Window swap** | Drag & drop | Keyboard only | Keyboard only | Keyboard only |
| **Focus highlight** | Rounded corners | Border only | Border only | Border only |
| **Color filter** | Yes | No | No | No |
| **Windows** | 10/11 only | 10/11 only | 10/11 only | 10/11 only |

## License

[LICENSE](LICENSE)
