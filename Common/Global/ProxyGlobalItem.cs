using PlayerProxyLib.Common.ProxyPlayer;
using Terraria;
using Terraria.ModLoader;

namespace PlayerProxyLib.Common.Global
{
    public class ProxyGlobalItem : GlobalItem
    {
        public override bool? CanHitNPC(Item item, Player player, NPC target)
        {
            if (!player.IsProxyPlayer())
                return null;

            ProxyPlayerModPlayer proxyData =
                player.GetModPlayer<ProxyPlayerModPlayer>();

            // Never hit the entity that owns this proxy.
            if (proxyData.owner is NPC ownerNPC &&
                ownerNPC.whoAmI == target.whoAmI)
            {
                return false;
            }

            // Prevent friendly fire.
            if (target.friendly == !player.hostile)
                return false;

            return null;
        }
    }
}
