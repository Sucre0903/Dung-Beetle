# 发布检查清单（维护者）

[返回首页](../README.md)

当前源码快照未包含编译后的 `.gha`、示例 `.gh` 文件、Yak 发布清单或 LICENSE。以下是后续发布的建议步骤，并非已完成事项。

## 1. 准备可安装版本

- [ ] 核对 `Dung Beetle/Dung Beetle.csproj` 中的 `<Version>`；目前为 `1.0`，正式 Tag 由维护者确认。
- [ ] 检查项目元数据（Title、Company、Description）和 `DungBeetleInfo.cs` 中的简介是否需要统一。
- [ ] 选择目标 Rhino 版本、操作系统和运行时，解决或验证依赖兼容性警告。
- [ ] 使用 Release 配置编译，确认 `.gha` 和需要随包分发的依赖。
- [ ] 在干净的目标 Rhino / Grasshopper 环境安装并测试全部三个电池。
- [ ] 保持已有组件 GUID 不变，测试旧 `.gh` 文件能否继续读取。
- [ ] 分别验证毫米单位的滑行案例、S 曲线拟合案例、水平平面联集案例及异常输入。

## 2. 创建 GitHub Release

在 [Releases](https://github.com/Sucre0903/Dung-Beetle/releases) 中创建一个与实际版本对应的 Release，将已测试的插件及其依赖打包上传到 Assets。

建议文件名：`Dung-Beetle-vX.Y.Z-win-<runtime>.zip`（占位示例，请替换为实际版本与运行时）。压缩包内部保留 `Dung Beetle.gha` 文件名，不要把不同框架的同名插件放到同一个安装目录。

发布说明应包含：
- 新增与修改的组件；
- 已测试的 Rhino 版本 / OS / 运行时；
- 已知限制与安装方法；
- 如涉及旧版本兼容性，写明迁移方式。

## 3. 优化仓库首页的发现体验

- About 简介建议：`Rhino / Grasshopper toolkit for particle simulation, curve-to-arc fitting and smart planar union.`
- Topics 建议：`grasshopper`、`rhino3d`、`parametric-design`、`computational-design`、`geometry-processing`、`fabrication`、`csharp`。
- 后续上传每个组件实际运行的 GH/Rhino 截图或短 GIF，并提供一个能直接打开的最小 `.gh` 示例。可在 `examples/` 中按组件分目录。
- 如计划开放源代码授权，请由维护者选择合适的 LICENSE；不要在未确认授权条件时自行声明 MIT、GPL 或免费商用。
- 未来可考虑 Rhino Package Manager（Yak）或 Food4Rhino 分发；当前仓库并不证明这些渠道已上线。

参考：[GitHub Releases 文档](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)。
