using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using static PlayerProxyLib.Common.ProxyUtils;

namespace PlayerProxyLib.Common
{
    public static class ProxyPlayers
    {
        private const ulong RequestInterval = 60;
        private const ulong PendingLifetime = 600;
        private const ulong GarbageCollectInterval = 30;

        private readonly record struct ProxyKey(
            ProxyEntityType Type, int Index, int EntityType, int Owner, int Identity);

        private readonly record struct EntityDescriptor(
            ProxyEntityType Type, int Index, int EntityType, int Owner, int Identity)
        {
            // Projectile.whoAmI may differ across peers.
            public ProxyKey Key => Type == ProxyEntityType.Projectile
                ? new(Type, -1, EntityType, Owner, Identity)
                : new(Type, Index, EntityType, -1, -1);
        }

        private sealed class ProxyRecord
        {
            public required Entity Entity;
            public required EntityDescriptor Descriptor;
            public required Player Player;
            public required int Slot;
            public required uint Generation;
        }

        private sealed class PendingCreation
        {
            public required EntityDescriptor Descriptor;
            public required int Slot;
            public required uint Generation;
            public required byte Options;
            public required ulong ReceivedAt;
        }

        private static readonly Dictionary<Entity, ProxyRecord> _players = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<int, ProxyRecord> _playersOwners = [];
        private static readonly Dictionary<ProxyKey, ulong> _lastRequestAt = [];
        private static readonly Dictionary<ProxyKey, PendingCreation> _pending = [];
        private static readonly Dictionary<ProxyKey, uint> _latestGeneration = [];
        private static readonly Dictionary<ProxyKey, uint> _destroyedGeneration = [];
        private static readonly Dictionary<ProxyKey, ulong> _generationSeenAt = [];
        private static ulong _ticks;
        private static uint _nextGeneration;
        private static ulong _lastSnapshotRequestAt;
        private static bool _snapshotReceived;

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

        private static bool IsEntityValid(Entity entity) => entity switch
        {
            NPC npc => npc.active && npc.life > 0 && Main.npc.IndexInRange(npc.whoAmI)
                && ReferenceEquals(Main.npc[npc.whoAmI], npc),
            Projectile projectile => projectile.active && projectile.timeLeft > 0
                && Main.projectile.IndexInRange(projectile.whoAmI)
                && ReferenceEquals(Main.projectile[projectile.whoAmI], projectile),
            _ => false
        };

        private static bool MatchesProjectile(Projectile projectile, EntityDescriptor descriptor) =>
            projectile != null && projectile.type == descriptor.EntityType
            && projectile.owner == descriptor.Owner && projectile.identity == descriptor.Identity
            && IsEntityValid(projectile);

        private static Entity ResolveEntity(EntityDescriptor descriptor)
        {
            if (descriptor.Type == ProxyEntityType.NPC)
            {
                if (!Main.npc.IndexInRange(descriptor.Index))
                    return null;
                NPC npc = Main.npc[descriptor.Index];
                return npc != null && npc.type == descriptor.EntityType && IsEntityValid(npc) ? npc : null;
            }

            if (descriptor.Type != ProxyEntityType.Projectile)
                return null;

            if (Main.projectile.IndexInRange(descriptor.Index))
            {
                Projectile atIndex = Main.projectile[descriptor.Index];
                if (MatchesProjectile(atIndex, descriptor))
                    return atIndex;
            }

            foreach (Projectile projectile in Main.projectile)
                if (MatchesProjectile(projectile, descriptor))
                    return projectile;
            return null;
        }

        private static ProxyRecord TryCreateProxy(
            Entity entity, EntityDescriptor descriptor, int slot, uint generation, byte options)
        {
            // The final Main.player element is Terraria's sentinel.
            if (slot < 0 || slot >= Main.maxPlayers || slot >= Main.player.Length - 1)
                return null;

            if (_players.TryGetValue(entity, out ProxyRecord existing)
                && existing.Generation == generation && existing.Slot == slot
                && ReferenceEquals(Main.player[slot], existing.Player))
            {
                ApplyOptions(existing.Player, options);
                return existing; // Duplicate packet: retain inventory, buffs, and flags.
            }

            if (_players.TryGetValue(entity, out existing) && existing.Generation >= generation)
                return null;
            if (_playersOwners.TryGetValue(slot, out ProxyRecord previous)
                && previous.Generation >= generation)
                return null;

            if (existing != null)
                ReleaseRecord(existing, false);
            if (previous != null)
                ReleaseRecord(previous, false);

            // A real player always wins a slot collision.
            if (Main.player[slot]?.active == true)
                return null;

            Player player = new() { whoAmI = slot, active = true };
            Main.player[slot] = player;
            ProxyPlayerModPlayer state = player.GetModPlayer<ProxyPlayerModPlayer>();
            state.owner = entity;
            state.isFakePlayer = true;
            ApplyOptions(player, options);

            ProxyRecord record = new()
            {
                Entity = entity,
                Descriptor = descriptor,
                Player = player,
                Slot = slot,
                Generation = generation
            };
            _players[entity] = record;
            _playersOwners[slot] = record;
            return record;
        }

