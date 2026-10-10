using System.Text.Json;
using System.Text.Json.Serialization;
namespace StarPie.Plugin.Sound;

// DTO元数据由插件内生成，避免反射序列化元数据跨越可回收程序集生命周期。
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SoundDocument))]
[JsonSerializable(typeof(SoundPreferences))]
[JsonSerializable(typeof(CustomSoundProfile))]
[JsonSerializable(typeof(SoundEventConfig))]
[JsonSerializable(typeof(List<CustomSoundProfile>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal partial class SoundJsonContext : JsonSerializerContext { }
