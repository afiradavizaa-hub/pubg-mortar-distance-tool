# PUBG 地图测距工具

Windows 透明悬浮窗测距工具。直接覆盖在窗口化或无边框的 PUBG 地图上，以固定 600 米基准尺校准后测量任意两点的平面直线距离。无需导入截图，不读取游戏内存，不注入游戏进程。

## 技术栈

- C# / WPF / .NET 10
- `PubgMapDistanceTool.Core`：校准、测量、撤销、坐标空间与高 DPI 坐标转换
- `PubgMapDistanceTool`：透明窗口、绘制、鼠标交互、游戏客户区跟踪、全局热键
- `PubgMapDistanceTool.Tests`：无第三方依赖的自动化测试程序

## 使用

1. 将 PUBG 设置为“窗口化”或“无边框窗口”模式，并打开游戏地图。
2. 启动 `PubgMapDistanceTool.exe`。工具默认透明、置顶，并在 Windows 任务栏显示图标。
3. 按 `F7` 进入校准。可直接点击两个网格点，或按住左键拖取一条线；基准尺会自动校正为水平。两端的细十字线用于精确定位网格。
4. 点击“完成校准”。基准尺像素长度和每像素米数会锁定，控制条自动收起，并立即进入测距。
5. 在地图任意位置按住鼠标左键，从起点拖到终点后松开，立即生成当前唯一一条测量。也可点击起点、再点击终点。
6. 再次拉线会自动替换上一条测量，屏幕始终只保留一条线。点击左上角“展开”可恢复完整控制条。
7. 点击测量线可选中它；随后可复制、删除或拖动两个较小的圆点端点。
8. 按 `F9` 开启鼠标穿透以正常操作游戏；再次按 `F9` 返回编辑。
9. 按 `F6` 显示或隐藏悬浮窗，点击“退出”结束程序。

按 `Alt+Tab` 切换到 PUBG 或其他应用时，测距窗口自动最小化到 Windows 任务栏。需要再次测距时，从任务栏或 `Alt+Tab` 切回，也可按 `F6` 恢复。

游戏内地图缩放、地图布局或分辨率发生变化后，应主动点击“重新校准”。游戏客户区尺寸、DPI 或窗口身份发生变化时，程序会自动清除旧校准和测量，避免继续输出失效结果。

### 默认快捷键

- `F6`：显示 / 隐藏悬浮窗（全局）
- `F7`：进入校准模式（全局）
- `F8`：进入测距模式（全局）
- `F9`：切换鼠标穿透（全局）
- `Esc`：取消当前未完成操作（工具编辑焦点内）
- `Ctrl+Z`：撤销（工具编辑焦点内）

所有全局快捷键都可以在“快捷键”设置中重新选择。支持全键盘按键，并可任意组合 `Ctrl`、`Alt`、`Shift`、`Win`；保存到 `%LOCALAPPDATA%\PubgMapDistanceTool\settings.json`。

## 构建与测试

需要 Windows 和 .NET 10 SDK。

```powershell
dotnet build PubgMapDistanceTool.slnx -c Release
dotnet run --project tests\PubgMapDistanceTool.Tests\PubgMapDistanceTool.Tests.csproj -c Release
```

生成自包含 Windows x64 单文件版本：

```powershell
dotnet publish src\PubgMapDistanceTool\PubgMapDistanceTool.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o "outputs\PUBG地图测距工具-win-x64"
```

普通窗口启动方式（透明悬浮层不可用时的回退）：

```powershell
PubgMapDistanceTool.exe --normal
```

## 坐标与计算

所有基准尺端点和测距点都保存为 PUBG 客户区物理像素坐标。悬浮窗的位置、大小和 PPI 变化不会改变这些坐标。绘制与点击时，程序在以下坐标系之间双向转换：

- Windows 桌面物理像素坐标
- 悬浮窗 WPF DIP 坐标
- PUBG 客户区物理像素坐标

设基准尺像素长度为 `S`，测量两点像素距离为 `L`：

```text
K = 600 / S
D = L × K
```

只有基准尺和待测点使用同一地图显示比例、同一坐标平面且没有非等比拉伸时，结果才成立。

## 兼容性与限制

- 已验证：Windows 透明 WPF 窗口、全屏悬浮绘制、鼠标穿透、全局热键、普通窗口回退、高 DPI 双向坐标换算。
- 控制条使用浅色磨砂、圆角胶囊按钮的 iOS 风格，并保持文字和状态颜色高对比度。
- 窗口化、无边框窗口模式通常可用于透明置顶覆盖。
- 独占全屏会绕过桌面合成器，透明悬浮窗可能不可见。此时请在游戏中切换为无边框/窗口化，或使用 `--normal` 普通窗口回退。
- 游戏反作弊策略可能限制第三方覆盖层。程序不会尝试绕过反作弊；若边框或点击行为被阻止，请改用允许覆盖的显示模式。
- 程序只能监测 PUBG 客户区的位置、尺寸和 DPI，无法可靠识别游戏内部地图是否缩放或平移。相关操作后必须手动重新校准。
- 跨多个显示器且各屏 DPI 不同时，单一 WPF 窗口无法对每个区域使用不同 DPI 变换；建议将游戏和悬浮窗保持在同一个显示器。
- 本工具只计算截图/画面中的二维直线距离，不包含地形高度差、道路绕行、角色或载具路线。
- 当前机器未安装/运行 PUBG 本体，因此未宣称已在真实游戏反作弊环境中验证覆盖显示；桌面、普通窗口和透明切换路径已实际测试。
