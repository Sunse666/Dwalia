<p align="center">
  <img src="icon.ico" width="128" alt="Dwalia">
</p>

# Dwalia

Windows 平铺窗口管理器。

自动将窗口排列成无重叠的瓦片布局，通过工作区管理和纯键盘操作来控制窗口——无需鼠标。

## 特性

- **8 种平铺布局**: Dynamic、MasterStack、Monocle、Grid、HorizontalStack、Columns、VerticalStack、BSP
- **窗口压角** (`Ctrl+Alt+[]'\`)：Dynamic 布局下窗口跨列插入/创列
- **Resize 模式** (`Alt+R`)：拖拽圆角矩形交换窗口位置，点击边界调整大小，HJKL / 方向键键盘微调
- **虚拟工作区**: 5 个可配置的工作区，各自独立的窗口集合
- **纯键盘驱动**: 所有操作通过 `Alt` + 按键组合完成
- **动态间距**: 可实时调整内边距和外边距
- **浮动窗口**: 任意窗口在平铺和浮动间切换
- **窗口规则**: 按进程名/标题自动分配工作区、浮动、全屏或指定布局
- **焦点高亮**: 活动窗口的彩色边框 + 圆角矩形覆盖层
- **亚克力模糊**: 可选的毛玻璃桌面背景
- **颜色滤镜**: 可选的全屏微调色彩，统一不同应用的视觉风格
- **无限状态栏页**: 任意 `bar_page` 名称自动创建新页，无限扩展
- **30+ 控件**: 音量、天气、股票、剪贴板、空闲时间、进程监控等
- **多显示器**: 覆盖完整虚拟桌面
- **零配置**: 所有键位开箱即用 — 无需配置文件
- **YAML 配置**: 可读性强；编辑保存自动热重载，或按 `Alt+Shift+R`
- **开机自启**: 可选注册表自启 (`auto_start: true`)

## 快速开始

### 环境要求

- Windows 10 或 11
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### 安装

```powershell
git clone https://github.com/Sunse666/Dwalia.git
cd Dwalia
dotnet build -c Release
```

运行 `bin/Release/net8.0-windows/Dwalia.exe`。所有键位开箱即用 — 无需配置文件。首次启动会生成 `config.yaml` 供自定义修改。

### 基本操作

| 操作 | 快捷键 |
|---|---|
| 聚焦下方/上方窗口 | `Alt+J` / `Alt+K` |
| 聚焦左方/右方窗口 | `Alt+H` / `Alt+L` |
| 与下方/上方窗口交换 | `Alt+Shift+J` / `Alt+Shift+K` |
| 与左方/右方窗口交换 | `Alt+Shift+H` / `Alt+Shift+L` |
| 切换平铺布局 | `Alt+T` |
| 切换浮动 | `Alt+Shift+Space` |
| 切换全屏 | `Alt+F` |
| 关闭窗口 | `Alt+Q` |
| 进入 resize 模式 | `Alt+R` |
| 窗口压角 | `Ctrl+Alt+[` / `]` / `'` / `\` |
| 调整主区域比例 | `Alt+Ctrl+H` / `Alt+Ctrl+J` / `Alt+Ctrl+K` / `Alt+Ctrl+L` |
| 调整窗口间距 | `Alt+,` / `Alt+.` |
| 切换工作区 | `Alt+Shift+1-5` |
| 移动窗口到工作区 | `Alt+Shift+M` / `Alt+Shift+N` |
| 启动终端 | `Alt+Enter` |
| 切换状态栏模式 | `Alt+Shift+Down` / `Alt+Shift+Up` |
| 隐藏/显示状态栏 | `Alt+U` |
| 热重载配置 | `Alt+Shift+R` |
| 退出 Dwalia | `Alt+Shift+Q` |

### Resize 模式

按 `Alt+R` 进入 resize 模式，此时：

- **拖拽圆角矩形** 交换窗口位置（把窗口 A 拖到 B 上即可互换）
- **点击边界拖拽** 调整窗口大小 — 鼠标移到窗口边界时指针会变化
- **键盘微调**: `H`/`J`/`K`/`L` 或方向键（可通过 `resize_mode` 配置自定义）
- 按 `Esc` 或 `Enter` 退出 resize 模式

## 配置文件

Dwalia 从程序目录读取 `config.yaml`。所有键位都有内置默认值 —— 配置文件仅用于自定义。编辑保存后自动热重载，或按 `Alt+Shift+R` 手动重载。

```yaml
# Dwalia 配置文件 — 编辑保存后自动热重载

general:
  launch_terminal: wt.exe          # 终端命令
  enable_logging: false            # 设 true 以启用日志文件
  enable_swallowing: true          # 自动将子窗口归组到父窗口
  animation_enabled: true          # 布局切换动画
  animation_duration: 150          # 动画时长（毫秒）
  bar_height: 40                   # 状态栏高度 px（16-80）
  bar_position: top                # 状态栏位置 top 或 bottom
  default_layout: Dynamic          # 新工作区默认布局
  startup_workspace: 0             # 启动时激活的工作区（0=不切换）
  excluded_processes:              # 不纳入管理的进程
    - SearchApp
    - TextInputHost
    - SystemSettings
    - ApplicationFrameHost
    - LockApp
    - shellexperiencehost

theme:
  background: "#1a1b26"            # 桌面覆盖层背景色
  foreground: "#c0caf5"            # 任务栏文字色
  accent: "#7aa2f7"                # 强调色（焦点高亮、活动指示器）
  muted: "#565f89"                 # 次要色（布局标签、非活动指示器）
  taskbar_background: "#00ffffff"  # 状态栏背景色（含透明度）
  inactive_border: "#3b4261"       # 非活动窗口边框色
  active_border: "#7aa2f7"         # 活动窗口边框色
  border_width: 2                  # 边框宽度（1-8）
  enable_acrylic: true             # 启用毛玻璃效果
  focus_active_opacity: 0.27      # 焦点高亮-活动窗口透明度
  focus_inactive_opacity: 0.09    # 焦点高亮-非活动窗口透明度
  focus_radius: 8                 # 焦点高亮圆角半径
  focus_fill: true                # true=填充模式, false=仅边框
  focus_follows_mouse: false      # 鼠标跟随聚焦
  color_filter: "#7aa2f7"          # 全局颜色滤镜
  color_filter_opacity: 0.0        # 滤镜透明度（0.0=关闭，建议 0.05）
  font_size: 16                    # 状态栏字体大小（8-24）
  bar_font: Segoe UI               # 状态栏字体族
  drag_source_color: ""            # 拖拽源高亮色（空=使用 accent 色）
  drag_target_color: ""            # 拖拽目标高亮色（空=白色）
  context_menu_background: "#2d2d2d"  # 右键菜单背景
  context_menu_foreground: "#cccccc"  # 右键菜单文字
  context_menu_border: "#444444"      # 右键菜单边框
  task_button_background: "#1a1a3e"   # 任务栏按钮背景
  task_button_hover_background: ""    # 任务栏按钮悬停背景（空=使用背景色）
  monitor_bar_background: "#00ffffff" # 多显示器工具栏背景
  monitor_bar_border: ""              # 多显示器工具栏边框（空=使用 muted 色）
  workspace_pill_inactive_color: ""   # 非活动工作区指示器
  workspace_pill_empty_color: ""      # 空工作区指示器
  workspace_pill_show_count: false    # 工作区胶囊旁显示窗口计数
  widget_separator_color: "#334466"    # widget 分隔符颜色
  media_dot_playing_color: "#00ff88"   # 媒体播放指示灯色
  media_dot_paused_color: "#334466"    # 媒体暂停指示灯色
  bar_border_color: "#3b4261"          # 状态栏边框色
  pill_corner_radius: 14               # 胶囊圆角
  pill_height: 30                      # 胶囊高度
  task_pill_corner_radius: 14          # 任务标签圆角
  task_pill_height: 32                 # 任务标签高度
  task_hover_brighten: 20              # 悬停增亮值
  progress_filled_char: "▰"            # 进度条实心字符
  progress_empty_char: "▱"             # 进度条空心字符
  marquee_speed: 25                    # 媒体文字滚动速度
  media_script: ""                     # 自定义媒体信息脚本路径
  media_script_interval: 3             # 脚本轮询间隔（秒）
  date_format: "HH:mm:ss  yyyy-MM-dd"  # 时钟日期格式

layout:
  inner_gap: 4                     # 窗口内边距
  outer_gap: 4                     # 屏幕外边距
  master_factor: 0.6               # 主区域比例（0.3-0.8）
  smart_gaps: false                # 单窗口时去掉间距
  enabled_layouts:                 # 启用的布局（Alt+T 循环切换）
    - Dynamic
    - MasterStack
    - Monocle
    - Grid
    - HorizontalStack
    - Columns
    - VerticalStack
    - BSP

workspaces:
  - name: "1: Term"                # 工作区 1
  - name: "2: Code"                # 工作区 2
  - name: "3: Web"                 # 工作区 3
  - name: "4: Comm"                # 工作区 4
  - name: "5: Misc"                # 工作区 5

window_rules:
  - process: chrome                # Chrome → Web 工作区
    workspace: "3: Web"
  - process: code                  # VS Code → Code 工作区
    workspace: "2: Code"
  - process: discord               # Discord → Comm 工作区
    workspace: "4: Comm"
    floating: false
  - process: snippingtool          # 截图工具 → 浮动
    floating: true
  # 高级规则示例:
  # - process: firefox
  #   title: YouTube
  #   title_match_mode: contains    # Exact (默认), Contains, StartsWith, Regex
  #   workspace: "3: Web"
  #   fullscreen: true              # 启动即全屏
  #   layout: Monocle               # 为该窗口工作区设置布局
  #   monitor: 1                    # 分配到指定显示器
  #   sticky: true                  # 在所有工作区显示

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
  - name: 终端
    path: wt.exe
  - name: Chrome
    path: chrome.exe
  - name: VS Code
    path: code
  - name: 资源管理器
    path: explorer.exe

autostart:
  - name: 终端
    command: wt.exe

# Resize 模式键位自定义
resize_mode:
  resize_left: H                   # resize 左移键（方向键始终可用）
  resize_down: J                   # resize 下移键
  resize_up: K                     # resize 上移键
  resize_right: L                  # resize 右移键

# 多显示器设置
monitor:
  monitor_mode: independent        # independent (默认) | mirror | span
  monitor_bar_enabled: true        # 非主屏显示辅助工具栏

widgets:
  # 停靠页 — 工作区指示器、窗口图标托盘、窗口标签、布局信息
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
  - type: taskbar
    bar_page: Docker
    align: left
    order: 2
    height: 30
    args: ''                       # "all" = 显示全部工作区窗口
  - type: systray
    bar_page: Docker
    align: left
    order: 0
    height: 28
    args: |
      wt.exe
      chrome.exe
      code
      explorer.exe                 # 快捷启动栏，自动取 exe 图标
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

  # 信息页 — 系统状态（每个 widget 单独控制颜色和开关）
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
  # 每个 widget 可选参数：
  #   bar_page: All | Docker | Basic | Advanced
  #   align: left | center | right
  #   order: 排序优先级（越小越靠前）
  #   enabled: true | false
  #   text_color: 文字颜色
  #   pill_color: 胶囊背景色
  #   width / height / font_size: 自定义尺寸
  #   format / args: widget 特定选项
```

### 全部命令

| 命令 | 说明 |
|---|---|
| `focus_down` / `focus_up` | 聚焦下方 / 上方窗口 |
| `focus_left` / `focus_right` | 聚焦左方 / 右方窗口 |
| `swap_down` / `swap_up` | 与下方 / 上方窗口交换位置 |
| `swap_left` / `swap_right` | 与左方 / 右方窗口交换位置 |
| `toggle_float` | 切换焦点窗口的平铺 / 浮动状态 |
| `toggle_fullscreen` | 切换焦点窗口全屏 |
| `toggle_scratchpad` | 切换草稿本显示/隐藏 |
| `toggle_sticky` | 切换窗口跨工作区粘滞 |
| `close_window` | 关闭焦点窗口 |
| `quit` | 退出 Dwalia（恢复所有窗口原始位置） |
| `reload_config` | 热重载 config.yaml |
| `launch_terminal` | 启动配置的终端 |
| `activate_window` | 打开窗口切换器 / 启动器 |
| `enter_resize_mode` / `exit_resize_mode` | 进入 / 退出 resize 模式 |
| `resize_left` / `resize_down` / `resize_up` / `resize_right` | 向指定方向调整大小 |
| `focus_1` — `focus_9` | 按索引聚焦工作区内的窗口 |
| `workspace_1` — `workspace_5` | 切换到指定工作区 |
| `workspace_next` / `workspace_previous` | 切换到下一个 / 上一个工作区 |
| `move_to_workspace_next` / `move_to_workspace_previous` | 移动焦点窗口到下一个 / 上一个工作区 |
| `cycle_layout` / `cycle_layout_previous` | 循环切换启用的布局 |
| `inc_master` / `dec_master` | 增加 / 减少主区域比例 |
| `inc_gap` / `dec_gap` | 增加 / 减少窗口间距 |
| `inc_inner_gap` / `dec_inner_gap` | 仅调整内边距 |
| `inc_outer_gap` / `dec_outer_gap` | 仅调整外边距 |
| `bar_next` / `bar_previous` | 状态栏模式切换 |
| `toggle_bar` | 显示 / 隐藏状态栏 |
| `snap_left_top` / `snap_right_top` | 将窗口压到左列 / 右列最上 |
| `snap_left_bottom` / `snap_right_bottom` | 将窗口压到左列 / 右列最下 |

### 状态栏模式

状态栏完全由 Widget 驱动，**页数无上限**。`Alt+Shift+Down` / `Alt+Shift+Up` 循环切换。每页由 `WidgetManager` 根据 `widgets:` 配置动态构建——无硬编码栏元素。

**任意 `bar_page` 名称自动创建新页。** 默认配置使用 5 页：

| 页面 | 默认内容 |
|---|---|
| **Docker** | 工作区指示器、窗口标签、布局名称 |
| **Basic** | 时钟、天气、活动窗口标题、运行时间、IP 地址 |
| **Advanced** | CPU、内存、网络、GPU、磁盘使用率、窗口数 |
| **Media** | 音量、媒体、麦克风、蓝牙、摄像头、音频设备 |
| **Tools** | 剪贴板、空闲时间、进程监控、股票、待办、自定义命令 |

通过每个 widget 的 `bar_page` 控制显示在哪一页。`bar_page: All` 在所有页面显示。所有支持的 widget 类型：

| Widget | 说明 |
|---|---|
| `workspace` | 工作区指示胶囊 |
| `window_tabs` | 窗口标签页（带标题和关闭按钮） |
| `active_window` | 当前活动窗口标题 |
| `layout` | 当前布局名称 / 主区域比例 |
| `clock` | 日期和时间 |
| `time_only` | 仅时间 |
| `date_only` | 仅日期 |
| `cpu` | CPU 使用率（带进度条） |
| `memory` | 内存使用率（带进度条） |
| `battery` | 电池电量 / 交流电源状态 |
| `network` | 网络下载 / 上传速度 |
| `media` | 当前播放媒体信息（带滚动字幕） |
| `gpu` | GPU 使用率 |
| `disk` | 磁盘读写速度 |
| `disk_usage` | 磁盘空闲 / 总空间 |
| `uptime` | 系统运行时间 |
| `wifi_ssid` | Wi-Fi SSID |
| `ip_address` | 本地 IP 地址 |
| `public_ip` | 公网 IP（通过 ipify） |
| `window_count` | 当前工作区窗口数量 |
| `world_clock` | 多时区时钟（逗号分隔 `args`） |
| `countdown` | 目标日期倒计时（`args`） |
| `launcher` | 快捷启动按钮 |
| `label` | 静态文本标签（`args`） |
| `button` | 可点击按钮（`args`） |
| `script` | 自定义脚本输出（`args` = 脚本路径） |
| `volume` | 系统音量 + 静音状态 |
| `weather` | 天气（wttr.in，`args` = 城市名） |
| `microphone` | 麦克风静音 / 活跃状态 |
| `bluetooth` | 蓝牙开关指示 |
| `camera` | 摄像头活跃 / 关闭指示 |
| `audio_device` | 当前音频输出设备名称 |
| `idle_time` | 距上次输入的空闲时间 |
| `clipboard` | 剪贴板文字预览（最多 60 字符） |
| `process_monitor` | 进程运行状态（`args` = 进程名） |
| `stock` | 股票/加密货币价格（`args` = 代码） |
| `todo` | 读取文本文件前 3 行（`args` = 文件路径） |
| `custom_command` | 定时执行命令（`args` = 命令，`font_size` = 轮询间隔秒数） |
| `taskbar` | 窗口图标托盘。左键聚焦、右键菜单、中键关闭。`args`: 默认=当前工作区, `"all"`=全部工作区, `"hidden"`=隐藏窗口 |
| `systray` | 快捷启动栏 Dock。`args` 中配置路径（换行/逗号分割），自动读取 exe 图标 |

在 `theme:` 下可配置：
- `font_size` / `bar_font` — 自定义字体
- `bar_height` / `bar_position` — 状态栏尺寸与位置
- `widget_pill_background` — 默认 widget 胶囊背景色
- `widget_cpu_color` / `widget_mem_color` / `widget_battery_color` — widget 文字颜色
- `widget_network_down_color` / `widget_network_up_color` / `widget_media_color` — 网络与媒体颜色
- `pill_corner_radius` / `pill_height` / `task_pill_corner_radius` / `task_pill_height` — 胶囊样式
- `progress_filled_char` / `progress_empty_char` — CPU/内存进度条字符
- `marquee_speed` — 媒体文字滚动速度
- `media_script` — 自定义媒体信息脚本路径
- `workspace_pill_show_count` — 工作区胶囊旁显示窗口计数
- `status_show_network` / `status_show_media` — 网络和媒体 widget 开关
- `date_format` — 时钟日期格式字符串

### 图层结构

Dwalia 桌面渲染层次（从底到顶）：

1. **亚克力背景** — 模糊的桌面覆盖层
2. **焦点高亮** — 平铺窗口周围的圆角矩形覆盖层
3. **管理窗口** — 你的应用程序，被平铺排列
4. **颜色滤镜** — 可选的全屏统一色调覆盖层

## 从源码构建

```powershell
git clone https://github.com/Sunse666/Dwalia.git
cd Dwalia
dotnet build -c Release
```

除 .NET 8 SDK 和 YamlDotNet NuGet 包（自动还原）外无外部依赖。

## 技术原理

Dwalia 使用了以下 Windows API：

- **WinEvent 钩子** (`SetWinEventHook`) — 追踪窗口创建、销毁、焦点和标题变化
- **低级键盘钩子** (`WH_KEYBOARD_LL`) — 系统级拦截 `Alt+按键` 组合
- **低级鼠标钩子** (`WH_MOUSE_LL`) — resize 模式下的拖拽交换和边界拖拽调整
- **Win32 窗口管理** (`SetWindowPos`、`ShowWindow`) — 定位和排列窗口
- **DWM 属性** (`DwmSetWindowAttribute`) — 彩色窗口边框和亚克力效果
- **WPF** — 覆盖层 UI（状态栏、焦点背景、颜色滤镜）

当你按下 `Alt+J`（聚焦下方窗口）时，Dwalia 在任何应用感知之前就拦截了按键组合，立即把焦点移到正下方的窗口，重新渲染焦点高亮，并更新 DWM 边框颜色——这一切都在一帧内完成。

## 同类软件对比

| | Dwalia | GlazeWM | Komorebi | bug.n |
|---|---|---|---|---|
| **语言** | C# (.NET) | C# (.NET) | Rust | AHK |
| **配置格式** | YAML | YAML | YAML / JSON | AHK 脚本 |
| **热重载** | 自动（文件监听）+ 手动 | 文件监听 | CLI 命令 | 重新运行脚本 |
| **状态栏** | 内置（3 种模式） | 内置 | 外部 (yasb) | 内置 |
| **布局数量** | 8 | 3 | 3 | 4 |
| **Resize 模式** | 鼠标 + 键盘 | 仅键盘 | 仅键盘 | 仅键盘 |
| **窗口交换** | 拖拽交换 | 仅键盘 | 仅键盘 | 仅键盘 |
| **焦点高亮** | 圆角矩形 | 仅边框 | 仅边框 | 仅边框 |
| **颜色滤镜** | 支持 | 无 | 无 | 无 |
| **平台** | Windows 10/11 | Windows 10/11 | Windows 10/11 | Windows 10/11 |

## 许可证

[LICENSE](LICENSE)
