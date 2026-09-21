using Terraria;
using Terraria.ID;

namespace PlayerProxyLib.Common
{
    public class ProxyUtils
    {
        internal static int GetAvailableWhoAmI()
        {
            //Scurity buffer for multiplayer
            // average ammount of player on servers is <= 5
            int reservedSpaceForRealPlayers = Main.netMode == NetmodeID.SinglePlayer ? 0 : 5;

            for (int i = Main.player.Length - 2; i >= reservedSpaceForRealPlayers; i--)
            {
                if (!Main.player[i].active) return i;
            }
            return -1;
        }

        internal enum MessageType : byte
        {
            CreateProxy,
            RequestProxy
        }

        internal enum ProxyEntityType : byte
        {
            NPC,
            Projectile
        }
    }
}
