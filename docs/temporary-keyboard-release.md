# 临时小键盘0.1.0发行说明

最低宿主为StarPie `2.0.1-beta.4`，SDK API为`1.15`。这是独立插件，通过官方插件目录获取，不复制进主程序便携包或安装器。`show`呼出数字、字母、功能键、全键盘或纯按键自定义布局；`configure`编辑默认3×4、可调整行列的按键方案。旧动作型方案文件继续保留，不自动迁移为纯按键方案。

用户已完成当前功能实机验收；发行阶段启用manifest的`release.enabled`。最高五级子菜单仍是宿主后续规划，不属于该插件功能。

## 发行链路

`Import-ModuleRegistryFromSource.ps1`优先沿用既有类型认领和动作源码识别；多动作插件未单独提供`*Action.cs`时，使用manifest首个显式feature ID作为目录入口。本插件入口为`show`。目录的SDK版本从提交中的PluginApi读取，避免停留在旧1.14。

`Build-Modules.ps1 -RunTests`发现模块专用`tests/<id>/Validate-Module.ps1`时使用该入口，否则保留原有通用测试流程。小键盘入口为每轮创建独占数据目录，执行原模型／存储、编辑、纯按键、假分发和离屏UI五套检查。UI测试绑定实际插件SHA-256及显式SDK SHA-256，不需要依赖或引用宿主程序集；原有显式宿主候选用法继续有效。没有Show、真实输入、真实插件动作或系统改动。

本地验证示例：

```powershell
./build/Import-ModuleRegistryFromSource.ps1
./build/Build-Modules.ps1 -ModuleIds starpie.plugin.temporarykeyboard -Configuration Release -RunTests
```

发行使用`Release Official Module Set`，选择`beta`、模块`starpie.plugin.temporarykeyboard`；首次先执行dry-run验证打包和目录合成。发布后须核对GitHub及AtomGit附件、SHA-256、实际目录兼容筛选和用户安装确认；不把本地验证或草稿当作已发布。
