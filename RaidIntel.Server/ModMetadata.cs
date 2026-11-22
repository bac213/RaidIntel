using SPTarkov.Server.Core.Models.Spt.Mod;

namespace RaidIntel.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.spt.raidintel";
    public string Name { get; init; } = "RaidIntel";
    public string Author { get; init; } = "bac213";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.1.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.5");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}
