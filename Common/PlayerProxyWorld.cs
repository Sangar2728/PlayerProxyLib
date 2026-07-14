using PlayerProxyLib.Common;
using Terraria.ModLoader;

public class PlayerProxyWorld : ModSystem
{
    private static int _gcTimer;
    public override void PostUpdateTime()
    {
        // 30 ticks beacause 60 is to slow
        const int refreshRate = 30; // Refresh rate in ticks
        if (++_gcTimer >= refreshRate)
        {
            _gcTimer = 0;
            ProxyPlayers.GarbageCollector();
        }
        base.PostUpdateTime();
    }

    public override void OnWorldUnload()
    {
        ProxyPlayers.ClearDictionaries();
        base.OnWorldUnload();
    }
}