using PlayerProxyLib.Common.NetWorking;
using PlayerProxyLib.Common.ProxyPlayer;
using Terraria.ModLoader;

public class PlayerProxyWorld : ModSystem
{
    public override void PreUpdatePlayers()
    {
        ProxyPlayerManager.PreparePlayers();
    }

    public override void PostUpdateTime()
    {
        ProxyPlayerManager.Tick();
        base.PostUpdateTime();
    }

    public override void OnWorldUnload()
    {
        ProxyPlayerManager.ClearDictionaries();
        base.OnWorldUnload();
    }
}
