using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

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
        /// The proxy remains associated with the entity until it is disposed or the entity becomes invalid.
        /// Only the proxy identity and synchronized properties are managed by the library.
        /// Any additional state stored in the returned <see cref="Player"/> is the consumer's responsibility.
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

        public static Player GetPlayerProxy(this Entity entity, bool visible = false, bool sync = true)
        {
            if (entity == null || !entity.active) return null;
            if (entity is NPC npc && npc.life <= 0) return null;

            if (!_players.TryGetValue(entity, out int playerwhoAmI))
            {
                int index = ProxyUtils.GetAvailableWhoAmI();
                if (index <= -1) return null;

                _players[entity] = index;
                _playersOwners[index] = entity;

                Main.player[index] = new Player()
                {
                    whoAmI = index,
                    active = true
                };
                playerwhoAmI = index;
            }

            Player player = Main.player[playerwhoAmI];
            if(sync) Sync(player, entity, visible);

            return player;
        }
        /// <summary>
        /// Refresh the sync with the <see cref="Player"/> proxy associated with the specified <paramref name="entity"/>.
        /// </summary>
        public static void UpdatePlayerProxy(this Entity entity, bool visible = false) => GetPlayerProxy(entity, visible);


        public static void DisposePlayerProxy(this Entity entity)
        {
            if (!_players.TryGetValue(entity, out int playerWhoAmI)) return;
            Main.player[playerWhoAmI].Reset();
        }

        /// <summary>
        /// Disposes the <see cref="Player"/> proxy associated with the specified <paramref name="entity"/>.
        /// </summary>
        /// <remarks>
        /// Removes the association between the entity and its proxy, releases the underlying
        /// <see cref="Main.player"/> slot, and invalidates the current proxy instance.
        /// 
        /// Calling <see cref="GetPlayerProxy(Entity, bool, bool)"/> again for the same entity
        /// creates a new proxy.
        /// </remarks>
        public static bool IsProxyPlayer(this Player player) => player.GetModPlayer<ProxyPlayerModPlayer>().isFakePlayer;

        private static void Sync(Player player, Entity entity, bool visible)
        {

            bool dead = entity switch
            {
                NPC npc => npc.life <= 0,
                Projectile projectile => projectile.timeLeft <= 0,
                _ => true
            };
            ProxyPlayerModPlayer modPlayer = player.GetModPlayer<ProxyPlayerModPlayer>();
            player.position = entity.position;
            player.Center = entity.Center;
            player.velocity = entity.velocity;
            player.direction = entity.direction;
            player.active = entity.active;
            player.dead = dead;
            modPlayer.isFakePlayer = true;
            modPlayer.shouldBeInvisible = !visible;

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
                    Projectile actualNPC = projectileInIndex ? Main.projectile[entity.whoAmI] : null;
                    bool isSameNPC = ReferenceEquals(entity, actualNPC);

                    if (projectileInIndex && isSameNPC && projectile.timeLeft > 0) continue;
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

