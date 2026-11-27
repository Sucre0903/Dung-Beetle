# 空间快速匹配

开发测试版 `1.1.0-dev.3` · 2026-11-27

将附件中的 `transform V1` 曲线匹配脚本接入屎壳郎。基线为源码 ZIP 对应的提交 `9010b27b7b652ed608d4bff34d6ee5da1c382d66`，原工程版本为 `1.0`。组件位于 **Dung Beetle → Geometry**，也可在画布双击搜索 **Spatial Curve Match** 或 **SpaceMatch**。

两组曲线可位于三维空间的不同平面。组件为每条源曲线寻找几何上全等的目标，输出源位置到目标位置的旋转和平移。输入需为公差内平面的曲线，开曲线与开曲线匹配，闭合曲线与闭合曲线匹配。组件使用 Rhino 模型坐标和模型单位，不修改输入，也不写入 Rhino 文档。变换无缩放、错切或三维镜像。开启 `Flip` 时，算法额外尝试将目标平面法向反过来构造旋转候选。

## Windows 试装

1. 解压测试包。如果 Windows 的 ZIP 属性中有 **解除锁定**，可在解压前应用。
2. 在 Grasshopper 选择 **File → Special Folders → Components Folder**，记下目录，然后关闭 Rhino。
3. 如果已装屎壳郎，将旧版 `Dung Beetle.gha` 及其专用配套文件备份到 Grasshopper 扫描范围外。按下表选一个试装目录，把其中的文件保持在同一个目录中。可将试装目录放在 **Components Folder**，如未加载，使用 Rhino 命令 `GrasshopperDeveloperSettings` 的 **Add Folder** 指定该目录。
4. 重启 Rhino，打开 Grasshopper，在画布搜索 `Spatial Curve Match`。回退时关闭 Rhino，移出本次加入的试装目录，恢复旧版，再重启。

| 当前 Windows Rhino 8 运行时 | 包内目录 |
|---|---|
| .NET Core | `Install-Windows/net7.0-windows` |
| .NET Framework 4.8 | `Install-Windows/net48` |

同一 Rhino 会话只加载一套屎壳郎文件。测试包里的 GHA 由同版本源码编译，均为 Any CPU 托管程序集。若已安装 `1.1.0-dev.2`，关闭 Rhino 后替换为本版同目标框架的 `Dung Beetle.gha` 即可更新组件图标。

## 输入

| 输入 | 类型和访问方式 | 默认和取值 |
|---|---|---|
| `SourceCrvs`（S） | Curve · List | 空。索引及输出顺序以本次源列表为准。 |
| `TargetCrvs`（T） | Curve · List | 空。为当前源列表提供候选，目标索引从 0 开始。 |
| `Tolerance`（Tol） | Number · Item | `0`。输入正有限数作为模型单位下的公差。其他值使用 Rhino 文档绝对公差，无文档时使用 `0.001`。 |
| `SampleCount`（N） | Integer · Item | `96`。小于 16 时使用 96，大于 512 时使用 512。 |
| `AllowFlip`（Flip） | Boolean · Item | `True`。尝试反向目标平面法向。 |
| `UniqueTarget`（Unique） | Boolean · Item | `True`。同一次列表求解内每个目标索引最多使用一次。`False` 时每条源曲线独立选择目标。 |

六个输入均可不接线。源或目标列表为空时给出 Grasshopper 提示。附件的 `.cs` 未包含原脚本电池的面板默认值，本版的四个参数默认值在上表列明。对比旧定义时，请把原定义中的实际参数接到新组件。

曲线输入遵循 Grasshopper 的列表分支配对。每次求解中的目标索引与唯一分配仅针对当次目标列表。希望两棵树里的所有曲线参与同一轮匹配时，对 `S` 和 `T` 两个输入都启用 **Flatten**。

## 输出

| 输出 | 类型和访问方式 | 匹配失败时 |
|---|---|---|
| `MatchedTargetIndex`（I） | Integer · List | `-1` |
| `Transforms`（X） | Transform · List | 无效变换 `Transform.Unset` |
| `IsPerfect`（OK） | Boolean · List | `False` |
| `MaxDeviation`（Dev） | Number · List | `NaN` |
| `MatchedCrvs`（C） | Curve · List | 空项 |
| `Log` | Text · List | 一条汇总，以及每条源曲线的诊断。 |

