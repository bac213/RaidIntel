using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace RaidIntel.Server;

[Injectable(TypePriority = OnLoadOrder.Routers)]
public class RaidIntelRouter(JsonUtil jsonUtil, RaidIntelRouterCallback callback)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<EmptyRequestData>(
            "/raidIntel/quests",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.HandleGetQuests(sessionId)
        ),
    ])
{
}

[Injectable]
public class RaidIntelRouterCallback(
    HttpResponseUtil httpResponseUtil,
    QuestRequirementService questRequirementService)
{
    public ValueTask<string> HandleGetQuests(MongoId sessionId)
    {
        return new ValueTask<string>(httpResponseUtil.NoBody(questRequirementService.BuildQuestIntel(sessionId)));
    }
}
