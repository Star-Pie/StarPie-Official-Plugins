using StarPie.Plugin;

namespace StarPie.Plugin.Ocr;

/// <summary>
/// StarPie 随包动作包「截屏识字 (OCR)」。
/// <para>
/// 它认领两个顶层动作类型：<c>Ocr</c> 与历史别名 <c>ScreenOcr</c> ——
/// 认领声明在 csproj 的 <c>StarPiePluginTypeClaims</c> 程序集元数据里，
/// 宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。这个动作会读取屏幕内容，
/// 想单独关掉它的用户不在少数 —— 而它从前与「系统控制」同属一个包，要关就得一起关。
/// </para>
/// <para>
/// <b>它声明 <c>ScreenCapture</c></b>：单独一项而不是并进 <c>Ui</c>，
/// 因为截屏是隐私敏感能力，安装确认页上必须让用户看见「它会看到我的屏幕」。
/// 执行走 <c>IHostScreenCaptureService</c>，未声明会被直接拒绝，绝不静默降级。
/// </para>
/// </summary>
public sealed class OcrPlugin : IStarPiePlugin
{
    // 刻意不缓存 IPluginContext 的任何「服务实例」：Shutdown 之后任何一次残留调用
    // 都会摸到一个已被卸载的 ALC 里的对象。只留 context 一个引用、置空即断链。
    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;

        // ① 词条先登记：Descriptor 与 Parameters 会在属性访问时查当前语言文案。
        Texts.Register(context);

        // ② 动作登记。短 ID 必须与 csproj 里认领串右侧的值逐字一致 ——
        //    两个类型名（Ocr / ScreenOcr）指向的是同一个短 ID `ocr`。
        context.Actions.Register(new OcrAction(context));

        context.Log.Info("截屏识字包已就绪");
    }

    public void Shutdown() => _context = null;
}
