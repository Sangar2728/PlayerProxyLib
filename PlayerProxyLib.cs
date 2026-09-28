using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using PlayerProxyLib.Common;
using PlayerProxyLib.Common.NetWorking;
using PlayerProxyLib.Common.ProxyPlayer;
using PlayerProxyLib.Common.Structs;
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
            On_Player.ItemCheck += Player_ItemCheck;
            On_Projectile.AI += Projectile_AI;
            base.Load();
        }


        public override void Unload()
        {
            IL_NPC.TargetClosest -= IL_TargetClosest;
            IL_NPC.TargetClosest_WOF -= IL_TargetClosest;
            On_NPC.GetActivePlayerCount -= ON_GetActivePlayerCount;
            On_Player.ItemCheck -= Player_ItemCheck;
            On_Projectile.AI -= Projectile_AI;
            base.Unload();
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
                if (modPlayer == null) continue;
                if (modPlayer.isFakePlayer && !modPlayer.shouldCountForPlayerCount) continue;
                count++;
            }
            return count > 0 ? count : 1;
        }

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            MessageType messageType = (MessageType)reader.ReadByte();

            switch (messageType)
            {
                case MessageType.RequestProxy:
                    ProxyPlayerManager.ReceiveProxyRequest(reader, whoAmI);
                    break;

                case MessageType.CreateProxy:
                    ProxyPlayerManager.ReceiveProxyCreation(reader);
                    break;
                case MessageType.DestroyProxy:
                    ProxyPlayerManager.ReceiveProxyDestroy(reader);
                    break;
                case MessageType.RequestSnapshot:
                    ProxyPlayerManager.ReceiveSnapshotRequest(whoAmI);
                    break;
                case MessageType.ConfigureProxy:
                    ProxyPlayerManager.ReceiveProxyOptions(reader);
                    break;
                case MessageType.SnapshotComplete:
                    ProxyPlayerManager.ReceiveSnapshotComplete();
                    break;
            }
        }

        private static void Player_ItemCheck(On_Player.orig_ItemCheck orig, Player self)
        {
            using var context = new ProxyContextScope(self.whoAmI, virtualizePvpTargets: true);
            orig(self);
        }

        private static void Projectile_AI(On_Projectile.orig_AI orig, Projectile self)
        {
            using var context = new ProxyContextScope(self.owner);

            orig(self);
        }

       
    }
}
