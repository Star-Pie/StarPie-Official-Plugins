# SDK 二进制兼容矩阵

本探针只使用假图片/结果服务和本地生成二维码，不展示窗口、不截图、不写剪贴板、不打开浏览器，也不通过宿主执行实际动作。

从官方仓库根运行 Run-SdkCompatibility.ps1。它从固定 e3aadbbfb656a89ba68c4c211461c3b87e0caece 读取 SDK 1.11 源码，用与此前已验收功能候选完全一致的 ImageTools-1.12.cs.txt 补成独立 SDK 1.12 契约，单独编译。冻结文本规范化 LF 的 SHA-256 为 93D4076E83ADDE684DEB846A1D481FF5ABB313EEB3A1A01B63DDCD34759DE034，不能从当前新接口自动生成基线。1.12 是旧源码候选，不是已公开发行 SDK 的声明。

Sdk12HostProbe 用实际旧 SDK 运行当前已构建插件 DLL，先确认 1.13 类型与方法均不存在，再调用插件初始化和内部假服务会话，验证解码、译后标题、明确选择与取消。执行的是当前 DLL，不重新编译插件以规避二进制问题。新 SDK 的上下文假对象不能替代此项。

脚本另用真正 1.11 契约编译 LegacyPlugin，只注册一个绝不能被执行的动作。其输出路径记在 artifacts/qr-sdk-compat/last-legacy-fixture.txt。主程序 scratch/qr-tools-tests/QrToolsTests.csproj 可用该路径作为第二参数，验证旧插件在新宿主中的真实扫描、加载、登记、撤销和 ALC 回收。

每次生成独立 artifacts/qr-sdk-compat/<GUID>，保留源、DLL 和证据；不删除既有目录。当前 SDK 身份仍为 1.0.0.0，宿主 API 1.13，扫码最低 API 1.12。SDK 1.11 宿主缺图片服务，应由目录兼容门禁拒绝。

ApiSurfaceProbe 将当前 SDK 与真实 1.11/1.12 DLL 的全部公开类型、接口实现和声明成员签名比较，要求零删除；新增接口与 DTO 字段允许。1.11 基线 859 项、1.12 基线 915 项。LegacyPlugin 同时在旧契约编译后访问 PluginManifest.Features 及 PluginFeature 全部原属性，补足运行时绑定证明。比较公开签名不等同于证明所有旧行为或任意第三方插件都兼容。
