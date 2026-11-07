using CommonAssets.Scripts.Cutscenes;
using EFT;
using Fika.Core.Networking.Packets.World;

namespace Fika.Core;

/// <summary>
/// 移植占位（Stub）：dev-5.0 分支里 FikaCutsceneManager / FikaFinalMissionDirector
/// 只被调用、没有提交实现文件，导致编译失败 19 处。
/// 这里提供空实现让工程能编过；运行期行为 = 过场/终局剧情不同步。
/// 上游补上真正实现后应删除本文件。
/// </summary>
public sealed class FikaCutsceneManager
{
    public static FikaCutsceneManager Instance { get; private set; }

    public bool ReplayingHostStart => false;

    public bool IsActive => false;

    public static void Create(GameWorld gameWorld)
    {
        Instance = new FikaCutsceneManager();
    }

    public void Tick() { }

    public void Dispose()
    {
        Instance = null;
    }

    public void LocalViewComplete(object processId) { }

    public void HostStartedCutscene(string cutsceneId, CutsceneProcess process, bool fixedTime, bool forceMode, float initTime) { }

    public void CutsceneEnded(object processId, bool ended) { }

    public void ApplyStartPacket(CutsceneStartPacket packet) { }

    public void ReceiveSkipPacket(CutsceneSkipPacket packet) { }

    public void ReceiveEndPacket(CutsceneEndPacket packet) { }

    public void ReceiveSkipIntention(CutsceneSkipIntentionPacket packet) { }

    public void ReceiveViewComplete(CutsceneViewCompletePacket packet) { }
}

public sealed class FikaFinalMissionDirector
{
    public static FikaFinalMissionDirector Instance { get; private set; }

    public static void Create(GameWorld gameWorld)
    {
        Instance = new FikaFinalMissionDirector();
    }

    public static void StripProfile(EFT.Profile profile, int netId) { }

    public void Tick() { }

    public void OnPacketReceived(FinalMissionPacket packet) { }
}
