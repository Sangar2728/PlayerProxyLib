using Terraria;
using Terraria.ID;

namespace PlayerProxyLib.Common
{
    public class ProxyUtils
    {
        internal static int GetAvailableWhoAmI()
        {
            // Keep the usual low real-player indices free. This is not a formal
            // reservation; TryCreateProxy still validates every slot before use.
            int reservedSpaceForRealPlayers = Main.netMode == NetmodeID.SinglePlayer ? 0 : 5;

            for (int i = System.Math.Min(Main.maxPlayers - 1, Main.player.Length - 2);
                i >= reservedSpaceForRealPlayers; i--)
            {
                if (Main.player[i]?.active != true && !ProxyPlayers.IsSlotReserved(i))
                    return i;
            }
            return -1;
        }

        internal enum MessageType : byte
        {
            CreateProxy,
            RequestProxy,
            DestroyProxy,
            RequestSnapshot,
            ConfigureProxy,
            SnapshotComplete
        }

        internal enum ProxyEntityType : byte
        {
            NPC,
            Projectile
        }
    }
}
