using Microsoft.Xna.Framework;
using PlayerProxyLib.Common.NetWorking;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using static PlayerProxyLib.Common.NetWorking.ProxyPlayerManager;
using static PlayerProxyLib.Common.ProxyUtils;

namespace PlayerProxyLib.Common.ProxyPlayer
{
    public static class ProxyPlayers
    {
  

        /// <summary>
        /// Gets a proxy for an active NPC or Projectile. Clients return null until the server
        /// supplies the proxy. Equipment, buffs, and other extra Player state belong to the caller.
        /// </summary>
        public static Player GetPlayerProxy(this Entity entity, bool sync = true)
        {
            if (!IsEntityValid(entity) || !TryDescribe(entity, out EntityDescriptor descriptor))
                return null;

            _players.TryGetValue(entity, out ProxyRecord record);
            if (record != null && !IsRecordValid(record))
            {
                ReleaseRecord(record, Main.netMode == NetmodeID.Server);
                record = null;
            }

            if (record == null)
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    RequestProxy(descriptor);
                    return null;
                }

                int slot = GetAvailableWhoAmI();
                if (slot < 0)
                    return null;

                uint generation = ++_nextGeneration;
                if (generation == 0)
                    generation = ++_nextGeneration;
                record = TryCreateProxy(entity, descriptor, slot, generation, 0);
                if (record == null)
                    return null;
                if (Main.netMode == NetmodeID.Server)
                    SendProxyCreation(record);
            }

            if (sync)
                Sync(record.Player, entity);
            return record.Player;
        }

        private static bool TryDescribe(Entity entity, out EntityDescriptor descriptor)
        {
            switch (entity)
            {
                case NPC npc:
                    descriptor = new(ProxyEntityType.NPC, npc.whoAmI, npc.type, -1, -1);
                    return true;
                case Projectile projectile:
                    descriptor = new(ProxyEntityType.Projectile, projectile.whoAmI,
                        projectile.type, projectile.owner, projectile.identity);
                    return true;
                default:
                    descriptor = default;
                    return false;
            }
        }

       

        public static bool IsRecordValid(ProxyRecord record) =>
            record != null && IsEntityValid(record.Entity)
            && Main.player.IndexInRange(record.Slot)
            && ReferenceEquals(Main.player[record.Slot], record.Player)
            // Netplay deactivates slots without a socket. Identity and owner lifetime
            // determine validity; losing active must not discard inventory or projectile counts.
            && TryDescribe(record.Entity, out EntityDescriptor current)
            && current.Key == record.Descriptor.Key;

        
        public static void UpdatePlayerProxy(this Entity entity) => GetPlayerProxy(entity);

        public static void DisposePlayerProxy(this Entity entity)
        {
            if (entity != null && _players.TryGetValue(entity, out ProxyRecord record))
                ProxyPlayerManager.ReleaseRecord(record, Main.netMode == NetmodeID.Server);
        }

        public static bool IsProxyPlayer(this Player player) =>
            player?.GetModPlayer<ProxyPlayerModPlayer>().isFakePlayer ?? false;

        public static void ConfigureProxyPlayer(this Entity entity, bool targetable = false,
            bool countForPlayerCount = false, bool shouldBeDrawn = false)
        {
            Player player = GetPlayerProxy(entity);
            if (player == null) return;

            byte previous = GetOptions(player);

            ProxyPlayerModPlayer state = player.GetModPlayer<ProxyPlayerModPlayer>();
            state.shouldBeIgnoredByNPCs = !targetable;
            state.shouldCountForPlayerCount = countForPlayerCount;
            state.shouldBeDrawn = shouldBeDrawn;

            if (Main.netMode == NetmodeID.Server && previous != GetOptions(player)
                && _players.TryGetValue(entity, out ProxyRecord record))
                SendProxyOptions(record);
        }
        public static Vector2 GetMouseWorld(this Player player)
        {
           return player.GetModPlayer<ProxyPlayerModPlayer>().mouseWorld;
        }
        public static void SetMouseWorld(this Player player, Vector2 mousePos)
        {
           player.GetModPlayer<ProxyPlayerModPlayer>().mouseWorld = mousePos;
        }
        public static void Sync(Player player, Entity entity)
        {
            player.Center = entity.Center;
            player.velocity = entity.velocity;
            player.direction = entity.direction;
            player.active = entity.active;
            player.dead = entity switch
            {
                NPC npc => npc.life <= 0,
                Projectile projectile => projectile.timeLeft <= 0,
                _ => false
            };
            player.GetModPlayer<ProxyPlayerModPlayer>().isFakePlayer = true;
        }

        

        internal static void GarbageCollector()
        {
            foreach (ProxyRecord record in _playersOwners.Values.ToArray())
                if (!IsRecordValid(record))
                    ReleaseRecord(record, Main.netMode == NetmodeID.Server);
        }

        
    }
}
