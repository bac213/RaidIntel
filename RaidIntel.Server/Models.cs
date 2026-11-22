using System.Text.Json.Serialization;

namespace RaidIntel.Server;

public record QuestIntelResponse
{
    [JsonPropertyName("quests")]
    public List<QuestIntelEntry> Quests { get; init; } = new();
}

public record QuestIntelEntry
{
    [JsonPropertyName("questId")]
    public string QuestId { get; init; } = "";

    [JsonPropertyName("fallbackName")]
    public string FallbackName { get; init; } = "";

    [JsonPropertyName("traderId")]
    public string TraderId { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("repeatableKind")]
    public string RepeatableKind { get; init; } = "";

    [JsonPropertyName("requirements")]
    public List<QuestItemReqDto> Requirements { get; init; } = new();
}

public class QuestItemReqDto
{
    [JsonPropertyName("conditionId")]
    public string ConditionId { get; init; } = "";

    [JsonPropertyName("targets")]
    public List<string> Targets { get; init; } = new();

    [JsonPropertyName("requiredCount")]
    public int RequiredCount { get; init; }

    [JsonPropertyName("handedInCount")]
    public int HandedInCount { get; init; }

    [JsonPropertyName("completed")]
    public bool Completed { get; init; }

    [JsonPropertyName("onlyFoundInRaid")]
    public bool OnlyFoundInRaid { get; set; }

    [JsonPropertyName("isWeaponAssembly")]
    public bool IsWeaponAssembly { get; init; }

    [JsonPropertyName("isCategoryTarget")]
    public bool IsCategoryTarget { get; init; }

    [JsonPropertyName("isPlant")]
    public bool IsPlant { get; init; }
}
