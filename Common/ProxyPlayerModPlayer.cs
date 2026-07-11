using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace PlayerProxyLib.Common
{
    public class FakePlayerModPlayer : ModPlayer
    {
        public bool shouldBeInvisible;
        public bool isFakePlayer;
        public override void HideDrawLayers(PlayerDrawSet drawInfo)
        {
            if (!shouldBeInvisible) return;
            PlayerDrawLayers.Head.Hide();
            PlayerDrawLayers.Torso.Hide();
            PlayerDrawLayers.Leggings.Hide();
            PlayerDrawLayers.ArmOverItem.Hide();
            PlayerDrawLayers.Skin.Hide();
        }
    }
}
