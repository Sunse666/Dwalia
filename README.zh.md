# Dwalia

Windows 平铺窗口管理器。

自动将窗口排列成无重叠的瓦片布局，通过工作区管理和纯键盘操作来控制窗口——无需鼠标。

## 特性

- **7 种平铺布局**: MasterStack、Monocle、Grid、HorizontalStack、Columns、VerticalStack、BSP
- **虚拟工作区**: 5 个可配置的工作区，各自独立的窗口集合
- **纯键盘驱动**: 所有操作通过 `Alt` + 按键组合完成
- **动态间距**: 可实时调整内边距和外边距
- **浮动窗口**: 任意窗口在平铺和浮动间切换
- **窗口规则**: 按进程名自动分配工作区或设为浮动
- **焦点高亮**: 活动窗口的彩色边框 + 圆角矩形覆盖层
- **亚克力模糊**: 可选的毛玻璃桌面背景
- **颜色滤镜**: 可选的全屏微调色彩，统一不同应用的视觉风格
- **多显示器**: 覆盖完整虚拟桌面
- **零配置**: 所有键位开箱即用 — 无需配置文件
- **YAML 配置**: 可读性强；编辑保存自动热重载，或按 `Alt+Shift+R`
- **启动器栏**: 常用应用的快捷启动按钮
- **信息栏**: 系统状态一览（时钟、CPU、内存、电池）

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
| 调整主区域比例 | `Alt+[` / `Alt+]` |
| 调整窗口间距 | `Alt+,` / `Alt+.` |
| 切换工作区 | `Alt+Shift+1-5` |
| 移动窗口到工作区 | `Alt+Shift+N` / `Alt+Shift+M` |
| 启动终端 | `Alt+Enter` |
| 切换状态栏模式 | `Alt+Shift+Down` / `Alt+Shift+Up` |
| 隐藏/显示状态栏 | `Alt+U` |
| 热重载配置 | `Alt+Shift+R` |
| 退出 Dwalia | `Alt+Shift+Q` |

## 配置文件

Dwalia 从程序目录读取 `config.yaml`。所有键位都有内置默认值 —— 配置文件仅用于自定义。编辑保存后自动热重载，或按 `Alt+Shift+R` 手动重载。

```yaml
# Dwalia 配置文件 — 编辑保存后自动热重载

general:
  launch_terminal: wt.exe          # 终端命令
  enable_logging: false            # 设 true 以启用日志文件
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
  taskbar_background: "#5516161e"  # 状态栏背景色（含透明度）
  inactive_border: "#3b4261"       # 非活动窗口边框色
  active_border: "#7aa2f7"         # 活动窗口边框色
  border_width: 2                  # 边框宽度（1-8）
  enable_acrylic: true             # 启用毛玻璃效果
  focus_active_opacity: 0.27      # 焦点高亮-活动窗口透明度
  focus_inactive_opacity: 0.09    # 焦点高亮-非活动窗口透明度
  focus_radius: 8                 # 焦点高亮圆角半径
  focus_fill: true                # true=填充模式, false=仅边框
  color_filter: "#7aa2f7"          # 全局颜色滤镜
  color_filter_opacity: 0.0        # 滤镜透明度（0.0=关闭，建议 0.05）

layout:
  inner_gap: 4                     # 窗口内边距
  outer_gap: 2                     # 屏幕外边距
  master_factor: 0.6               # 主区域比例（0.3-0.8）
  enabled_layouts:                 # 启用的布局（Alt+T 循环切换）
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
  - command: dec_master
    binding: Alt+OemOpenBrackets
  - command: inc_master
    binding: Alt+OemCloseBrackets
  # ... 完整列表见生成的 config.yaml

launcher:
  - name: 终端
    path: wt.exe
  - name: Chrome
    path: chrome.exe
  - name: VS Code
    path: code
  - name: 资源管理器
    path: explorer.exe
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
| `close_window` | 关闭焦点窗口 |
| `quit` | 退出 Dwalia（恢复所有窗口原始位置） |
| `reload_config` | 热重载 config.yaml |
| `launch_terminal` | 启动配置的终端 |
| `focus_1` — `focus_9` | 按索引聚焦工作区内的窗口 |
| `workspace_1` — `workspace_5` | 切换到指定工作区 |
| `workspace_next` / `workspace_previous` | 切换到下一个 / 上一个工作区 |
| `move_to_workspace_next` / `move_to_workspace_previous` | 移动焦点窗口到下一个 / 上一个工作区 |
| `cycle_layout` | 循环切换启用的布局 |
| `inc_master` / `dec_master` | 增加 / 减少主区域比例 |
| `inc_gap` / `dec_gap` | 增加 / 减少窗口间距 |
| `bar_next` / `bar_previous` | 状态栏模式切换（Docker → Info → Launcher） |
| `toggle_bar` | 显示 / 隐藏状态栏 |

### 状态栏模式

顶部状态栏有 3 种模式，用 `Alt+Shift+Down` / `Alt+Shift+Up` 循环切换：

| 模式 | 内容 |
|---|---|
| **Docker** | 工作区指示器、窗口标签、布局名称 |
| **Info** | 时钟、CPU 使用率、内存使用率、电池状态 |
| **Launcher** | 快捷启动按钮（在 `launcher:` 下配置） |

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
| **布局数量** | 7 | 3 | 3 | 4 |
| **焦点高亮** | 圆角矩形 | 仅边框 | 仅边框 | 仅边框 |
| **颜色滤镜** | 支持 | 无 | 无 | 无 |
| **平台** | Windows 10/11 | Windows 10/11 | Windows 10/11 | Windows 10/11 |

## 许可证

GNU General Public License v3.0 — 详见 [LICENSE](LICENSE)。

## 灵感来源

- [dwm](https://dwm.suckless.org/) — 最初的动态窗口管理器
- [i3](https://i3wm.org/) — 键盘驱动的树状布局平铺
- [GlazeWM](https://github.com/glzr-io/glazewm) — 简洁的 YAML 配置，C# 实现
- [Komorebi](https://github.com/LGUG2Z/komorebi) — Rust 驱动，Windows 上的 bspwm
