using System.Collections.Generic;
using System.Linq;

namespace StarPie.Plugin.EmojiPicker;

internal sealed record EmojiCategoryMeta(string Id, string TitleKey, string TooltipKey);

internal static class EmojiCatalog
{
    internal static readonly string[] DefaultRecommendedGlyphs =
    [
        "😄", "👍", "❤️", "🎉", "🚀", "👀", "🙏", "🔥"
    ];

    internal static readonly EmojiCategoryMeta[] CategoryTabs =
    [
        new("recent", "secRecommended", "catRecent"),
        new("smileys", "catSmileys", "catSmileys"),
        new("people", "catPeople", "catPeople"),
        new("animals", "catAnimals", "catAnimals"),
        new("food", "catFood", "catFood"),
        new("travel", "catTravel", "catTravel"),
        new("activities", "catActivities", "catActivities"),
        new("objects", "catObjects", "catObjects"),
        new("symbols", "catSymbols", "catSymbols"),
        new("customJump", "secCustom", "catCustom")
    ];

    internal static readonly EmojiEntry[] BuiltInEmojis =
    [
        // Smileys & Emotion (48 entries, dense 6x8 layout matching reference screenshot)
        new("😀", "嘿嘿笑", "Grinning Face", "xiao ha laugh smile kaixin 开心 笑", "smileys"),
        new("😃", "大笑", "Grinning Big Eyes", "xiao laugh happy 开心", "smileys"),
        new("😄", "露齿笑", "Grinning Smiling Eyes", "xiao grin happy 开心", "smileys"),
        new("😁", "呲牙笑", "Beaming Face", "ciya xiao grin", "smileys"),
        new("😆", "眯眼大笑", "Grinning Squinting", "haha xiao xd 大笑", "smileys"),
        new("😅", "苦笑流汗", "Grinning Sweat", "ganga kuxiao han sweat 尴尬", "smileys"),
        new("🤣", "笑得打滚", "Rolling Laughing", "rofl xiaosi 哈哈哈 笑死", "smileys"),
        new("😂", "笑哭了", "Tears of Joy", "xiaoku joy cry laugh 笑哭", "smileys"),
        new("🙂", "呵呵微笑", "Slightly Smiling", "weixiao hehe smile 微笑", "smileys"),
        new("🙃", "倒立微笑", "Upside Down Face", "daoli wuyu 无语", "smileys"),
        new("🫠", "融化社死", "Melting Face", "ronghua melt shesi 社死", "smileys"),
        new("😉", "眨眼", "Winking Face", "zhayan wink 暗示", "smileys"),
        new("😊", "害羞微笑", "Smiling Eyes", "haixiu blush smile 开心", "smileys"),
        new("😇", "天使光环", "Smiling Halo", "tianshi angel innocent", "smileys"),
        new("🥰", "满眼爱意", "Smiling Hearts", "aixin love xihuan 喜欢", "smileys"),
        new("😍", "花痴眼", "Heart Eyes", "huachi love heart", "smileys"),
        new("🤩", "星星眼", "Star Struck", "xingxing star wa 哇", "smileys"),
        new("😘", "飞吻", "Blowing Kiss", "feiwen kiss 么么", "smileys"),
        new("😗", "亲亲", "Kissing Face", "qinqin kiss", "smileys"),
        new("☺️", "腼腆笑", "Smiling Face", "miantian smile", "smileys"),
        new("😚", "闭眼亲", "Kissing Closed Eyes", "qin kiss", "smileys"),
        new("😙", "微笑亲", "Kissing Smiling Eyes", "qin kiss", "smileys"),
        new("🥲", "含泪微笑", "Smiling With Tear", "hanlei tear smile 强颜欢笑", "smileys"),
        new("😋", "好吃舔舌", "Savoring Food", "haochi yum delicious", "smileys"),
        new("😛", "吐舌头", "Face With Tongue", "tushe tongue", "smileys"),
        new("😜", "眨眼吐舌", "Winking Tongue", "tiaopi wink tongue 调皮", "smileys"),
        new("🤪", "滑稽疯笑", "Zany Face", "huaji crazy 滑稽", "smileys"),
        new("😝", "眯眼吐舌", "Squinting Tongue", "tiaopi tongue", "smileys"),
        new("🤑", "见钱眼开", "Money Mouth", "facai money qian 发财", "smileys"),
        new("🤗", "抱抱", "Hugging Face", "baobao hug 拥抱", "smileys"),
        new("🤭", "捂嘴偷笑", "Hand Over Mouth", "touxiao wuzui giggle 偷笑", "smileys"),
        new("🫢", "惊讶捂嘴", "Open Eyes Hand Over Mouth", "jingya gasp 惊讶", "smileys"),
        new("🫣", "捂眼偷看", "Peeking Eye", "toukan peek 偷看", "smileys"),
        new("🤫", "嘘声安静", "Shushing Face", "xu quiet hush 安静", "smileys"),
        new("🤔", "思考", "Thinking Face", "sikao think 琢磨", "smileys"),
        new("🫡", "敬礼收到", "Saluting Face", "jingli salute shoudao 收到", "smileys"),
        new("🤐", "闭嘴拉链", "Zipper Mouth", "bizui zip shut 闭嘴", "smileys"),
        new("🤨", "挑眉怀疑", "Raised Eyebrow", "huaiyi doubt 怀疑", "smileys"),
        new("😐", "面无表情", "Neutral Face", "lengmo neutral 冷漠", "smileys"),
        new("😑", "无语", "Expressionless Face", "wuyu 无语", "smileys"),
        new("😶", "沉默无言", "No Mouth", "chenmo silent 沉默", "smileys"),
        new("🙄", "翻白眼", "Rolling Eyes", "fanbaiyan eyeroll wuyu", "smileys"),
        new("😬", "尴尬咬牙", "Grimacing Face", "ganga grimace 尴尬", "smileys"),
        new("😮‍💨", "长叹一口气", "Exhaling Face", "tanqi sigh 叹气", "smileys"),
        new("😴", "困倦睡觉", "Sleeping Face", "shuijiao sleep kun 困", "smileys"),
        new("🥳", "派对庆祝", "Partying Face", "paidui party qingzhu 庆祝", "smileys"),
        new("😎", "墨镜酷", "Sunglasses", "ku cool 帅", "smileys"),
        new("😭", "放声大哭", "Loudly Crying", "daku cry sob 呜呜", "smileys"),

        // People & Gestures (16 entries)
        new("👍", "点赞真棒", "Thumbs Up", "zan dianzan good ok 点赞", "people"),
        new("👎", "差评踩", "Thumbs Down", "cai bad", "people"),
        new("👌", "OK 没问题", "OK Hand", "ok haode 好的 没问题", "people"),
        new("✌️", "耶胜利", "Victory Hand", "ye peace victory 耶", "people"),
        new("🤞", "祈祷好运", "Crossed Fingers", "qidao luck 好运", "people"),
        new("👏", "鼓掌啪啪", "Clapping Hands", "guzhang clap 鼓掌", "people"),
        new("🙏", "拜托感谢", "Folded Hands", "baituo xiexie pray thanks 谢谢 拜托", "people"),
        new("🤝", "握手合作", "Handshake", "woshou handshake 合作", "people"),
        new("💪", "加油力量", "Flexed Biceps", "jiayou muscle strong 加油", "people"),
        new("🫶", "比心手势", "Heart Hands", "bixin heart 比心", "people"),
        new("👋", "挥手再见", "Waving Hand", "zaijian hi bye wave 再见 你好", "people"),
        new("🙌", "举手欢呼", "Raising Hands", "huanhu yay 欢呼", "people"),
        new("🤦", "扶额无语", "Person Facepalming", "fue facepalm 无语", "people"),
        new("🤷", "摊手无奈", "Person Shrugging", "tanshou shrug 无奈", "people"),
        new("👀", "围观吃瓜", "Eyes", "weiguan look chigua 围观", "people"),
        new("🧠", "智慧大脑", "Brain", "danao brain 聪明", "people"),

        // Animals & Nature (16 entries)
        new("🐱", "小猫", "Cat Face", "mao cat 猫咪", "animals"),
        new("🐶", "小狗", "Dog Face", "gou dog 狗狗", "animals"),
        new("🐼", "大熊猫", "Panda", "xiongmao panda", "animals"),
        new("🦊", "狐狸", "Fox", "huli fox", "animals"),
        new("🐸", "青蛙", "Frog", "qingwa frog", "animals"),
        new("🐟", "摸鱼小鱼", "Fish", "moyu yu fish 摸鱼", "animals"),
        new("🦈", "鲨鱼", "Shark", "shayu shark", "animals"),
        new("🦋", "蝴蝶", "Butterfly", "hudie butterfly", "animals"),
        new("🌸", "樱花", "Cherry Blossom", "yinghua flower 花", "animals"),
        new("🌻", "向日葵", "Sunflower", "xiangrikui flower", "animals"),
        new("🌿", "绿叶植物", "Herb", "zhiwu leaf 摸鱼养生", "animals"),
        new("🌙", "弯月夜晚", "Crescent Moon", "yueliang moon 晚安", "animals"),
        new("☀️", "太阳晴天", "Sun", "taiyang sun 早安", "animals"),
        new("⭐", "星星", "Star", "xingxing star", "animals"),
        new("🌈", "彩虹", "Rainbow", "caihong rainbow", "animals"),
        new("⚡", "闪电", "High Voltage", "shandian fast lightning 极速", "animals"),

        // Food & Drink (16 entries)
        new("☕", "咖啡续命", "Hot Beverage", "kafei coffee xuming 咖啡", "food"),
        new("🧋", "奶茶", "Bubble Tea", "naicha boba 奶茶", "food"),
        new("🍵", "绿茶热茶", "Teacup", "cha tea 喝茶", "food"),
        new("🍺", "干杯啤酒", "Beer Mug", "pijiu ganbei beer 干杯", "food"),
        new("🥂", "碰杯庆祝", "Clinking Glasses", "pengbei cheers 庆祝", "food"),
        new("🍔", "汉堡", "Hamburger", "hanbao burger", "food"),
        new("🍟", "薯条", "French Fries", "shutiao fries", "food"),
        new("🍕", "披萨", "Pizza", "pisa pizza", "food"),
        new("🍜", "热拉面", "Steaming Bowl", "lamian noodles 干饭", "food"),
        new("🍱", "便当盒饭", "Bento Box", "biandang lunch 午饭", "food"),
        new("🥟", "饺子", "Dumpling", "jiaozi dumpling", "food"),
        new("🍉", "吃瓜西瓜", "Watermelon", "chigua xigua melon 吃瓜", "food"),
        new("🍓", "草莓", "Strawberry", "caomei berry", "food"),
        new("🍰", "生日蛋糕", "Shortcake", "dangao cake 蛋糕", "food"),
        new("🍩", "甜甜圈", "Doughnut", "tiantianquan donut", "food"),
        new("🍿", "爆米花", "Popcorn", "baomihua movie 吃瓜看戏", "food"),

        // Travel & Places (8 entries)
        new("🚀", "火箭起飞", "Rocket", "huojian rocket qifei 起飞", "travel"),
        new("✈️", "飞机出行", "Airplane", "feiji plane travel 出差", "travel"),
        new("🚗", "汽车", "Automobile", "qiche car 开车", "travel"),
        new("🚄", "高铁出发", "High-Speed Train", "gaotie train", "travel"),
        new("🏠", "回家", "House", "jia home 下班回家", "travel"),
        new("🏢", "公司办公", "Office Building", "gongsi office 上班", "travel"),
        new("🗺️", "地图路线", "World Map", "ditu map", "travel"),
        new("⛵", "扬帆", "Sailboat", "fan sail", "travel"),

        // Activities & Sports (8 entries)
        new("🎉", "拉花庆祝", "Party Popper", "qingzhu party tada 恭喜", "activities"),
        new("🎊", "彩球祝贺", "Confetti Ball", "zhuhe confetti", "activities"),
        new("🏆", "奖杯冠军", "Trophy", "jiangbei trophy win 第一", "activities"),
        new("🎮", "游戏手柄", "Video Game", "youxi game 开黑", "activities"),
        new("🎨", "设计画板", "Artist Palette", "sheji huaban art 设计", "activities"),
        new("🎵", "音乐音符", "Musical Note", "yinyue music", "activities"),
        new("⚽", "足球", "Soccer Ball", "zuqiu soccer", "activities"),
        new("🎯", "精准靶心", "Bullseye", "baxin target 精准", "activities"),

        // Objects & Tools (16 entries)
        new("💡", "灵感灯泡", "Light Bulb", "linggan idea bulb 点子", "objects"),
        new("📌", "图钉置顶", "Pushpin", "zhiding pin 置顶", "objects"),
        new("💻", "笔记本电脑", "Laptop", "diannao laptop code 开发", "objects"),
        new("⌨️", "键盘", "Keyboard", "jianpan keyboard", "objects"),
        new("🖱️", "鼠标", "Computer Mouse", "shubiao mouse", "objects"),
        new("📱", "手机", "Mobile Phone", "shouji phone", "objects"),
        new("📎", "回形针附件", "Paperclip", "fujian clip 附件", "objects"),
        new("📁", "文件夹", "File Folder", "wenjianjia folder 目录", "objects"),
        new("🔧", "扳手维修", "Wrench", "banshou fix tool 修bug", "objects"),
        new("⚙️", "齿轮设置", "Gear", "shezhi setting config", "objects"),
        new("🔍", "放大镜搜索", "Magnifying Glass", "sousuo search find 查找", "objects"),
        new("📦", "打包发布", "Package", "dabao package release 发布", "objects"),
        new("📝", "备忘录文档", "Memo", "wendang note 记录", "objects"),
        new("🔒", "锁定隐私", "Locked", "suo lock", "objects"),
        new("🔔", "铃铛提醒", "Bell", "tixing notify 通知", "objects"),
        new("🎁", "礼物惊喜", "Wrapped Gift", "liwu gift", "objects"),

        // Symbols (16 entries)
        new("❤️", "红心喜欢", "Red Heart", "aixin hongxin heart love 爱", "symbols"),
        new("🩵", "浅蓝心", "Light Blue Heart", "lanxin blue heart", "symbols"),
        new("🔥", "火焰厉害", "Fire", "huo fire awesome 牛", "symbols"),
        new("✨", "闪亮星星", "Sparkles", "xingxing sparkles 闪耀", "symbols"),
        new("✅", "确认通过", "Check Mark", "tongguo ok check pass 通过", "symbols"),
        new("❌", "叉号否决", "Cross Mark", "no error cross 错误", "symbols"),
        new("⚠️", "注意警告", "Warning", "jinggao warn 注意", "symbols"),
        new("💯", "一百分满分", "Hundred Points", "manfen 100 满分", "symbols"),
        new("❓", "问号疑问", "Question Mark", "wenhao question 疑惑", "symbols"),
        new("❗", "感叹号", "Exclamation", "gantanhao bang 重要", "symbols"),
        new("➕", "加号", "Plus", "jia plus", "symbols"),
        new("🔄", "刷新同步", "Counterclockwise Arrows", "shuaxin refresh sync", "symbols"),
        new("💬", "对话气泡", "Speech Balloon", "duihua chat 回复", "symbols"),
        new("💭", "思考气泡", "Thought Balloon", "sikao think", "symbols"),
        new("🏷️", "标签", "Label", "biaoqian tag", "symbols"),
        new("🚩", "旗帜标记", "Triangular Flag", "qizhi flag", "symbols")
    ];

