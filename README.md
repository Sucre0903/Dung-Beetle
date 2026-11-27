<p align="center">
  <img src="Dung%20Beetle/Resources/Dung_Beetle_icon2.png" width="72" alt="Dung Beetle logo">
</p>

<h1 align="center">Dung Beetle · 屎壳郎</h1>

<p align="center"><strong>让复杂的几何工作，变成几个简单的 Grasshopper 电池。</strong><br>
Physics simulation · Fabrication-oriented geometry · Planar operations</p>

<p align="center">
  <a href="https://github.com/Sucre0903/Dung-Beetle/releases"><strong>下载 / Releases</strong></a> ·
  <a href="#安装">安装</a> ·
  <a href="#案例示例">案例示例</a> ·
  <a href="docs/Usage.zh-CN.md">详细使用手册</a> ·
  <a href="README.en.md">English</a> ·
  <a href="https://github.com/Sucre0903/Dung-Beetle/issues">问题反馈</a>
</p>

<p align="center">
  <img alt="Rhino 8 target" src="https://img.shields.io/badge/Rhino-8-24304F">
  <img alt="Grasshopper 1" src="https://img.shields.io/badge/Grasshopper-1-749B58">
  <img alt="C sharp" src="https://img.shields.io/badge/C%23-.NET-7958A6">
  <img alt="Development status" src="https://img.shields.io/badge/Status-In%20development-EAA34D">
</p>

---

## 为什么使用 Dung Beetle？

Dung Beetle 是面向 Rhino / Grasshopper 的参数化设计工具集，聚焦物理模拟、几何优化与加工制造中的实际问题。

想知道一个粒子沿滑道会如何运动？想把自由曲线变成可用于圆弧加工的分段几何？或者想将大量重叠的水平曲面合并成清晰的平面区域？Dung Beetle 将这些工作封装成独立组件，让设计、分析与几何整理在同一个 Grasshopper 工作流中完成。

## 组件一览

| 电池 | 能解决什么问题 | 工具栏 |
| :--- | :--- | :--- |
| <img src="Dung%20Beetle/Resources/sliding_simulation_icon1.png" width="36" alt=""> **Sliding Simulation** | 模拟粒子滑行、重力、摩擦和碰撞，输出轨迹、速度及过载分析数据。 | Physics |
| <img src="Dung%20Beetle/Resources/GH_SCurve_24x24.png" width="36" alt=""> **Curve to Biarcs** | 将自由曲线拟合为多段圆弧，优化短弧和接缝折角，辅助型材拉弯、滚圆等加工准备。 | Geometry |
| <img src="Dung%20Beetle/Resources/Grasshopper_SurfaceUnion_Minimal2_24.png" width="36" alt=""> **Smart Planar Union** | 对同一水平面上的重叠单面 Brep 进行智能联集，输出整理后的平面区域及原始输入分组。 | Geometry |
| <img src="Dung%20Beetle/Resources/SpatialCurveMatch_24x24.png" width="36" alt=""> **Spatial Curve Match** | 在三维空间中对齐全等的平面曲线，输出目标索引、刚性变换和偏差。[使用说明](docs/Spatial-Curve-Match.zh-CN.md) | Geometry |

## 案例示例

通过配套的 Rhino 模型与 Grasshopper 文件，快速体验 Dung Beetle 的实际工作流程。

### Example 01 · 综合示例

**下载案例文件：**

- [Grasshopper 文件（.gh）](Dung%20Beetle/Examples/DungBeetle%20Example%2001.gh)
- [Rhino 模型文件（.3dm）](Dung%20Beetle/Examples/DungBeetle%20Example%2001.3dm)

下载两个文件后，分别在 Grasshopper 和 Rhino 中打开，即可查看并体验案例工作流程。

## 安装

**推荐尝试环境：Rhino 8 for Windows + 内置 Grasshopper（GH1）。**

项目当前引用 Rhino 8 系列 Grasshopper SDK，并配置 `net7.0-windows`、`net7.0`、`net48` 三个目标框架。这并不等于所有 Rhino 版本、运行时或操作系统均已验证；其他环境的兼容性仍需测试。

### 方式 A：从 Release 安装（适合普通用户）

