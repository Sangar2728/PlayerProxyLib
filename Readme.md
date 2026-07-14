# PlayerProxyLib

PlayerProxyLib allows NPCs and Projectiles to use Terraria APIs that require a `Player` by providing persistent Player proxies.
Instead of reimplementing player-only mechanics, simply obtain a proxy and use the existing Terraria API.

## Installation

Add PlayerProxyLib as a dependency to your mod.

## Usage

```csharp
Player proxyPlayer => NPC.GetPlayerProxy(); // Gets or creates the proxy
public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
{
    proxyPlayer.name = NPC.FullName;
    proxyPlayer.hasTitaniumStormBuff = true;
    proxyPlayer.AddBuff(BuffID.TitaniumStorm, 600);

    var p = Projectile.NewProjectile(Projectile.GetSource_None(), position, velocity, ProjectileID.TitaniumStormShard, damage, 0, proxyPlayer.whoAmI);
    Main.projectile[p].hostile = true;
    Main.projectile[p].friendly = false;
    Main.projectile[p].penetrate = 2;
    Main.projectile[p].timeLeft = 1000;
    base.OnHitPlayer(target, hurtInfo);
}
public override void AI()
{
    NPC.UpdatePlayerProxy(); // Keeps the proxy synchronized with the NPC.
    base.AI();
}
public override void OnKill()
{
    NPC.DisposePlayerProxy(); // Optional. Proxies are disposed automatically when the owner becomes invalid.
    base.OnKill();
}
```

## API

```csharp
Player GetPlayerProxy(bool visible = false, bool sync = true); 

void UpdatePlayerProxy(bool visible = false);

void DisposePlayerProxy();

bool IsProxyPlayer();
```

## Notes

- Proxies are persistent.
- Proxies are automatically disposed when their owner becomes invalid.
- Additional state stored in a proxy is managed by the consumer.
- Supports `NPC` and `Projectile`.
- Calling `GetPlayerProxy()` synchronizes the proxy by default.
## License

MIT