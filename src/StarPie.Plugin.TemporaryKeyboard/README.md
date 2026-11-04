# 临时小键盘

独立WPF官方插件源码候选，要求宿主 beta.4／SDK1.15。当前 `release.enabled=false`，尚未发布或随宿主携带。

支持数字、字母、功能键、全键盘、自定义五种模式。通过轮盘的“呼出小键盘”动作选择初始模式／方案，在非激活窗口连续点击输入；Esc、切换应用、暂停、停用关闭。修饰键只作用于下一次点击，不跨点击保持系统按下。

“编辑自定义键盘”管理命名方案与每页3×4插件动作。编辑器使用宿主参数表单；输入窗口在编辑前关闭。方案保存在插件私有目录 `keyboard-schemes.json`，上限1MiB，不通过有长度限制的普通设置字段保存。损坏原文不自动覆盖，显式恢复先备份；超限文件需人工恢复。

能力：Ui、InputSimulation、PluginActions。只在用户打开自定义页或选动作时加载相关的已启用插件，不自动安装／启用目标插件，不复制SendInput，不引用宿主DLL。

构建：`dotnet build src/StarPie.Plugin.TemporaryKeyboard/TemporaryKeyboard.csproj -c Release`。纯模型和存储测试设置 `KEYBOARD_TEST_ROOT` 为隔离证据目录，再运行 `tests/starpie.plugin.temporarykeyboard/ModelTests.csproj`。窗口模板测试需要显式 `StarPieCandidateDir` 与插件DLL／哈希参数，不调用Show，不发送输入。

真实输入、非激活焦点、IME、多屏DPI、管理员目标与跨插件动作均须人工验收。完整记录在宿主 `docs/plans/temporary-keyboard-verification.md`。