        private static bool IsRecordValid(ProxyRecord record) =>
            record != null && IsEntityValid(record.Entity)
            && Main.player.IndexInRange(record.Slot)
            && ReferenceEquals(Main.player[record.Slot], record.Player)
            // Netplay deactivates slots without a socket. Identity and owner lifetime
            // determine validity; losing active must not discard inventory or projectile counts.
            && TryDescribe(record.Entity, out EntityDescriptor current)
            && current.Key == record.Descriptor.Key;

        private static void RequestProxy(EntityDescriptor descriptor)
        {
            ProxyKey key = descriptor.Key;
            if (_pending.ContainsKey(key))
                return;
            if (_lastRequestAt.TryGetValue(key, out ulong last) && _ticks - last < RequestInterval)
                return;

            _lastRequestAt[key] = _ticks;
            ModPacket packet = ModContent.GetInstance<PlayerProxyLib>().GetPacket();
            packet.Write((byte)MessageType.RequestProxy);
            WriteDescriptor(packet, descriptor);
            packet.Send();
        }

        private static void WriteDescriptor(BinaryWriter writer, EntityDescriptor descriptor)
        {
            writer.Write((byte)descriptor.Type);
            writer.Write((short)descriptor.Index);
            writer.Write(descriptor.EntityType);
            writer.Write((short)descriptor.Owner);
            writer.Write(descriptor.Identity);
        }

        private static EntityDescriptor ReadDescriptor(BinaryReader reader) =>
            new((ProxyEntityType)reader.ReadByte(), reader.ReadInt16(), reader.ReadInt32(),
                reader.ReadInt16(), reader.ReadInt32());

        private static byte GetOptions(Player player)
        {
            ProxyPlayerModPlayer state = player.GetModPlayer<ProxyPlayerModPlayer>();
            byte options = 0;
            if (state.shouldBeDrawn) options |= 1;
            if (state.shouldBeIgnoredByNPCs) options |= 2;
            if (state.shouldCountForPlayerCount) options |= 4;
            return options;
        }

        private static void ApplyOptions(Player player, byte options)
        {
            ProxyPlayerModPlayer state = player.GetModPlayer<ProxyPlayerModPlayer>();
            state.shouldBeDrawn = (options & 1) != 0;
            state.shouldBeIgnoredByNPCs = (options & 2) != 0;
            state.shouldCountForPlayerCount = (options & 4) != 0;
        }

        private static void SendProxyCreation(ProxyRecord record, int toClient = -1)
        {
            if (Main.netMode != NetmodeID.Server)
                return;
            // Preserve the user's existing one-line GetPacket change.
            ModPacket packet = ModContent.GetInstance<PlayerProxyLib>().GetPacket();
            packet.Write((byte)MessageType.CreateProxy);
            WriteDescriptor(packet, record.Descriptor);
            packet.Write((ushort)record.Slot);
            packet.Write(record.Generation);
            packet.Write(GetOptions(record.Player));
            packet.Send(toClient);
        }

        private static void SendProxyOptions(ProxyRecord record)
        {
            if (Main.netMode != NetmodeID.Server)
                return;
            ModPacket packet = ModContent.GetInstance<PlayerProxyLib>().GetPacket();
            packet.Write((byte)MessageType.ConfigureProxy);
            WriteDescriptor(packet, record.Descriptor);
            packet.Write((ushort)record.Slot);
            packet.Write(record.Generation);
            packet.Write(GetOptions(record.Player));
            packet.Send();
        }

        private static void SendProxyDestroy(ProxyRecord record)
        {
            if (Main.netMode != NetmodeID.Server)
                return;
            ModPacket packet = ModContent.GetInstance<PlayerProxyLib>().GetPacket();
            packet.Write((byte)MessageType.DestroyProxy);
            WriteDescriptor(packet, record.Descriptor);
            packet.Write((ushort)record.Slot);
            packet.Write(record.Generation);
            packet.Send();
        }

