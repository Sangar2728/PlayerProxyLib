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
            On_Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float += On_NewProjectile;
            base.Load();
        }


        public override void Unload()
        {
            IL_NPC.TargetClosest -= IL_TargetClosest;
            IL_NPC.TargetClosest_WOF -= IL_TargetClosest;
            On_NPC.GetActivePlayerCount -= ON_GetActivePlayerCount;
            On_Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float -= On_NewProjectile;
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
                case MessageType.DestroyProxy:
                    ProxyPlayers.ReceiveProxyDestroy(reader);
                    break;
                case MessageType.RequestSnapshot:
                    ProxyPlayers.ReceiveSnapshotRequest(whoAmI);
                    break;
                case MessageType.ConfigureProxy:
                    ProxyPlayers.ReceiveProxyOptions(reader);
                    break;
                case MessageType.SnapshotComplete:
                    ProxyPlayers.ReceiveSnapshotComplete();
                    break;
            }
        }

        private static int On_NewProjectile(On_Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float orig, Terraria.DataStructures.IEntitySource source, float x, float y, float speedX, float speedY, int type, int damage, float knockback, int owner, float ai0, float ai1, float ai2)
        {
            // Projectile normal: no tocar nada.
            if (owner < 0 ||  owner >= Main.maxPlayers || Main.player[owner] == null || !Main.player[owner].IsProxyPlayer() || owner == Main.myPlayer)
            {
                return orig(source, x, y, speedX, speedY, type, damage, knockback, owner, ai0, ai1, ai2);
            }

            int previousMyPlayer = Main.myPlayer;

            try
            {
                // Durante NewProjectile hacemos que la lógica vanilla
                // considere al proxy como el propietario local.
                Main.myPlayer = owner;

                return orig(source, x, y, speedX, speedY, type, damage, knockback, owner, ai0, ai1, ai2);
            }
            finally
            {
                Main.myPlayer = previousMyPlayer;
            }
        }
    }
}
