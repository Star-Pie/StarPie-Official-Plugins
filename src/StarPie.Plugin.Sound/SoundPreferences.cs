namespace StarPie.Plugin.Sound;

public sealed class SoundPreferences
{
    public bool EnableSoundEffects { get; set; } = true;
    public double SoundVolume { get; set; } = 0.6;
    public string SoundTheme { get; set; } = "Mechanical";
    public bool SoundOnPopup { get; set; } = true;
    public bool SoundOnHover { get; set; } = true;
    public bool SoundOnExpand { get; set; } = true;
    public bool SoundOnExecute { get; set; } = true;
    public bool SoundOnCancel { get; set; } = true;
    public string ActiveCustomSoundProfileId { get; set; } = "cyber_haptic";
    public List<CustomSoundProfile> CustomSoundProfiles { get; set; } = CustomSoundProfile.CreateDefaultDemoProfiles();
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extras { get; set; }
}
