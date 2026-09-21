using Mono.Cecil.Cil;
using MonoMod.Cil;
using PlayerProxyLib.Common;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using static PlayerProxyLib.Common.ProxyUtils;

namespace PlayerProxyLib
{
    public class PlayerProxyLib : Mod
    {
        public override void Load()
        {
            IL_NPC.TargetClosest += IL_TargetClosest;
            IL_NPC.TargetClosest_WOF += IL_TargetClosest;
            On_NPC.GetActivePlayerCount += ON_GetActivePlayerCount;
            base.Load();
        }


        public override void Unload()
        {
            IL_NPC.TargetClosest -= IL_TargetClosest;
            IL_NPC.TargetClosest_WOF -= IL_TargetClosest;
            On_NPC.GetActivePlayerCount -= ON_GetActivePlayerCount;
        }
        private void IL_TargetClosest(ILContext il)
        {
            ILCursor c = new(il);

            int loopIndex = -1;
            ILLabel continueLabel = null;

            if (!c.TryGotoNext(
                MoveType.After,
                i => i.MatchLdloc(out loopIndex),
                i => i.MatchLdelemRef(),
                i => i.MatchLdfld<Player>(nameof(Player.ghost)),
                i => i.MatchBrtrue(out continueLabel)))
            {
                Logger.Warn("PlayerProxyLib: failed to patch NPC.TargetClosest.");
                return;
            }

            // Push:
            // arg0 = this (NPC)
            // local = player index
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc_S, (byte)loopIndex);

            c.EmitDelegate(static (NPC npc, int index) =>
            {
                var modPlayer = Main.player[index].GetModPlayer<ProxyPlayerModPlayer>();

                if (!modPlayer.isFakePlayer)
                    return false;

                // Never target your own proxy.
                if (ReferenceEquals(modPlayer.owner, npc))
                    return true;

                // Optionally ignore proxies globally.
                return modPlayer.shouldBeIgnoredByNPCs;
            });

            c.Emit(OpCodes.Brtrue_S, continueLabel);
        }
        private int ON_GetActivePlayerCount(On_NPC.orig_GetActivePlayerCount orig)
        {
            int count = 0;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player player = Main.player[i];
                if (player == null || !player.active) continue;
                ProxyPlayerModPlayer modPlayer = player.GetModPlayer<ProxyPlayerModPlayer>();
                if(modPlayer == null) continue;
                if (modPlayer.isFakePlayer && !modPlayer.shouldCountForPlayerCount) continue;
                count++;
            }
            return count > 0? count : 1;
        }

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            MessageType messageType = (MessageType)reader.ReadByte();

            switch (messageType)
            {
                case MessageType.RequestProxy:
                    ProxyPlayers.ReceiveProxyRequest(reader, whoAmI);
                    break;

                case MessageType.CreateProxy:
                    ProxyPlayers.ReceiveProxyCreation(reader);
                    break;
            }
        }

    }
}
