using PlayerProxyLib.Common.ProxyPlayer;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace PlayerProxyLib.Common.Global
{
    public class ProxyGlobalProjectile : GlobalProjectile
    {
        public override bool? CanHitNPC(Projectile projectile, NPC target)
        {
            int owner = projectile.owner;

            if (owner < 0 || owner >= Main.maxPlayers)
                return null;

            Player player = Main.player[owner];

            if (player == null || !player.IsProxyPlayer())
                return null;

            ProxyPlayerModPlayer proxyData =
                player.GetModPlayer<ProxyPlayerModPlayer>();

            // El projectile pertenece al proxy de este mismo NPC.
            if (ReferenceEquals(proxyData.owner, target))
                return false;


            // Prevent friendly fire
            if (target.friendly == !player.hostile) return false;

            return null;
        }
        public override void OnSpawn(Projectile projectile, IEntitySource source)
        {
            int owner = projectile.owner;

            if (owner < 0 || owner >= Main.maxPlayers)
                return;

            Player player = Main.player[owner];

            if (player == null || !player.IsProxyPlayer())
                return;

            // Enemy proxy projectiles can damage real players.
            if (player.hostile)
                projectile.hostile = true;
        }
        public override bool CanHitPlayer(Projectile projectile, Player target)
        {
            if (target.IsProxyPlayer())
                return false;

            return true;
        }
    }
}

