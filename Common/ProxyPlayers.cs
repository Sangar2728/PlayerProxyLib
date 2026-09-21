using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using static PlayerProxyLib.Common.ProxyUtils;

namespace PlayerProxyLib.Common
{
    public static class ProxyPlayers
    {
        private static readonly Dictionary<Entity, int> _players = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<int, Entity> _playersOwners = [];

        /// <summary>
        /// Gets the persistent <see cref="Player"/> proxy associated with the specified <paramref name="entity"/>.
        /// If no proxy exists, one is created.
        /// </summary>
        /// <remarks>
        /// <para>The proxy remains associated with the entity until it is disposed or the entity becomes invalid.</para>
        /// <para>Only the proxy identity and synchronized properties are managed by the library.</para>
        /// <para>Any additional state stored in the returned <see cref="Player"/> is the consumer's responsibility.</para>
        /// 
        /// Example:
        /// <code>
        /// 
        /// Player proxy = npc.GetPlayerProxy();
        ///
        /// if (proxy != null)
        /// {
        ///     proxy.AddBuff(BuffID.TitaniumStorm, (int)Utils1.FormatTimeToTick(0, 0, 0, 6));
        ///
        ///     int p = Projectile.NewProjectile(Projectile.GetSource_None(), pos, velocity, ProjectileID.TitaniumStormShard, damage, 0, proxy.whoAmI);
        /// }
        /// </code>
        /// </remarks>
        /// <returns>
        /// <see cref="Player"/> if <paramref name="entity"/> is valid and <see cref="Main.player"/> has an available slot; otherwise <see langword="null"/>.
        /// </returns>

        public static Player GetPlayerProxy(this Entity entity, bool sync = true)
        {
            if (entity == null || !entity.active) return null;
            if (entity is NPC npc && npc.life <= 0) return null;

            if (!_players.TryGetValue(entity, out int playerWhoAmI))
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    SendProxyRequest(entity);
                    return null;
                }

                int index = ProxyUtils.GetAvailableWhoAmI();
                if (index <= -1) return null;

                CreateProxy(entity: entity, index);

                if (Main.netMode == NetmodeID.Server)
                    SendProxyCreation(entity, index);

                playerWhoAmI = index;
            }

            Player player = Main.player[playerWhoAmI];
            if(sync) Sync(player, entity);

