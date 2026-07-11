using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;

namespace PlayerProxyLib.Common
{
    public static class ProxyPlayers
    {
        private static readonly Dictionary<NPC, int> _players = [];

        public static Player GetPlayerProxy(this NPC npc, bool visible = false)
        {
            if (!_players.TryGetValue(npc, out int playerwhoAmI))
            {
                int index = GetAvailableWhoAmI();
                if (index <= -1) return null;

                _players[npc] = index;
                Main.player[index] = new Player()
                {
                    whoAmI = index,
                    active = true
                };
                playerwhoAmI = index;
            }

            Player player = Main.player[playerwhoAmI];
            Sync(player, npc,visible);

            return player;
        }
        public static Player UpdatePlayerProxy(NPC npc, bool visible = false) => GetPlayerProxy(npc, visible);
        public static void DisposePlayerProxy(this NPC npc)
        {
            if (!_players.TryGetValue(npc, out int playerWhoAmI)) return;
            Main.player[playerWhoAmI].Reset();
            _players.Remove(npc);  
        }

        private static void Sync(Player player, NPC npc, bool visible)
        {
            player.position = npc.position;
            player.Center = npc.Center;
            player.velocity = npc.velocity;
            player.direction = npc.direction;
            player.active = npc.active;
            player.dead = npc.life <= 0;
            player.GetModPlayer<FakePlayerModPlayer>().isFakePlayer = true;
            if (!visible) player.GetModPlayer<FakePlayerModPlayer>().shouldBeInvisible = true;
            
        }

        internal static void GarbageCollector()
        {
            if (_players.Count == 0) return;
            foreach (Player player in Main.player)
            {
                if (player == null || !player.active) continue;
                if (!player.GetModPlayer<FakePlayerModPlayer>().isFakePlayer) continue;

                NPC npc = _players.FirstOrDefault(x => x.Value == player.whoAmI).Key;
                if (npc == null) continue;

                bool npcInIndex = Main.npc.IndexInRange(npc.whoAmI);

                NPC actualNPC = npcInIndex ? Main.npc[npc.whoAmI] : null;
                bool isSameNPC = ReferenceEquals(npc, actualNPC);

                if (npcInIndex && isSameNPC && npc.life > 0) continue;

                player.Reset();
            }
        }

        private static void Reset(this Player player)
        {
            int whoAmI = player.whoAmI;
            Main.player[whoAmI] = new Player()
            {
                whoAmI = whoAmI,
                active = false
            };
            NPC npc = _players.FirstOrDefault(x => x.Value == player.whoAmI).Key;
            _players.Remove(npc);
        }

        private static int GetAvailableWhoAmI()
        {
            int reservedSpaceForRealPlayers = Main.netMode == NetmodeID.SinglePlayer ? 0 : 5; 

            for (int i = Main.player.Length - 2; i >= reservedSpaceForRealPlayers; i--)
            {
                if (!Main.player[i].active) return i;   
            }
            return -1;
        }

    }
}

