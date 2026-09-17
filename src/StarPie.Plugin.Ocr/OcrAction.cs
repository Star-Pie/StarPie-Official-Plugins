using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.Ocr;

/// <summary>
/// 动作「截屏识字 (OCR)」。对应配置里的 <c>Type="Ocr"</c>（历史别名 <c>ScreenOcr</c>）。
/// <para>
/// <b>无参数</b>：识别区域由用户在按下扇区之后<b>现场框选</b>，没有任何需要事先保存的配置。
/// 这也是为什么它的手写面板里没有输入控件，只有一段说明与两枚按钮
/// （✂️ 立即测试截屏、⚙️ 接口配置）—— 那两枚按钮操作的是<b>全局</b> OCR 设置与一次性测试，
/// 不属于某个扇区的动作参数，因此不进 <see cref="Parameters"/>。
/// </para>
/// <para>
/// <b>执行体只有一行转发</b>：截图、框选、识别全在宿主侧。插件不碰任何截图 API，
/// 这正是「宿主自己的动作真的能靠 SDK 独立跑起来」的检验方式之一。
/// </para>
/// </summary>
internal sealed class OcrAction : IActionContribution
{
    private readonly IPluginContext _context;

    public OcrAction(IPluginContext context) => _context = context;

    // 属性而不是缓存字段：界面语言可以在运行时切换，每次访问重新取词条，界面才会跟着语言走。
    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "ocr",
        DisplayName = T("ocr.title", Texts.OcrTitle),
        Description = null,

        // 分类留空：认领了顶层类型的动作出现在「动作类型」主下拉里，不进插件动作的分组体系。
        Category = "",
        IconKey = null,

        // 【必须与拆分前一致】原来它作为内建动作就在动作线程上同步执行。
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    /// <summary>无参数。按约定返回空列表而不是 null。</summary>
    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    /// <summary>无参数可校验。</summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    /// <summary>无参数可预览。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";

    /// <summary>
    /// 执行。
    /// <para>
    /// <b>线程约束</b>：调用方（<c>PluginInvoker</c>）在唯一的动作线程上以同步方式等待本方法，
    /// 所以实现里绝不能出现真正的异步等待 —— 一旦 <c>await</c> 到别的上下文就会死锁。
    /// 宿主那侧自己会把框选识别 Task.Run 起来，这里只需发起。
    /// </para>
    /// </summary>
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        try
        {
            _context.ScreenCapture.CaptureAndRecognize();
        }
        catch (PluginCapabilityDeniedException ex)
        {
            // 走到这里说明清单的 capabilities 里少了 ScreenCapture。
            // 异常消息本身已写明怎么修，这里把它记进日志，再给用户一句人话 ——
            // 用户不该看到 .NET 异常文本。
            _context.Log.Error("截屏识别被宿主拒绝：本插件未声明 ScreenCapture 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        return Task.FromResult(ActionResult.Empty);
    }
}