            return player;
        }
        private static void SendProxyRequest(Entity entity)
        {
    
   
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ProxyEntityType entityType;

            if (entity is NPC)
                entityType = ProxyEntityType.NPC;
            else if (entity is Projectile)
                entityType = ProxyEntityType.Projectile;
            else
                return;

            ModPacket packet = ModContent
                .GetInstance<PlayerProxyLib>()
                .GetPacket();

            packet.Write((byte)MessageType.RequestProxy);
            packet.Write((byte)entityType);
            packet.Write((short)entity.whoAmI);

            packet.Send();
        }

        private static Player CreateProxy(Entity entity, int index)
        {
            _players[entity] = index;
            _playersOwners[index] = entity;

            Player player = new()
            {
                whoAmI = index,
                active = true
            };

            Main.player[index] = player;

            player.GetModPlayer<ProxyPlayerModPlayer>().owner = entity;

            return player;
        }

        private static void SendProxyCreation(Entity entity,int proxyWhoAmI,int toClient = -1)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            ProxyEntityType entityType;

            if (entity is NPC)
                entityType = ProxyEntityType.NPC;
            else if (entity is Projectile)
                entityType = ProxyEntityType.Projectile;
            else
                return;

            ModPacket packet = ModContent
                .GetInstance<PlayerProxyLib>()
                .GetPacket();

            packet.Write((byte)MessageType.CreateProxy);
            packet.Write((byte)entityType);
            packet.Write((short)entity.whoAmI);
            packet.Write((byte)proxyWhoAmI);

            packet.Send(toClient);
        }

        internal static void ReceiveProxyCreation(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ProxyEntityType entityType = (ProxyEntityType)reader.ReadByte();
            int entityWhoAmI = reader.ReadInt16();
            int proxyWhoAmI = reader.ReadByte();

            Entity entity = entityType switch
            {
                ProxyEntityType.NPC
                    when Main.npc.IndexInRange(entityWhoAmI)
                    => Main.npc[entityWhoAmI],

                ProxyEntityType.Projectile
                    when Main.projectile.IndexInRange(entityWhoAmI)
                    => Main.projectile[entityWhoAmI],

                _ => null
            };

            if (entity == null || !entity.active)
                return;

            CreateProxy(entity, proxyWhoAmI);
        }

        internal static void ReceiveProxyRequest(BinaryReader reader, int fromClient)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            ProxyEntityType entityType = (ProxyEntityType)reader.ReadByte();
            int entityWhoAmI = reader.ReadInt16();

            Entity entity = entityType switch
            {
                ProxyEntityType.NPC
                    when Main.npc.IndexInRange(entityWhoAmI)
                    => Main.npc[entityWhoAmI],

                ProxyEntityType.Projectile
                    when Main.projectile.IndexInRange(entityWhoAmI)
                    => Main.projectile[entityWhoAmI],

                _ => null
            };

            if (entity == null || !entity.active)
                return;

            Player proxy = entity.GetPlayerProxy();

            if (proxy == null)
                return;

            SendProxyCreation(entity, proxy.whoAmI, fromClient);
        }

        /// <summary>
        /// Refresh the sync with the <see cref="Player"/> proxy associated with the specified <paramref name="entity"/>.
        /// </summary>
        public static void UpdatePlayerProxy(this Entity entity) => GetPlayerProxy(entity);

        /// <summary>
        /// Disposes the <see cref="Player"/> proxy associated with the specified <paramref name="entity"/>.
        /// </summary>
        /// <remarks>
        /// <para>Removes the association between the entity and its proxy, releases the underlying
        /// <see cref="Main.player"/> slot, and invalidates the current proxy instance.</para>
        /// 
        /// <para>Calling <see cref="GetPlayerProxy(Entity, bool)"/> again for the same entity creates a new proxy.</para>
        /// <para>Once a proxy has been disposed, it must not be referenced again.</para>
        /// </remarks>
        public static void DisposePlayerProxy(this Entity entity)
        {
            if (!_players.TryGetValue(entity, out int playerWhoAmI)) return;
            Main.player[playerWhoAmI].Reset();
        }

        /// <summary>
        /// Check if player is a proxy
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public static bool IsProxyPlayer(this Player player) => player?.GetModPlayer<ProxyPlayerModPlayer>().isFakePlayer ?? false;

        /// <summary>
        /// Configures multiple behaviors of the proxy player.
        /// </summary>
        /// <remarks>
        /// If the specified <paramref name="entity"/> does not already have an associated proxy, one is created automatically.
        /// </remarks>
        /// <param name="entity"></param>
        /// <param name="targetable"></param>
        /// <param name="countForPlayerCount"></param>
        /// <param name="shouldBeDrawn"></param>
        public static void ConfigureProxyPlayer(this Entity entity, bool targetable = false, bool countForPlayerCount = false, bool shouldBeDrawn = false)
        {
            Player player = GetPlayerProxy(entity);
            if(player == null) return;

            ProxyPlayerModPlayer modPlayer = player.GetModPlayer<ProxyPlayerModPlayer>();
            modPlayer.shouldBeIgnoredByNPCs = !targetable;
            modPlayer.shouldCountForPlayerCount = countForPlayerCount;
            modPlayer.shouldBeDrawn = shouldBeDrawn;
        }

        private static void Sync(Player player, Entity entity)
        {

            bool dead = entity switch
            {
                NPC npc => npc.life <= 0,
                Projectile projectile => projectile.timeLeft <= 0,
                _ => false
            };
            ProxyPlayerModPlayer modPlayer = player.GetModPlayer<ProxyPlayerModPlayer>();
            player.Center = entity.Center;
            player.velocity = entity.velocity;
            player.direction = entity.direction;
            player.active = entity.active;
            player.dead = dead;
            modPlayer.isFakePlayer = true;
        }

        internal static void GarbageCollector()
        {
            if (_players.Count == 0) return;
            foreach (Player player in Main.player)
            {
                if (player == null || !player.active) continue;
                if (!player.IsProxyPlayer()) continue;

                Entity entity = GetEntityByPlayer(player.whoAmI);
                if (entity == null) continue;


                if (entity is NPC npc)
                {
                    bool npcInIndex = Main.npc.IndexInRange(entity.whoAmI);
                    NPC actualNPC = npcInIndex ? Main.npc[entity.whoAmI] : null;
                    bool isSameNPC = ReferenceEquals(entity, actualNPC);

                    if (npcInIndex && isSameNPC && npc.life > 0) continue;
                }
                else if (entity is Projectile projectile)
                {
                    bool projectileInIndex = Main.projectile.IndexInRange(entity.whoAmI);
                    Projectile actualProj = projectileInIndex ? Main.projectile[entity.whoAmI] : null;
                    bool isSameProj = ReferenceEquals(entity, actualProj);

                    if (projectileInIndex && isSameProj && projectile.timeLeft > 0) continue;
                }
                player.Reset();
            }
        }



        private static void Reset(this Player player)
        {
            int whoAmI = player.whoAmI;
            Entity entity = GetEntityByPlayer(whoAmI);

            Main.player[whoAmI] = new Player()
            {
                whoAmI = whoAmI,
                active = false
            };

            _playersOwners.Remove(player.whoAmI);
            if (entity != null) _players.Remove(entity);
        }

        internal static void ClearDictionaries()
        {
            foreach (int playerWhoAmI in _playersOwners.Keys)
            {
                if (Main.player.IndexInRange(playerWhoAmI) && Main.player[playerWhoAmI] != null)
                {
                    Main.player[playerWhoAmI].active = false;
                }
            }
            _players.Clear();
            _playersOwners.Clear();
        }

        private static Entity GetEntityByPlayer(int whoAmI) => _playersOwners.GetValueOrDefault(whoAmI);
    }
}

