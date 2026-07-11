using Mono.Cecil.Cil;
using MonoMod.Cil;
using PlayerProxyLib.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.ModLoader;

namespace PlayerProxyLib
{
    // Please read https://github.com/tModLoader/tModLoader/wiki/Basic-tModLoader-Modding-Guide#mod-skeleton-contents for more information about the various files in a mod.
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
        }
        private void IL_TargetClosest(ILContext il)
        {
            ILCursor c = new(il);

            // Buscar el patrón:
            // ldfld Player::ghost
            // brtrue.s ContinueLoop
            if (!c.TryGotoNext(
                MoveType.Before,
                i => i.MatchLdfld<Player>("ghost"),
                i => i.MatchBrtrue(out _)))
            {
                Logger.Warn("No se encontró la comprobación de ghost.");
                return;
            }

            // El cursor está antes del ldfld.
            // Avanzamos hasta el brtrue.
            c.Index++;

            // Guardamos el destino del continue.

            ILLabel continueLabel = (ILLabel)c.Next.Operand;

            // Buscar la llamada a TryTrackingTarget
            if (!c.TryGotoNext(
                MoveType.Before,
                i => i.MatchCall("Terraria.NPC", "TryTrackingTarget")))
            {
                Logger.Warn("No se encontró TryTrackingTarget.");
                return;
            }

            // Retroceder hasta el ldarg.0
            c.GotoPrev(i => i.MatchLdarg(0));
            // Guardar el punto donde insertaremos
            Instruction insertPoint = c.Next;

            // Volver a ese punto
            c.Goto(insertPoint);

            // Cargar i
            c.Emit(OpCodes.Ldloc_S, (byte)4);

            // bool isFakePlayer = ...
            c.EmitDelegate((int index) =>
            {
                return Main.player[index]
                    .GetModPlayer<FakePlayerModPlayer>()
                    .isFakePlayer;
            });  

            // if (isFakePlayer)
            //     continue;
           c.Emit(OpCodes.Brtrue_S, continueLabel);

        }
        private int ON_GetActivePlayerCount(On_NPC.orig_GetActivePlayerCount orig)
        {
            int count = 0;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player player = Main.player[i];
                if (player == null || !player.active) continue;
                if (player.GetModPlayer<FakePlayerModPlayer>().isFakePlayer) continue;
                count++;
            }
            return count > 0? count : 1;
        }

    }
}
