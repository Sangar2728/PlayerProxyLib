using Terraria;
using Terraria.ID;

namespace PlayerProxyLib.Common
{
    public class ProxyUtils
    {
        internal static int GetAvailableWhoAmI()
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
