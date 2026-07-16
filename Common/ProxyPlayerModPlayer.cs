using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace PlayerProxyLib.Common
{
    public class ProxyPlayerModPlayer : ModPlayer
    {
        internal bool shouldBeDrawn;
        internal bool isFakePlayer;
        internal bool shouldBeIgnoredByNPCs;
        internal bool shouldCountForPlayerCount;
        internal Entity owner;

        public override void HideDrawLayers(PlayerDrawSet drawInfo)
        {
            if (!isFakePlayer) return;
            if (shouldBeDrawn) return;
            PlayerDrawLayers.Head.Hide();
            PlayerDrawLayers.Torso.Hide();
            PlayerDrawLayers.Leggings.Hide();
            PlayerDrawLayers.ArmOverItem.Hide();
            PlayerDrawLayers.Skin.Hide();
        }
    }
}
