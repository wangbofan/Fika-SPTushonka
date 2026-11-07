using FikaServer.Models.Fika.Config;
using FikaServer.Services.Cache;
using FikaServer.Services.Headless;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace FikaServer.OnLoad;


[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad)]
public sealed class FikaPostLoad(FikaServerConfig fikaServerConfig, HeadlessProfileService headlessProfileService,
    FriendRequestsService friendRequestsService, PlayerRelationsService playerRelationsCacheService) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // 原版只在 amount > 0 时才加载无头档案，导致 amount 与实际档案不一致时
        // 服务端把已存在的无头档案全判为 "Invalid headless client"。
        // 这里改成始终加载：amount 只用于“自动补建”，不再决定“是否加载”。
        await headlessProfileService.OnPostLoadAsync(cancellationToken);

        await playerRelationsCacheService.OnPostLoad();
        friendRequestsService.OnPostLoad();
    }
}