        internal static void ReceiveProxyCreation(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;
            EntityDescriptor descriptor = ReadDescriptor(reader);
            int slot = reader.ReadUInt16();
            uint generation = reader.ReadUInt32();
            byte options = reader.ReadByte();
            ProxyKey key = descriptor.Key;
            if (_destroyedGeneration.TryGetValue(key, out uint destroyed) && generation <= destroyed)
                return;
            if (_latestGeneration.TryGetValue(key, out uint latest) && generation < latest)
                return;
            _latestGeneration[key] = generation;
            _generationSeenAt[key] = _ticks;
            _lastRequestAt.Remove(key);

            Entity entity = ResolveEntity(descriptor);
            ProxyRecord created = entity == null ? null : TryCreateProxy(entity, descriptor, slot, generation, options);
            if (created != null)
            {
                Sync(created.Player, entity);
                _pending.Remove(key);
                return;
            }

            // Entity replication may arrive after this packet.
            _pending[key] = new PendingCreation
            {
                Descriptor = descriptor,
                Slot = slot,
                Generation = generation,
                Options = options,
                ReceivedAt = _ticks
            };
        }

        internal static void ReceiveProxyRequest(BinaryReader reader, int fromClient)
        {
            if (Main.netMode != NetmodeID.Server)
                return;
            EntityDescriptor descriptor = ReadDescriptor(reader);
            if (fromClient < 0 || fromClient >= Main.maxPlayers
                || Main.player[fromClient]?.active != true)
                return;
            Entity entity = ResolveEntity(descriptor);
            if (entity != null && _players.TryGetValue(entity, out ProxyRecord record)
                && IsRecordValid(record))
                SendProxyCreation(record, fromClient);
            // A client request never creates a server proxy.
        }

        internal static void ReceiveSnapshotRequest(int fromClient)
        {
            if (Main.netMode != NetmodeID.Server || fromClient < 0
                || fromClient >= Main.maxPlayers || Main.player[fromClient]?.active != true)
                return;
            foreach (ProxyRecord record in _playersOwners.Values.ToArray())
                if (IsRecordValid(record))
                    SendProxyCreation(record, fromClient);
            ModPacket packet = ModContent.GetInstance<PlayerProxyLib>().GetPacket();
            packet.Write((byte)MessageType.SnapshotComplete);
            packet.Send(fromClient);
        }

        internal static void ReceiveSnapshotComplete()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                _snapshotReceived = true;
        }

        internal static void ReceiveProxyOptions(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;
            EntityDescriptor descriptor = ReadDescriptor(reader);
            int slot = reader.ReadUInt16();
            uint generation = reader.ReadUInt32();
            byte options = reader.ReadByte();
            if (_playersOwners.TryGetValue(slot, out ProxyRecord record)
                && record.Generation == generation && record.Descriptor.Key == descriptor.Key)
                ApplyOptions(record.Player, options);
            else if (_pending.TryGetValue(descriptor.Key, out PendingCreation pending)
                && pending.Generation == generation && pending.Slot == slot)
                pending.Options = options;
        }

        internal static void ReceiveProxyDestroy(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;
            EntityDescriptor descriptor = ReadDescriptor(reader);
            int slot = reader.ReadUInt16();
            uint generation = reader.ReadUInt32();
            ProxyKey key = descriptor.Key;
            if (!_destroyedGeneration.TryGetValue(key, out uint previous) || generation > previous)
                _destroyedGeneration[key] = generation;
            _generationSeenAt[key] = _ticks;
            if (_pending.TryGetValue(key, out PendingCreation pending)
                && pending.Generation <= generation)
                _pending.Remove(key);
            if (_playersOwners.TryGetValue(slot, out ProxyRecord record)
                && record.Generation == generation && record.Descriptor.Key == key)
                ReleaseRecord(record, false);
        }

        public static void UpdatePlayerProxy(this Entity entity) => GetPlayerProxy(entity);

        public static void DisposePlayerProxy(this Entity entity)
        {
            if (entity != null && _players.TryGetValue(entity, out ProxyRecord record))
                ReleaseRecord(record, Main.netMode == NetmodeID.Server);
        }

        public static bool IsProxyPlayer(this Player player) =>
            player?.GetModPlayer<ProxyPlayerModPlayer>().isFakePlayer ?? false;

        public static void ConfigureProxyPlayer(this Entity entity, bool targetable = false,
            bool countForPlayerCount = false, bool shouldBeDrawn = false)
        {
            Player player = GetPlayerProxy(entity);
            if (player == null)
                return;
            byte previous = GetOptions(player);
            ProxyPlayerModPlayer state = player.GetModPlayer<ProxyPlayerModPlayer>();
            state.shouldBeIgnoredByNPCs = !targetable;
            state.shouldCountForPlayerCount = countForPlayerCount;
            state.shouldBeDrawn = shouldBeDrawn;
            if (Main.netMode == NetmodeID.Server && previous != GetOptions(player)
                && _players.TryGetValue(entity, out ProxyRecord record))
                SendProxyOptions(record);
        }