    internal static List<KaomojiEntry> CreateDefaultKaomojis() =>
    [
        new() { Id = "k1", Text = "٩(ˊᗜˋ*)و", Name = "超开心举手", Category = "kaoHappy", Keywords = "kaixin happy yay 举手 开心" },
        new() { Id = "k2", Text = "✧*｡٩(ˊᗜˋ*)و✧*｡", Name = "闪亮欢呼", Category = "kaoHappy", Keywords = "huanhu sparkle happy 欢呼" },
        new() { Id = "k3", Text = "Ciallo～(∠・ω< )⌒★", Name = "元气问候", Category = "kaoHappy", Keywords = "ciallo hi greet 问候" },
        new() { Id = "k4", Text = "( ͡° ͜ʖ ͡°)", Name = "滑稽懂的都懂", Category = "kaoHappy", Keywords = "huaji meme 滑稽" },
        new() { Id = "k5", Text = "(╯°□°）╯︵ ┻━┻", Name = "愤怒掀桌", Category = "kaoRoast", Keywords = "xianzhuo angry 吐槽 掀桌" },
        new() { Id = "k6", Text = "┬─┬ノ( º _ ºノ)", Name = "默默摆正桌子", Category = "kaoRoast", Keywords = "baizheng calm 冷静 摆正" },
        new() { Id = "k7", Text = "[doge]", Name = "狗头保命", Category = "kaoRoast", Keywords = "doge goutou 狗头" },
        new() { Id = "k8", Text = "(¬_¬)", Name = "嫌弃凝视", Category = "kaoRoast", Keywords = "xianqi stare 嫌弃" },
        new() { Id = "k9", Text = "(｡•́︿•̀｡)", Name = "委屈嘟嘴", Category = "kaoSad", Keywords = "weiqu sad 委屈" },
        new() { Id = "k10", Text = "_(┐「ε:)_", Name = "咸鱼躺平", Category = "kaoSad", Keywords = "xianyu tangping 摆烂 躺平" },
        new() { Id = "k11", Text = "(つД`)ノ", Name = "大哭求抱", Category = "kaoSad", Keywords = "daku cry 呜呜 哭" },
        new() { Id = "k12", Text = "收到，我马上处理！", Name = "快速响应短语", Category = "kaoPhrase", Keywords = "shoudao chuli 短语 收到" },
        new() { Id = "k13", Text = "辛苦啦，感谢反馈～", Name = "致谢短语", Category = "kaoPhrase", Keywords = "xinku ganxie thanks 短语 感谢" },
        new() { Id = "k14", Text = "已修复，请更新后复验 ✅", Name = "验收回复短语", Category = "kaoPhrase", Keywords = "xiufu fuyan pass 短语 修复" }
    ];

    internal static readonly string[] KaomojiCategories =
    [
        "kaoHappy", "kaoRoast", "kaoSad", "kaoPhrase"
    ];

    internal static string GetEmojiDisplayName(EmojiEntry entry, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return entry.NameZh;
        return languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? entry.NameZh : entry.NameEn;
    }

    internal static EmojiEntry? FindEmoji(string glyph) =>
        BuiltInEmojis.FirstOrDefault(e => string.Equals(e.Glyph, glyph, StringComparison.Ordinal));
}
