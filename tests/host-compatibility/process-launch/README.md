# 三态权限调用回归

运行：dotnet run --project tests/host-compatibility/process-launch/ProcessLaunchTests.csproj -c Release

零测试框架依赖，直接编译三个插件的实际动作实现；所有宿主操作均为测试替身，不启动进程、不弹 UAC。
覆盖三态参数声明与传递、非法值双层拒绝、旧 Launch 参数兼容、新值优先、四语选项、失败不触发插件隔离以及旧 SDK 适配器兼容。