1. 打开 [GitHub Releases](https://github.com/Sucre0903/Dung-Beetle/releases)，如果已有发布包，请下载与运行环境匹配的插件资产（Assets），而不是 `Source code (zip)`。
2. 解压下载的发布包。在 Windows 中，如文件属性底部出现“解除锁定 / Unblock”，勾选后应用。
3. 在 Rhino 中运行 `Grasshopper`，选择 Grasshopper 菜单 **File → Special Folders → Components Folder**。
4. 关闭 Rhino，将 `Dung Beetle.gha` 和发布包明确要求的配套依赖文件放入该目录（建议保持依赖的相对位置）。不要混装不同版本或不同目标框架的 `.gha`。
5. 重启 Rhino，打开 Grasshopper，找到 **Dung Beetle** 工具栏，即可使用。

> 如果 Releases 暂无可安装资产，请使用下面的源码编译方式。仓库的源码 ZIP 本身不是插件安装包。

### 方式 B：从源码编译（适合开发者）

1. 克隆或下载仓库，使用 Visual Studio 2022 打开根目录的 `Dung Beetle.sln`。
2. 安装与项目目标框架匹配的 .NET SDK / .NET Framework 4.8 开发工具，并还原 NuGet 包。
3. 选择 Release 配置并生成项目。
4. 在 `Dung Beetle/bin/Release/<目标框架>/` 查找 `Dung Beetle.gha` 及所需运行时依赖，选择与 Rhino 运行时匹配的一份产物，再按方式 A 安装。

也可在仓库根目录尝试构建 Windows 的 .NET 7 目标（仅示例，非所有 Rhino 8 运行时的通用发行包）：

```powershell
dotnet build ".\Dung Beetle\Dung Beetle.csproj" -c Release -f net7.0-windows
```

当前工程引用 `System.Resources.Extensions 10.0.1`，与 .NET 7 的组合可能产生兼容性警告。请在目标 Rhino 环境实际验证后再分发，勿仅以编译成功判断兼容性。

## 三个快速上手案例

以下是可手动搭建的示例，不需要额外示例文件。端口以实际组件名称为准。

### 01 · Sliding Simulation — 让粒子沿曲面滑行

<img src="Dung%20Beetle/Resources/sliding_simulation_icon1.png" width="42" alt="Sliding Simulation">

**适用场景：** 滑梯与滑道方案研究、粒子运动路径、速度分布可视化。

- Rhino 模型建议使用毫米单位。建立一条有高差的坡面或滑道 Brep，并在上端设置一个点。
- 分别连接 `Obs`（障碍物）和 `Pts`（起点），设置 `R=10`、`F=0.45`、`Drop=True`、`Col=False`、`T=10`、`dt=0.02`；初速度可为零。
- 查看 `Traj` 的运动路径，将 `S` 接入 Panel 查看速率，或用 Gradient / Custom Preview 制作速度着色分析（按帧或对应路径点组织数据）。

```text
Ramp Brep ── Obs ┐
Start Point ─ Pts ├─ Sliding Simulation ─ Traj / S / G
Parameters ──────┘
```

画布搜索：`Sliding_SimulationComponent` 或 `滑行物理模拟 v61.2`。

> 这是数值模拟工具，不是工程安全认证软件；速度和 G 值不能直接作为人员安全或规范合规的结论。

### 02 · Curve to Biarcs — 让自由曲线更接近可加工几何

<img src="Dung%20Beetle/Resources/GH_SCurve_24x24.png" width="42" alt="Curve to Biarcs">

**适用场景：** 弯管、弧形型材、自由曲线分段、加工前的几何优化。

- 绘制一条长度数千毫米的平滑 S 形曲线，接入 `C`。
- 示例参数：`Tol=1`、`Ang=5`、`MaxL=1000`、`MinL=100`、`Res=50`。
- 查看 `A` 的圆弧结果，用 `Dev`、`J_Ang`、`S_Arc`、`K_Pt` 检查偏差、折角与残余问题；有不合适接缝时尝试开启 `O_K`。

```text
Freeform Curve ─ C ─ Curve to Biarcs ─ A (Arcs)
                         │
                         └─ Dev / J_Ang / K_Pt
```

画布搜索：`Curve to Biarcs` 或 `Crv2Arcs`。

> 长度限制、拟合公差与 G1 光顺性是优化目标，而不是对所有输入的硬性保证；请结合偏差及实际设备的弯曲半径和工艺约束复核。

### 03 · Smart Planar Union — 让重叠面变成清晰区域

<img src="Dung%20Beetle/Resources/Grasshopper_SurfaceUnion_Minimal2_24.png" width="42" alt="Smart Planar Union">

**适用场景：** 平面图形整理、重叠铺装区域、平面板件和轮廓提取前处理。

- 在 World XY 平面绘制三个部分重叠的闭合轮廓，使用 Boundary Surfaces 生成单面平面 Brep。
- 把同一标高的一组面 Flatten 后接入 `S`，设置 `Tol=0.01`，准备完成后将 `Run` 设为 `True`。
- 隐藏原始面的预览，查看 `Top` 的联集结果；`Groups` 可追踪原输入成员索引，`Failed` 和 `Log` 用于检查异常。

```text
Overlapping Planar Breps ─ S ─ Smart Planar Union ─ Top
Tolerance / Run ──────────────┘                   Groups / Failed / Log
```

画布搜索：`Smart Planar Union` 或 `PlanarUnion`。

> 当前实现限定同一 Z 标高的水平、单面平面 Brep；不是任意三维实体布尔联集。若输入为斜平面，请先统一转换到 World XY，再将结果变换回原平面。

更完整的输入输出说明、默认值、数据树用法及排错流程，请阅读 [中文使用手册](docs/Usage.zh-CN.md)。

## 反馈与参与

欢迎分享你的实际设计场景，报告 Bug 或提出功能需求。如果 Dung Beetle 对你的工作有帮助，欢迎给仓库点一个 ⭐ Star，让更多 Grasshopper 用户发现它。

[提交 Issue](https://github.com/Sucre0903/Dung-Beetle/issues) 时建议附上 Rhino 版本、操作系统、插件版本、输入几何、参数设置、截图和日志。请移除项目中的敏感信息。

**作者：Suke**

项目仍在持续开发。使用前请检查几何与模拟结果；当前仓库未包含明确的 LICENSE 文件，代码授权及再分发条件请向作者确认。
