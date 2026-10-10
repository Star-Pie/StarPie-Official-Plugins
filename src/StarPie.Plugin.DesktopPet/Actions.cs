using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 动作一：显示桌宠。
/// <para>
/// 允许扇区参数单独覆盖尺寸、素材目录与动画开关；留空则回退至插件设置页的默认值。
/// </para>
/// </summary>
internal sealed class ShowPetContribution : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly PetController _controller;
    private readonly string? _iconKey;

    public ShowPetContribution(IPluginContext context, PetController controller, string? iconKey)
    {
        _context = context;
        _controller = controller;
        _iconKey = iconKey;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "showPet",
        ShowInActionPicker = false,
        DisplayName = "显示桌宠",
        DisplayNameKey = "action.show-pet.name",
        Description = "在屏幕上显示桌宠。轻点呼出轮盘，拖动可挪位置，收起请使用「隐藏桌宠」动作。",
        Category = "桌宠",
        IconKey = _iconKey,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public IReadOnlyList<ParameterField> Parameters => new List<ParameterField>
    {
        new()
        {
            Key = PetPreference.SizeKey,
            Label = "尺寸",
            LabelKey = "field.size.label",
            Type = ParameterFieldType.Number,
            DefaultValue = Defaults.SizeDiu.ToString("0.##", CultureInfo.InvariantCulture),
            Min = Defaults.SizeMin,
            Max = Defaults.SizeMax,
            HelpText = "留空则使用插件设置页中的默认尺寸。",
        },
        new()
        {
            Key = PetPreference.AssetDirectoryKey,
            Label = "素材目录",
            LabelKey = "field.asset-directory.label",
            Type = ParameterFieldType.Folder,
            DefaultValue = "",
            HelpText = "留空则使用插件设置页中的默认素材目录。",
        },
        new()
        {
            Key = PetPreference.EnableAnimationKey,
            Label = "播放动画",
            LabelKey = "field.animation.label",
            Type = ParameterFieldType.Bool,
            DefaultValue = "true",
            HelpText = "留空则使用插件设置页中的默认动画设置。",
        },
    };

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue(PetPreference.SizeKey, out string? sizeRaw) && !string.IsNullOrWhiteSpace(sizeRaw))
        {
            if (!double.TryParse(sizeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double size))
            {
                return _context.I18n.T("error.size-number", "尺寸必须是一个数值。");
            }
            if (size < Defaults.SizeMin || size > Defaults.SizeMax)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _context.I18n.T("error.size-range", "尺寸必须在 {0} 到 {1} 之间。"),
                    Defaults.SizeMin, Defaults.SizeMax);
            }
        }

        return null;
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        double size = Defaults.SizeDiu;
        if (parameters.TryGetValue(PetPreference.SizeKey, out string? sizeRaw) &&
            double.TryParse(sizeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            size = PetPreference.Clamp(parsed, Defaults.SizeMin, Defaults.SizeMax);
        }
        else
        {
            size = PetPreference.Size(_context);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            _context.I18n.T("preview.show-pet", "显示桌宠 · 尺寸 {0}px"),
            size.ToString("0.##", CultureInfo.InvariantCulture));
    }

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail(_context.I18n.T("error.cancelled", "动作已被取消。"));
        }

        if (!_context.Info.HasCapability(PluginCapability.Ui))
        {
            return ActionResult.Ok(
                _context.I18n.T("notify.no-ui-capability", "清单未声明「Ui（界面）」能力，无法创建桌宠窗口。"),
                silent: false);
        }

        double size = PetPreference.FromAction(
            input, PetPreference.SizeKey,
            PetPreference.Size(_context), Defaults.SizeMin, Defaults.SizeMax);

        string assetDir = PetPreference.FromAction(
            input, PetPreference.AssetDirectoryKey,
            PetPreference.AssetDirectory(_context));

        bool enableAnim = PetPreference.FromAction(
            input, PetPreference.EnableAnimationKey,
            PetPreference.EnableAnimation(_context));

        bool success = false;
        try
        {
            await _context.Dispatcher.InvokeAsync(
                () => { success = _controller.Show(size, assetDir, enableAnim); }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Log.Error("创建或显示桌宠窗口失败", ex);
            return ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.show-failed", "桌宠无法显示：{0}"),
                ex.Message));
        }

        if (!success)
        {
            // 环境未就绪（如素材缺失），以非静默 Ok 提示，防止被宿主计入熔断
            return ActionResult.Ok(
                _context.I18n.T("notify.asset-missing", "桌宠素材未就绪，请在插件设置中选择包含 idle.png 的素材目录。"),
                silent: false);
        }

        return ActionResult.Ok(Preview(input.Parameters), silent: true);
    }
}

/// <summary>
/// 动作二：隐藏桌宠。
/// </summary>
internal sealed class HidePetContribution : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly PetController _controller;

    public HidePetContribution(IPluginContext context, PetController controller)
    {
        _context = context;
        _controller = controller;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "hidePet",
        ShowInActionPicker = false,
        DisplayName = "隐藏桌宠",
        DisplayNameKey = "action.hide-pet.name",
        Description = "收掉当前显示的桌宠窗口，释放窗口与图像内存。",
        Category = "桌宠",
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    public string Preview(IReadOnlyDictionary<string, string> parameters) =>
        _context.I18n.T("preview.hide-pet", "收掉桌宠");

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail(_context.I18n.T("error.cancelled", "动作已被取消。"));
        }

        try
        {
            await _context.Dispatcher.InvokeAsync(_controller.Hide).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Log.Error("隐藏桌宠失败", ex);
            return ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.hide-failed", "桌宠没能收起来：{0}"),
                ex.Message));
        }

        return ActionResult.Ok(Preview(input.Parameters), silent: true);
    }
}

/// <summary>
/// 动作三：管理桌宠。
/// <para>
/// 打开桌宠管理窗口，支持导入本地形象、12 状态预览与形象切换。
/// </para>
/// </summary>
internal sealed class ManagePetContribution : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly PetController _controller;

    public ManagePetContribution(IPluginContext context, PetController controller)
    {
        _context = context;
        _controller = controller;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "managePet",
        ShowInActionPicker = false,
        DisplayName = "管理桌宠",
        DisplayNameKey = "action.manage-pet.name",
        Description = "打开桌宠形象管理面板，支持本地素材导入、12 状态预览与形象切换。",
        Category = "桌宠",
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    public string Preview(IReadOnlyDictionary<string, string> parameters) =>
        _context.I18n.T("preview.manage-pet", "打开桌宠管理面板");

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail(_context.I18n.T("error.cancelled", "动作已被取消。"));
        }

        try
        {
            await _context.Dispatcher.InvokeAsync(() => _controller.OpenManagerWindow()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Log.Error("打开桌宠管理面板失败", ex);
            return ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.manage-failed", "桌宠管理面板无法打开：{0}"),
                ex.Message));
        }

        return ActionResult.Ok(Preview(input.Parameters), silent: true);
    }
}
