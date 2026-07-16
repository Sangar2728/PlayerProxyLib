# PlayerProxyLib

PlayerProxyLib allows NPCs and Projectiles to use Terraria APIs that require a `Player` by providing a entity life time persistent Player proxies.
Instead of reimplementing player-only mechanics, simply obtain a proxy and use the existing Terraria API.

## What is this?

Some Terraria APIs only work with Player instances. PlayerProxyLib creates temporary Player proxies for NPCs and Projectiles, 
allowing mods to reuse vanilla player-only mechanics without reimplementing them.

## Installation

Add PlayerProxyLib as a dependency to your mod.

## Usage

```csharp
public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
{
    Player proxyPlayer = NPC.GetPlayerProxy(sync: false); // Gets or creates the proxy
    NPC.ConfigureProxyPlayer(shouldBeDrawn:true);
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
Player GetPlayerProxy(bool sync = true); 
// Set sync to false if you intend to update the proxy manually using UpdatePlayerProxy() or modify its synchronized properties before synchronization occurs.

void UpdatePlayerProxy();

void DisposePlayerProxy();

bool IsProxyPlayer();

void ConfigureProxyPlayer(this Entity entity, bool targetable = false, bool countForPlayerCount = false, bool shouldBeDrawn = false)
//Set targetable to true if you want that proxy can be focused by NPCs.
//Set countForPlayerCount to true if you want that proxy count toward Terraria's player count
//Set shouldBeDrawn to true if you want that proxy will be drawn by Terraria.
```

## Lifetime

- A proxy is permanently associated with a single NPC or Projectile.
- A proxy exists only while its owner is valid.
- Once disposed, the proxy must not be used again.
- Calling GetPlayerProxy() after disposal creates a new proxy.
- Proxies are not persistent across worlds or game sessions.

## Sync

The following properties are synchronized:

- Center
- velocity
- direction
- active
- dead

## Notes

- Proxies are persistent.
- Proxies are automatically disposed when their owner becomes invalid.
- Additional state stored in a proxy is managed by the consumer.
- Supports `NPC` and `Projectile`.
- Calling `GetPlayerProxy()` synchronizes the proxy by default.
## License

MIT