前五项按源列表索引对齐。需要把同一变换应用到对应实体、曲面或其他附属几何时，用 `OK` 筛选对应几何及 `X` 后接 Grasshopper 的 **Transform**。存在多个可用目标时，`Log` 会列出候选。一对一模式先追求匹配数量，候选优先级依据偏差和目标索引。成对偏差检查沿用原脚本的 RhinoCommon 曲线偏差和双向采样，`Dev` 表示本次计算使用的偏差估计。

## 源码与构建

- `Dung Beetle/CurveRigidMatcher.cs` 承载 V1 中的候选变换、偏差检查及一对一分配。匹配结果的曲线副本若变换失败，该项会保留失败占位并写入 `Log`。用于候选检查的临时曲线在检查后释放。
- `Dung Beetle/SpatialCurveMatchComponent.cs` 注册组件、端口和数据转换。组件沿用固定 GUID 和工具栏分类，并通过项目资源嵌入 `Resources/SpatialCurveMatch_24x24.png`。`1.1.0-dev.3` 更新了图标。深海军蓝圆底上的金黄色和暖橙色轮廓分别表示源曲线与目标曲线，三组虚线表示对应位置。
- 使用 Visual Studio 2022 打开 `Dung Beetle.sln`。目标保持为 `net7.0-windows`、`net7.0`、`net48`。Grasshopper 和 RhinoCommon SDK 均为 `8.0.23304.9001`，现有项目依赖 `System.Resources.Extensions 10.0.1`。

在解决方案目录运行以下 PowerShell 命令。产物位于 `Dung Beetle/bin/Release/<目标框架>/`。

```powershell
dotnet build ".\Dung Beetle\Dung Beetle.csproj" -c Release -f net7.0-windows
dotnet build ".\Dung Beetle\Dung Beetle.csproj" -c Release -f net48 -p:GenerateResourceUsePreserializedResources=true
```

## 验证与验收

| 检查 | 状态 |
|---|---|
| Release 编译 | 三个目标已使用 .NET SDK 8.0.428 在 Linux 上编译。两个 .NET 7 目标各有四条现有依赖的目标框架支持警告。当前 Linux 构建 `net48` 时启用了预序列化资源选项。 |
| 空列表、两个空源项和空目标项、采样数上下限 | 独立 .NET 检查通过。确认每条源仍有独立占位，汇总包含无效输入计数。 |
| Grasshopper 数据类型 | 使用工程的 SDK 检查了无效变换、NaN 与曲线数据树中的空位置。 |
| Rhino 加载及使用反馈 | 用户反馈已成功试用 `1.1.0-dev.1`。 |
| `1.1.0-dev.3` 图标 | 已检查 24×24 PNG 的透明边角和原尺寸显示。三个构建目标的 `SpatialCurveMatchIcon` 均与新版 PNG 逐字节一致。实际 Rhino 工具栏显示待确认。 |
| 数据树配对、旧定义加载与性能 | 待在 Rhino 8 / Grasshopper 中进行完整验收。 |

1. **空间对齐。** 在 Rhino 中画一条不对称闭合折线，可用 `(0,0,0)`、`(100,0,0)`、`(80,45,0)`、`(10,65,0)` 四个点。复制后将副本绕世界 X 轴转 35°，再任意平移一段距离。将原曲线接 `S`、副本接 `T`，给定 `Tol = 0.01`。应得到 `I = 0`、`OK = True`、`Dev ≤ 0.01`，并看到 `C` 与目标重合。用同样步骤再确认一条不对称开放折线。
2. **未匹配占位。** 把原折线的 1.3 倍缩放副本加到源列表第一位，原折线放第二位。应得到 `I = [-1, 0]`、`OK = [False, True]`，`X`、`Dev` 和 `C` 的第一位为对应占位。另接一条明显非平面的折线，检查日志中的无效源诊断。
3. **重复目标与数据树。** 向同一分支的 `S` 放入两条全等源曲线，向 `T` 只放一个全等目标。`Unique=True` 时应只有一个成功，关闭时应有两个成功。另用两个分支复测，检查目标索引以各次求解的列表为准。
4. **版本回归与性能。** 使用相同参数同时运行原脚本和新组件，比较 `I`、`OK`、`Dev` 及输出几何。用包内原插件示例检查三个原组件，并用有代表性的曲线集记录两版 Grasshopper Profiler 耗时。例如，对同一组曲线分别做 10×10 与 100×100 的源目标列表测试。算法先筛选源与目标的组合，再对通过筛选的组合做几何验证，候选较多时耗时会随列表规模增加。
