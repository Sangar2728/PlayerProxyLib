using PlayerProxyLib.Common.ProxyPlayer;
using Terraria;
using Terraria.ModLoader;

namespace PlayerProxyLib.Common.Global
{
    public class ProxyGlobalNPC : GlobalNPC
    {
        public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot)
        {
            if (target.IsProxyPlayer())
                return false;

            return true;
        }
    }
}
