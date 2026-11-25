# Dung Beetle 使用手册

[返回项目首页](../README.md) · [English overview](../README.en.md)

本手册根据当前仓库组件接口编写。示例参数用于首次搭建和对比试算，并非工程推荐值或实测结果。请先完成 [安装](../README.md#安装)，并在 Grasshopper 中找到 Dung Beetle 分类。

## 1. Sliding Simulation（滑行物理模拟）

<img src="../Dung%20Beetle/Resources/sliding_simulation_icon1.png" width="44" alt="Sliding Simulation">

- 分类：`Dung Beetle → Physics`
- 实际组件名：`Sliding_SimulationComponent`
- 昵称：`滑行物理模拟 v61.2`

对障碍物 Brep 上的球形粒子运动进行数值模拟，考虑重力、摩擦及碰撞。独立粒子模式并行计算，多粒子互撞模式考虑粒子之间的碰撞。

### 输入端口

| 全名 | 简称 | 访问方式 | 初始值 / 说明 |
| --- | --- | --- | --- |
| Obstacles | Obs | List | 必填；地形、滑道等碰撞 Brep。实际接口是 Brep，Mesh 应先转换为合适的 Brep 或重建碰撞面。 |
| StartPts | Pts | List | 必填；起始点 |
| InitVel | Vel | List | 未连接时为零向量；大小为 m/s |
| Radius | R | List | 未连接时为 10；毫米 |
| Friction | F | List | 注册默认 0.45；空列表内部回退 0.35，建议显式输入 |
| Restitution | E | List | 0.5；粒子互撞模式的弹性参数，独立粒子模式不传入此参数 |
| Delay | D | List | 未连接时为 0；秒 |
| AutoDrop | Drop | Item | True；尝试自动贴地 |
| CollideParticles | Col | Item | True；多个粒子时启用互撞引擎 |
| Duration | T | Item | 10；模拟秒数 |
| TimeStep | dt | Item | 0.02；秒，须为正数 |

**单位：** 内部重力为 9810，速度输入乘以 1000，输出速度乘以 0.001。因此请使用毫米几何（包括粒子半径），而不是直接在米制模型中照搬数值。输入速度以 m/s 表示，时间以秒表示。

多个粒子共用参数列表时，代码通过循环索引重复使用列表值；一个数值可供全部粒子使用。为了方便核对，建议将同一模拟场景的起点和障碍物各组织为一个列表。

### 输出端口

| 全名 | 简称 | 含义 |
| --- | --- | --- |
| Message | msg | 中文诊断与统计信息；单粒子模式下日志可能为空 |
| Xforms | X | 各粒子的逐帧变换树 |
| Traj | Traj | 每粒子的运动轨迹树 |
| Vectors | V | 速度向量树（m/s） |
| Speeds | S | 速率树（m/s） |
| G_Forces | G | 加速度过载相关指标树 |

树分支按粒子索引组织。不同引擎的初始帧记录与 G 值定义存在实现差异；Billiards 分支写入的是 `MaxG` 指标，不能假定每项都是独立的瞬时人体过载。使用变换、速度等输出做动画或着色前，应先确认数据项数及帧对应关系。

### 首次试算：单粒子沿坡面滑行

1. 建立一块倾斜矩形平面 Brep，例如水平长度 3000 mm、落差 1500 mm、宽度 1000 mm。
2. 在坡面较高的一侧设置起点，略高于坡面，并接入 `Pts`。将坡面接入 `Obs`。
3. 设置零初速度、半径 10 mm、摩擦系数 0.45、`Drop=True`、`Col=False`、`T=10`、`dt=0.02`。
4. 将 `Traj` 接到 Curve 参数，将 `S` 接到 Panel，观察粒子是否运动以及速度变化。若超出坡面，可延长碰撞面或增加边界 Brep。
5. 分别试算 `F=0.35` 与 `F=0.45`，比较路径和速度。再缩小步长，检查结果是否对时间步长过度敏感。

### 进一步尝试

- **多粒子模拟：** 增加多个起点，设置合理半径与间距，打开 `Col`，用 `D` 控制不同发射时间。
- **速度着色：** 对速度范围进行 Remap Numbers，再连接 Gradient；先核对路径点、帧和粒子树分支。
- **动画：** 将起点处的原始粒子几何与对应分支 `X` 的逐帧变换匹配，用 List Item 选择帧，再通过 Transform 更新几何。

本组件是简化数值模型，不是人体动力学或设施安全认证工具；不得用输出直接替代工程校核、物理实测及专业安全评估。

## 2. Curve to Biarcs（曲线圆弧拟合）

<img src="../Dung%20Beetle/Resources/GH_SCurve_24x24.png" width="44" alt="Curve to Biarcs">

- 分类：`Dung Beetle → Geometry`
- 实际组件名：`Curve to Biarcs`
- 昵称：`Crv2Arcs`

通过圆弧逼近自由曲线，并尝试减少冗余段、修复短弧和接缝折角，适用于弯管、型材拉弯及其他圆弧加工的几何准备。

### 输入端口（全部为 Item）

| 全名 | 简称 | 注册默认值 | 作用 |
| --- | --- | ---: | --- |
| Curve | C | — | 原始曲线，必填 |
| Tolerance | Tol | 0.01 | 拟合偏差目标（模型单位） |
| Angle Tolerance | Ang | 5 | 折角判定阈值（度） |
| Max Length | MaxL | 1000 | 单段最大弧长目标 |
| Min Length | MinL | 100 | 单段最小弧长目标 |
| Resolution | Res | 50 | 拟合采样密度 |
| Seam Optimize | Seam | True | 尝试调整闭合曲线接缝 |
| Dir Optimize | Dir | True | 比较正反向拟合结果 |
| Mid Optimize | Mid | True | 改善圆弧中点贴合 |
| Opt Shorts | O_S | True | 尝试合并过短弧段 |
| Opt Kinks | O_K | False | 启用局部折角重构 |
| Kink Iters | Iter | 5 | 折角重构迭代次数 |

长度和偏差使用 Rhino 模型单位。建议输入正公差、正弧长、合理采样数，并确保 `MinL ≤ MaxL`。闭合接缝及反向优化可能改变输出顺序和接缝位置。

### 输出端口

| 全名 | 简称 | 类型 / 含义 |
| --- | --- | --- |
| Arcs | A | Arc List；拟合圆弧 |
| Deviations | Dev | Number List；各段估算偏差 |
| Tangents | Tan | Vector List；起始切线 |
| DevLines | DL | Line List；偏差可视化线段 |
| Log | Msg | Text Item；诊断与统计 |
| Shorts | S_Arc | Arc List；残余短弧 |
| Joints | J_Pt | Point List；接缝位置 |
| Angles | J_Ang | Number List；接缝折角（度） |
| Kinks | K_Pt | Point List；仍超过阈值的折角位置 |
| KinkAngles | K_Ang | Number List；残余折角（度） |
| Debug | Dbg | Curve List；诊断用局部曲线片段 |

### 首次试算：S 形自由曲线

1. 绘制一条平滑 S 形 NURBS 曲线，长度约 3000–5000 mm（建议测试尺寸）。
2. 接入 `C`，设置 `Tol=1`、`Ang=5`、`MaxL=1000`、`MinL=100`、`Res=50`。
3. 查看 `A`，将 `Dev` 接到 Panel；用 `DL` 显示误差位置。
4. 将 `J_Ang`、`S_Arc` 和 `K_Pt` 分别接到 Panel、Arc 和 Point 参数，确认残余短弧及折角。
5. 若折角超出项目允许值，开启 `O_K`，并尝试 `Iter=5` 后再次比较。若依旧不能满足要求，修改原曲线或调整合理公差和分段约束。
6. 加工前复查圆弧半径、长度、接缝、整体误差及工艺能力。

拟合误差来自算法估算；G1 连续性与长度限制为优化目标，并非严格保证。过于苛刻的公差或弧长限制可能相互冲突，不能因为组件有输出就认为满足加工要求。

## 3. Smart Planar Union（智能平面联集）

<img src="../Dung%20Beetle/Resources/Grasshopper_SurfaceUnion_Minimal2_24.png" width="44" alt="Smart Planar Union">

- 分类：`Dung Beetle → Geometry`
- 实际组件名：`Smart Planar Union`
- 昵称：`PlanarUnion`
- 内部算法标识：V3.3

针对大量水平重叠平面区域的联集任务。组件内部临时将输入面拉伸成实体，通过分阶段 Boolean Union 搜索进行合并，再提取平面区域，并尝试 Join / Merge 清理多余分割。临时实体只是算法手段，并非用户最终需要的结果。

### 输入端口

| 全名 | 简称 | 访问方式 | 默认值 / 说明 |
| --- | --- | --- | --- |
| Surfaces | S | List | 同一 Z 标高、水平、单面平面 Brep |
| Tolerance | Tol | Item | 0.01 模型单位；非正值回退为 0.01，必须小于内部拉伸高度 100 |
| Run | Run | Item | False；确认输入后设为 True 运行 |

### 输出端口

| 全名 | 简称 | 访问方式 | 说明 |
| --- | --- | --- | --- |
| Top | Top | List | 还原到输入标高并尝试 Join / Merge 后的平面 Brep |
| Groups | Groups | Tree | 每个分支对应一个最终实体合并组，包含从 0 开始的原始输入列表索引 |
| Failed | Failed | List | 预处理拒绝的原始 Brep；无法表示为空值的输入不会出现在这里 |
| Log | Log | Item | 中文运行状态、统计、候选复核和异常诊断 |

`Top` 经过整体 Join / Merge，因此不保证与 `Groups` 的分支逐项对应。Groups 中的索引以传入组件的列表顺序为准，而非全局 Rhino 对象编号。

### 首次试算：三个重叠平面

1. 在 World XY 平面创建三个部分重叠的闭合矩形或其他轮廓。
2. 将轮廓接到 Boundary Surfaces，生成独立的水平单面 Brep。
3. 将其 Flatten 后连接到 `S`，设置 `Tol=0.01`，然后将 Boolean Toggle 接入 `Run` 并置 True。
4. 关闭原始输入预览，将 `Top` 接到 Brep 参数查看结果。
5. 将 `Groups` 和 `Log` 接到 Panel，检查合并组及完成状态；将 `Failed` 接到 Brep 参数检查不合格输入。

再添加一个与其他面分离的独立区域，观察是否保留为独立区域；接着添加一张不同标高的面，验证拒绝输入与日志。对于倾斜共面区域，应先做统一平面变换到 World XY，并在计算完成后逆变换回原位置。

### 黄色警告与大批量计算

- “待运行”：检查 Run 是否为 False。
- “需检查”：可能存在输入拒绝、Boolean 异常、合并回退、顶面补救或搜索预算等事项，阅读 `Log` 和 `Failed`。
- “错误”：检查输入是否为空、是否满足水平共面单面 Brep 条件，以及容差设置。
- 仅剩包围盒重叠候选、且这些候选均已尝试，并不必然意味着失败。
- 当前搜索预算为 180 秒及 1200 次 Boolean 调用，但并非单次 Boolean 操作的强制超时；大批量计算为同步执行，可能暂时占用 Grasshopper 界面。建议先用小样本检查再增加输入。

## 4. 常见问题

| 问题 | 建议 |
| --- | --- |
| 看不到 Dung Beetle 标签 | 确认 `.gha` 及依赖位置、Windows 文件是否被锁定、Rhino 运行时是否兼容；重启 Rhino。 |
| 下载 ZIP 但没有 `.gha` | 可能下载了 GitHub 自动生成的源码压缩包。到 Releases 查找发布资产；没有发布资产时需编译。 |
| 同时出现多个版本 | 关闭 Rhino，从加载目录移出旧版，只保留一份与运行时匹配的安装包。 |
| GH 文件加载后组件缺失 | 检查 Dung Beetle 是否成功加载，避免在多个框架产物间混装。 |
| 曲线有残余短弧或折角 | 查看 `S_Arc`、`K_Pt`、`Dev`、`Msg`，调整参数或原始曲线，必要时人工修整。 |
| 平面联集得到多个区域 | 原区域可能不相交，或存在待检查运算；结合 `Groups`、`Failed`、`Log` 和几何预览判断。 |
| 滑行结果不符合预期 | 检查毫米单位、输入面、起点、摩擦、半径、步长及模拟时长。 |

反馈地址：[GitHub Issues](https://github.com/Sucre0903/Dung-Beetle/issues)。请注明 Rhino 版本、运行时、操作系统、插件版本及复现步骤，并提供不含敏感数据的最小几何样本与日志。
