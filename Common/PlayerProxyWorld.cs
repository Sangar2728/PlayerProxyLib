using PlayerProxyLib.Common;
using Terraria.ModLoader;

public class PlayerProxyWorld : ModSystem
{
    public override void PreUpdatePlayers()
    {
        ProxyPlayers.PreparePlayers();
    }

    public override void PostUpdateTime()
    {
        ProxyPlayers.Tick();
        base.PostUpdateTime();
    }

    public override void OnWorldUnload()
    {
        ProxyPlayers.ClearDictionaries();
        base.OnWorldUnload();
    }
}
