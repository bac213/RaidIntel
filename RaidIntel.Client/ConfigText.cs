using BepInEx.Configuration;

namespace RaidIntel.Client;

/// <summary>
/// Display metadata read by BepInEx ConfigurationManager (F12) via reflection.
/// Class name and public field names must match ConfigurationManager expectations.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    public string Category;
    public string DispName;
    public int? Order;
    public bool? IsAdvanced;
}

internal sealed class ConfigText
{
    private readonly ConfigFile _config;
    private int _order = 10000;

    internal bool Advanced { get; set; }

    internal ConfigText(ConfigFile config)
    {
        _config = config;
    }

    internal ConfigEntry<T> Bind<T>(
        string section,
        string key,
        T defaultValue,
        string category,
        string name,
        string description,
        AcceptableValueBase range = null)
    {
        var tags = new ConfigurationManagerAttributes
        {
            Category = category,
            DispName = name,
            Order = _order--,
            IsAdvanced = Advanced ? true : null,
        };
        return _config.Bind(section, key, defaultValue, new ConfigDescription(description, range, tags));
    }

    internal static AcceptableValueRange<float> Range(float min, float max) => new(min, max);
    internal static AcceptableValueRange<int> Range(int min, int max) => new(min, max);
}
