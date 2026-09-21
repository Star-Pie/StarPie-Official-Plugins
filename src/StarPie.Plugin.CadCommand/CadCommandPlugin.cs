using StarPie.Plugin;

namespace StarPie.Plugin.CadCommand;

/// <summary>
/// StarPie 独立单体插件入口：「CAD 模拟按键与命令输入」。
/// <para>
/// 专为 AutoCAD、浩辰CAD、中望CAD 等工程绘图软件用户打造：
/// 1. 规避中文输入法（IME）干扰：采用 Unicode 字符流注入，即使输入法处于中文状态也能准确下发英文命令；
/// 2. 模拟 CAD 宏经典的前置 ^C^C（两次 ESC）清空，避免在旧命令未完时报错；
/// 3. 支持 CAD 习惯的空格（Space）与回车（Enter）提交，或保留在命令行手动跟参数。
/// </para>
/// </summary>
public sealed class CadCommandPlugin : IStarPiePlugin
{
    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;

        // ① 注册多语言词条
        Texts.Register(context);

        // ② 注册 CAD 绘图与命令行风格的 SVG 矢量图标（直尺与铅笔构成的绘图图标）
        string iconKey = context.Icons.RegisterSvg("cad",
            "M20.71,7.04 C21.1,6.65 21.1,6 20.71,5.63 L18.37,3.29 C18,2.9 17.35,2.9 16.96,3.29 L15.13,5.12 L18.88,8.87 L20.71,7.04 Z M3,17.25 L3,21 L6.75,21 L17.81,9.94 L14.06,6.19 L3,17.25 Z M5,18.59 L5.41,19 L7.41,19 L15.69,10.72 L13.28,8.31 L5,16.59 L5,18.59 Z");

        // ③ 注册动作贡献点
        context.Actions.Register(new CadCommandAction(context, iconKey));

        context.Log.Info("CAD 模拟按键插件已成功初始化");
    }

    public void Shutdown()
    {
        // 关键：断开上下文引用，确保 ALC 能够被 CLR 垃圾收集器正常回收
        _context = null;
    }
}
