using Microsoft.Xna.Framework;
using PlayerProxyLib.Common.ProxyPlayer;
using Terraria;

namespace PlayerProxyLib.Common.Structs
{
    internal ref struct ProxyContextScope
    {
        private readonly bool active;

        private readonly int previousPlayer;
        private readonly int previousMouseX;
        private readonly int previousMouseY;

        // Players whose hostile flag we temporarily changed.
        private ulong changedHostile0;
        private ulong changedHostile1;
        private ulong changedHostile2;
        private ulong changedHostile3;

        public ProxyContextScope(int playerIndex, bool virtualizePvpTargets = false)
        {
            previousPlayer = Main.myPlayer;
            previousMouseX = Main.mouseX;
            previousMouseY = Main.mouseY;

            changedHostile0 = 0;
            changedHostile1 = 0;
            changedHostile2 = 0;
            changedHostile3 = 0;

            active =
                playerIndex >= 0 &&
                playerIndex < Main.maxPlayers &&
                Main.player[playerIndex]?.IsProxyPlayer() == true;

            if (!active)
                return;

            Player proxy = Main.player[playerIndex];

            Main.myPlayer = playerIndex;

            Vector2 mouseWorld = proxy.GetMouseWorld();
            Vector2 mouseScreen = mouseWorld - Main.screenPosition;

            Main.mouseX = (int)mouseScreen.X;
            Main.mouseY = (int)mouseScreen.Y;

            // Enemy proxies need real players to temporarily count as PvP targets
            // so vanilla melee ItemCheck doesn't reject them.
            if (virtualizePvpTargets && proxy.hostile)
            {
                for (int i = 0; i < Main.maxPlayers; i++)
                {
                    Player target = Main.player[i];

                    if (target == null ||
                        !target.active ||
                        target.IsProxyPlayer() ||
                        target.hostile)
                    {
                        continue;
                    }

                    MarkHostileChanged(i);
                    target.hostile = true;
                }
            }
        }

        private void MarkHostileChanged(int index)
        {
            int bit = index & 63;
            ulong mask = 1UL << bit;

            switch (index >> 6)
            {
                case 0:
                    changedHostile0 |= mask;
                    break;

                case 1:
                    changedHostile1 |= mask;
                    break;

                case 2:
                    changedHostile2 |= mask;
                    break;

                case 3:
                    changedHostile3 |= mask;
                    break;
            }
        }

        private readonly bool WasHostileChanged(int index)
        {
            int bit = index & 63;
            ulong mask = 1UL << bit;

            return (index >> 6) switch
            {
                0 => (changedHostile0 & mask) != 0,
                1 => (changedHostile1 & mask) != 0,
                2 => (changedHostile2 & mask) != 0,
                3 => (changedHostile3 & mask) != 0,
                _ => false
            };
        }

        public void Dispose()
        {
            if (!active)
                return;

            // Restore only players that were false before entering the scope.
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                if (WasHostileChanged(i))
                    Main.player[i].hostile = false;
            }

            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.myPlayer = previousPlayer;
        }
    }
    
}
