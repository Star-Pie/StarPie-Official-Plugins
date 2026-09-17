# StarPie 官方插件仓库

StarPie 官方内建动作模块的独立源码与发布仓库。

> 当前状态：仅包含仓库骨架与发布工作流，尚未迁入插件源码。

## 仓库目标

本仓库将负责维护从 StarPie 主程序中拆分出来的官方动作模块。目标架构如下：

- 所有官方模块集中在同一个仓库中维护；
- 每个模块拥有独立版本号；
- 每次只为发生变化的模块生成不可变发布资产；
- 每次模块集合发布生成一份签名的 `module-catalog.json`；
- 未变化模块不重复上传，由 catalog 引用其历史 Release 资产。

StarPie 运行时实际加载的仍然是 DLL 程序集。`.spkg` 只是分发容器：StarPie 验证并解压后，再把其中的 DLL 交给现有插件运行时加载。

## 仓库结构

```text
src/                         模块源码工程（预留，迁移尚未开始）
tests/                       模块包与宿主兼容性验证
build/                       构建、打包、catalog、校验与发布脚本
catalog/                     模块注册表与 catalog JSON Schema
docs/                        发布模型、模块契约与安全模型文档
templates/                   模块注册表与 plugin.json 示例
.github/workflows/           增量验证、全量验证与发布工作流
artifacts/                   仅用于 CI 产物，不提交 Git
release-metadata/            仅用于 CI 元数据，不提交 Git
```

## 发布模型

完整说明见 [`docs/release-model.md`](docs/release-model.md)。

整体行为如下：

```text
模块源码发生变化
-> 必须同步修改模块版本号
-> 只构建和测试发生变化的模块
-> 只为变化模块生成 .spkg
-> 创建一次模块集合 Release
-> Release 中只上传变化模块的 .spkg
-> 生成包含全部模块的 module-catalog.json
-> 未变化模块继续引用历史 Release 的包地址
-> Release 正文列出当前完整模块集合
```

## 工作流

- `validate.yml`
  - 在 Pull Request 和 `main` 分支推送时运行。
  - 只构建和测试本次 diff 影响的模块。
  - 公共构建文件、catalog、workflow、Schema 或模块注册表变化时，触发全量验证。
- `full-validation.yml`
  - 支持手动触发、每周定时触发，以及公共构建基础设施变化时触发。
  - 构建和测试全部已注册模块。
- `release-modules.yml`
  - 手动触发的正式发布工作流。
  - 将当前模块版本与最近一次发布的 catalog 进行比较。
  - 只构建、测试、打包和验证发生变化的模块。
  - 为完整模块集合生成 catalog 和 Release 正文表格。
  - 只向新 Release 上传变化模块的包。
  - 非 dry-run 发布必须满足 `SIGNING_ENABLED=true`。

## 开发环境要求

- Windows
- 由 `global.json` 选择的 .NET SDK
- PowerShell 7
- 用于发布工作流的 GitHub CLI
- 用于正式签名发布的 `cosign` 或 GitHub Action 安装器

## 初始迁移计划

1. 将 12 个官方模块工程放入 `src/`。
2. 填充 `module-registry.json`。
3. 增加模块包验证测试和宿主兼容性矩阵测试。
4. 通过 dry-run 验证增量构建、模块打包和 catalog 生成流程。
5. 在主程序中增加远程 catalog 读取与模块安装客户端。
6. 只有在远程安装链路验证通过后，才移除主仓库的 `ProjectReference` 和随包 DLL 复制逻辑。