using StarPie.Plugin;

namespace StarPie.Plugin.KeypadLayer;

/// <summary>
/// StarPie 官方独立插件入口：「按键映射」。
/// <para>
/// 通过轮盘开启或关闭自定义按键映射，将物理按键映射为目标按键：
/// 1. 支持可视化配置映射，默认提供左手数字小键盘预设（QWE/ASD/ZXC/R ➔ 789/456/123/0）；
/// 2. 宿主集中式管理：由宿主唯一的低级键盘钩子高效注入扫描码，失焦与长按 Esc 紧急释放；
/// 3. 单扇区切换：在轮盘中触发一次即开启，再次触发即关闭。
/// </para>
/// </summary>
public sealed class KeypadLayerPlugin : IStarPiePlugin
{
    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;

        // ① 注册多语言词条（zh-CN, zh-TW, en, ja）
        Texts.Register(context);

        // ② 注册数字小键盘风格的 SVG 矢量图标
        string iconKey = context.Icons.RegisterSvg("keypad",
            "M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3M19,19H5V5H19V19M7,7H9V9H7V7M11,7H13V9H11V7M15,7H17V9H15V7M7,11H9V13H7V11M11,11H13V13H11V11M15,11H17V13H15V11M7,15H9V17H7V15M11,15H13V17H11V15M15,15H17V17H15V15Z");

        // ③ 注册动作贡献点
        context.Actions.Register(new KeypadLayerAction(context, iconKey));

        context.Log.Info("按键映射插件已成功初始化");
    }

    public void Shutdown()
    {
        try
        {
            // 停用时幂等撤销可能残留的按键映射会话
            _context?.KeyboardRemap?.Deactivate();
        }
        catch
        {
            // 宿主停用逻辑具备终极兜底安全撤销，插件侧忽略任何清理异常
        }
        finally
        {
            // 关键：断开上下文引用，确保 ALC 能够被 CLR 垃圾收集器正常回收
            _context = null;
        }
    }
}
