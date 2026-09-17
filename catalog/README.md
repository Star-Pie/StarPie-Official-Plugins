# Catalog 目录

`module-registry.json` 是本仓库维护的官方模块事实来源。

`module-catalog.json` 是生成的发布产物。每次模块集合 Release 都会重新生成并发布一份 catalog，为每个官方模块引用当前应使用的模块包。

仓库不会提交生成的 `module-catalog.json`。发布工作流会下载最近一次已发布 catalog，用本次变化模块覆盖对应条目，然后发布新的完整 catalog。