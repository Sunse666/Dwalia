<p align="center">
  <img src="icon.ico" width="128" alt="Dwalia">
</p>

# Dwalia

A tiling window manager for Windows.

Dwalia automatically arranges your windows into non-overlapping tiles, managed through workspaces and keyboard-driven workflows — no mouse required.

## Features

- **8 tiling layouts**: Dynamic, MasterStack, Monocle, Grid, HorizontalStack, Columns, VerticalStack, BSP
- **Corner snap** (`Ctrl+Alt+[]'\`): Snap windows between columns in Dynamic layout
- **Resize mode** (`Alt+R`): Drag rounded rectangles to swap windows, click split edges to resize, or use `HJKL` / arrow keys
- **Virtual workspaces**: 5 configurable workspaces with independent window sets
- **Keyboard driven**: All operations via `Alt` + key chords
- **Dynamic gaps**: Adjustable inner/outer gaps in real time
- **Floating windows**: Toggle any window between tiled and floating
- **Window rules**: Auto-assign apps to workspaces, floating, fullscreen, or layout by process name / title
- **Focus highlighting**: Colored border + rounded corner overlay for the active window
- **Acrylic blur**: Optional acrylic backdrop for the desktop overlay
- **Color filter**: Subtle full-screen tint for visual cohesion across apps
- **Unlimited bar pages**: Create any number of bar pages — just add a new `bar_page` name in config
- **30+ widgets**: Volume, weather, stock, clipboard, idle time, process monitor, and more
- **Multi-monitor**: Covers the full virtual desktop
- **Zero config**: All keybindings work out of the box — no config file needed
- **YAML config**: Human-readable; edit and save to auto-reload, or press `Alt+Shift+R`
- **Auto-start**: Optional registry auto-start (`auto_start: true`)

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
| Snap window to corner | `Ctrl+Alt+[` / `]` / `'` / `\` |
| Adjust master ratio | `Alt+Ctrl+H` / `Alt+Ctrl+J` / `Alt+Ctrl+K` / `Alt+Ctrl+L` |
| Adjust gaps | `Alt+OemComma` / `Alt+OemPeriod` |
| Switch workspace | `Alt+Shift+1-5` |
| Move window to workspace | `Alt+Shift+M` / `Alt+Shift+N` |
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
  taskbar_background: "#00ffffff"
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
  font_size: 16                   # bar font size (8-24)
  bar_font: Segoe UI              # bar font family
  drag_source_color: ""           # drag source highlight (empty = use accent)
  drag_target_color: ""           # drag target highlight (empty = white)
  context_menu_background: "#2d2d2d"
  context_menu_foreground: "#cccccc"
  context_menu_border: "#444444"
  task_button_background: "#1a1a3e"
  task_button_hover_background: ""
  monitor_bar_background: "#00ffffff"
  monitor_bar_border: ""          # empty = use muted color
  workspace_pill_inactive_color: ""  # empty = use muted color
  workspace_pill_empty_color: ""     # empty = half-muted
  workspace_pill_show_count: false   # show window count beside pills
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

layout:
  inner_gap: 4
  outer_gap: 4
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
  #   sticky: true                  # show window on all workspaces

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
  binding: Alt+Shift+M
- command: move_to_workspace_previous
  binding: Alt+Shift+N
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
- command: snap_left_top
  binding: Alt+Ctrl+OemOpenBrackets
- command: snap_right_top
  binding: Alt+Ctrl+OemCloseBrackets
- command: snap_left_bottom
  binding: Alt+Ctrl+Oem7
- command: snap_right_bottom
  binding: Alt+Ctrl+Oem5

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
  monitor_mode: independent       # independent (default) | mirror | span
  monitor_bar_enabled: true       # show per-monitor bars

widgets:
  # Dock page — workspace pills, icon tray, window tabs, layout indicator
  - type: workspace
    bar_page: All
    align: left
    order: 1
    width: 0
    height: 30
    pill_color: ''
    text_color: ''
    enabled: true
    format: ''
    args: ''
    font_size: 0
  - type: window_tabs
    bar_page: Docker
    align: center
    order: 1
    width: 0
    height: 32
    pill_color: '#1a1a3e'
    text_color: '#00ccff'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: layout
    bar_page: Docker
    align: right
    order: 1

  # Info page — system stats (each widget controls its own color/enabled)
  - type: battery
    bar_page: Docker
    align: right
    order: 1
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#88ff00'
    enabled: true
    format: simple
    args: ''
    font_size: 13
  - type: wifi_ssid
    bar_page: Docker
    align: right
    order: 2
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#44ff44'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: layout
    bar_page: Docker
    align: right
    order: 3
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#ffaa00'
    enabled: true
    format: ''
    args: ''
    font_size: 14
  - type: active_window
    bar_page: Basic
    align: left
    order: 1
    width: 360
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#ffffff'
    enabled: true
    format: ''
    args: ''
    font_size: 14
  - type: clock
    bar_page: Basic
    align: center
    order: 1
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#00ffcc'
    enabled: true
    format: ''
    args: ''
    font_size: 18
  - type: media
    bar_page: Basic
    align: right
    order: 1
    width: 200
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#ff88ff'
    enabled: true
    format: ''
    args: ''
    font_size: 12
  - type: uptime
    bar_page: Basic
    align: right
    order: 2
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#8888ff'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: ip_address
    bar_page: Basic
    align: right
    order: 3
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#88aaff'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: network
    bar_page: Advanced
    align: right
    order: 1
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#00ff88'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: cpu
    bar_page: Advanced
    align: right
    order: 2
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#00ff88'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: memory
    bar_page: Advanced
    align: right
    order: 3
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#00ccff'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: disk_usage
    bar_page: Advanced
    align: right
    order: 4
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#ff8844'
    enabled: true
    format: ''
    args: C
    font_size: 13
  - type: gpu
    bar_page: Advanced
    align: right
    order: 5
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#ff4488'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: disk
    bar_page: Advanced
    align: right
    order: 6
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#ffaa44'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  - type: window_count
    bar_page: Advanced
    align: center
    order: 1
    width: 0
    height: 30
    pill_color: '#1a1a3e'
    text_color: '#88aaff'
    enabled: true
    format: ''
    args: ''
    font_size: 13
  # Per-widget options:
  #   bar_page: All | Docker | Basic | Advanced
  #   align: left | center | right
  #   order: sort priority (lower = first)
  #   enabled: true | false
  #   text_color: widget text color
  #   pill_color: widget background color
  #   width / height / font_size: custom dimensions
  #   format / args: widget-specific options
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
| `bar_next` / `bar_previous` | Cycle bar mode |
| `toggle_bar` | Show / hide the top bar |
| `snap_left_top` / `snap_right_top` | Snap window to top of left / right column |
| `snap_left_bottom` / `snap_right_bottom` | Snap window to bottom of left / right column |

### Bar Modes

Dwalia's bar is fully widget-driven with **unlimited pages**. Cycle pages with `Alt+Shift+Down` / `Alt+Shift+Up`. Each page is built dynamically by `WidgetManager` from the `widgets:` config — there are no hardcoded bar elements.

**Any `bar_page` name creates a new page.** The default config uses 5 pages:

| Page | Default content |
|---|---|
| **Docker** | Workspace pills, window tabs, layout indicator |
| **Basic** | Clock, weather, active window title, uptime, IP address |
| **Advanced** | CPU, memory, network, GPU, disk usage, window count |
| **Media** | Volume, media, microphone, Bluetooth, camera, audio device |
| **Tools** | Clipboard, idle time, process monitor, stock, todo, custom command |

Configure `bar_page` per widget to control which page it appears on. Use `bar_page: All` to show a widget on every page. The built-in widget types:

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
| `script` | Custom script output (`args` = script path) |
| `volume` | System volume level + mute status |
| `weather` | Weather via wttr.in (`args` = city name) |
| `microphone` | Microphone mute / live status |
| `bluetooth` | Bluetooth on / off indicator |
| `camera` | Camera active / off indicator |
| `audio_device` | Current audio output device name |
| `idle_time` | Time since last user input |
| `clipboard` | Clipboard text preview (last 60 chars) |
| `process_monitor` | Process running status (`args` = process name) |
| `stock` | Stock/crypto price via Yahoo Finance (`args` = symbol) |
| `todo` | Read first 3 lines of a text file (`args` = file path) |
| `custom_command` | Run a command periodically (`args` = command, `font_size` = poll interval) |
| `systray` | Quick-launch Dock. `args` = paths (newline/comma separated), auto-extracts exe icons |
| `window_controls` | Min/max/close buttons for foreground window |
| `power_menu` | Power menu. Double-click to confirm: Left=shutdown, Right=restart |
| `power_plan` | Current power plan name |
| `language` | Current input language indicator (auto-detects EN/中文/etc) |
| `recycle_bin` | Recycle bin status (file count) |
| `brightness` | Screen brightness percentage |
| `server_monitor` | Server status. `args` = URL, shows 🟢/🔴 |
| `notifications` | Notification center. Click opens Action Center |
| `pomodoro` | Pomodoro timer. `args` = work minutes, click to start/reset |
| `cava` | Volume control with expandable slider — click to expand, drag to adjust |
| `home` | Collapsible menu. `args` = path list, click to expand/collapse |

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