        private static void Sync(Player player, Entity entity)
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

        private static void ReleaseRecord(ProxyRecord record, bool notifyClients)
        {
            if (notifyClients)
                SendProxyDestroy(record);
            if (_players.TryGetValue(record.Entity, out ProxyRecord byEntity)
                && ReferenceEquals(byEntity, record))
                _players.Remove(record.Entity);
            if (_playersOwners.TryGetValue(record.Slot, out ProxyRecord bySlot)
                && ReferenceEquals(bySlot, record))
                _playersOwners.Remove(record.Slot);
            record.Player.active = false;
            record.Player.dead = true;
            // Do not clear a real player or newer proxy that replaced this object.
            if (Main.player.IndexInRange(record.Slot)
                && ReferenceEquals(Main.player[record.Slot], record.Player))
                Main.player[record.Slot] = new Player { whoAmI = record.Slot, active = false };
        }

        // Netplay.UpdateConnectedClients clears active for slots without a real client.
        // Restore proxies before Player.Update rebuilds ownedProjectileCounts and before
        // NPC AI reads their state. Keep the existing Player and its owner slot.
        internal static void PreparePlayers()
        {
            foreach (ProxyRecord record in _playersOwners.Values.ToArray())
            {
                if (IsRecordValid(record))
                    record.Player.active = true;
                else
                    ReleaseRecord(record, Main.netMode == NetmodeID.Server);
            }
        }

        internal static bool IsSlotReserved(int slot) => _playersOwners.ContainsKey(slot);

        internal static void Tick()
        {
            _ticks++;
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                if (!_snapshotReceived
                    && (_lastSnapshotRequestAt == 0 || _ticks - _lastSnapshotRequestAt >= PendingLifetime)
                    && Main.myPlayer >= 0 && Main.myPlayer < Main.maxPlayers
                    && Main.player[Main.myPlayer]?.active == true)
                {
                    ModPacket packet = ModContent.GetInstance<PlayerProxyLib>().GetPacket();
                    packet.Write((byte)MessageType.RequestSnapshot);
                    packet.Send();
                    _lastSnapshotRequestAt = _ticks;
                }
                foreach (KeyValuePair<ProxyKey, PendingCreation> pair in _pending.ToArray())
                {
                    PendingCreation pending = pair.Value;
                    if (_ticks - pending.ReceivedAt > PendingLifetime)
                    {
                        _pending.Remove(pair.Key);
                        continue;
                    }
                    Entity entity = ResolveEntity(pending.Descriptor);
                    if (entity == null)
                        continue;
                    ProxyRecord created = TryCreateProxy(entity, pending.Descriptor, pending.Slot,
                        pending.Generation, pending.Options);
                    if (created != null)
                    {
                        Sync(created.Player, entity);
                        _pending.Remove(pair.Key);
                    }
                }
                foreach (KeyValuePair<ProxyKey, ulong> pair in _lastRequestAt.ToArray())
                    if (_ticks - pair.Value > PendingLifetime)
                        _lastRequestAt.Remove(pair.Key);
                if (_ticks % PendingLifetime == 0)
                {
                    foreach (KeyValuePair<ProxyKey, ulong> pair in _generationSeenAt.ToArray())
                    {
                        if (_ticks - pair.Value <= PendingLifetime * 6
                            || _pending.ContainsKey(pair.Key)
                            || _playersOwners.Values.Any(record => record.Descriptor.Key == pair.Key))
                            continue;
                        _generationSeenAt.Remove(pair.Key);
                        _latestGeneration.Remove(pair.Key);
                        _destroyedGeneration.Remove(pair.Key);
                    }
                }
            }
            if (_ticks % GarbageCollectInterval == 0)
                GarbageCollector();
        }

        internal static void GarbageCollector()
        {
            foreach (ProxyRecord record in _playersOwners.Values.ToArray())
                if (!IsRecordValid(record))
                    ReleaseRecord(record, Main.netMode == NetmodeID.Server);
        }

        internal static void ClearDictionaries()
        {
            foreach (ProxyRecord record in _playersOwners.Values.ToArray())
                ReleaseRecord(record, false);
            _players.Clear();
            _playersOwners.Clear();
            _lastRequestAt.Clear();
            _pending.Clear();
            _latestGeneration.Clear();
            _destroyedGeneration.Clear();
            _generationSeenAt.Clear();
            _ticks = 0;
            _nextGeneration = 0;
            _lastSnapshotRequestAt = 0;
            _snapshotReceived = false;
        }
    }
}
