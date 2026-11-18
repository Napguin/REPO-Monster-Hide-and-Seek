<p align="center">
  <img src="pictures/logo/logo-1024.png" width="200" alt="R.E.P.O. Hide 'n Seek logo">
</p>

<h1 align="center">R.E.P.O. Hide 'n Seek</h1>

<p align="center">
  <b>Hide 'n Seek for R.E.P.O. – hunt as a real monster of the game, or in Classic mode with a weapon of choice.</b><br>
  <sub>by <b>Flyguy</b> (art &amp; animation) and <b>Napguin</b> (programming) · Thunderstore package: <code>HideAndSeekMonster</code></sub><br><br>
  <!-- Static badges: shields.io can't reach Thunderstore any more ("inaccessible"). Update the version on each release. -->
  <a href="https://thunderstore.io/c/repo/p/Napguin/HideAndSeekMonster/"><img src="https://img.shields.io/badge/Thunderstore-Download-2a9d8f" alt="Download on Thunderstore"></a>
  <a href="https://thunderstore.io/c/repo/p/Napguin/HideAndSeekMonster/"><img src="https://img.shields.io/badge/release-2.1.0-e76f51" alt="Latest release 2.1.0"></a>
  <a href="https://thunderstore.io/c/repo/p/Napguin/HideAndSeekMonster/"><img src="https://img.shields.io/badge/R.E.P.O.-BepInEx_mod-6c5ce7" alt="R.E.P.O. BepInEx mod"></a>
  <a href="https://github.com/Napguin/REPO-Monster-Hide-and-Seek/issues"><img src="https://img.shields.io/github/issues/Napguin/REPO-Monster-Hide-and-Seek?label=Issues" alt="GitHub issues"></a>
</p>

<p align="center">
  <a href="https://thunderstore.io/c/repo/p/Napguin/HideAndSeekMonster/"><b>⬇️ Download on Thunderstore</b></a> ·
  <a href="#installation">Installation</a> ·
  <a href="#how-to-play">How to play</a> ·
  <a href="#special-abilities">Specials</a> ·
  <a href="#configuration">Config</a>
</p>

---

## Installation

**With a mod manager (recommended):** install
[**Hide And Seek Monster** on Thunderstore](https://thunderstore.io/c/repo/p/Napguin/HideAndSeekMonster/) with r2modman or the Thunderstore Mod Manager –
BepInEx is installed automatically.

**Manually:**
1. Install [BepInEx](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) for R.E.P.O.
2. Download the latest zip from [`release/`](release/) and copy
   `BepInEx/plugins/HideAndSeekMonster/HideAndSeekMonster.dll` into `<REPO>/BepInEx/plugins/`.

A **Hide 'n Seek** game mode for R.E.P.O. with two modes:

- **Monster mode:** the hunter **becomes a real monster of the game** – e.g. a Huntsman, Robe, Trudge, … – each with
  its own special ability.
- **Classic mode:** the hunter stays a player and hunts with a **weapon of choice**.

Everyone else has a short time to hide. Then the hunter is released and hunts them down.

> **Every player in the lobby needs this mod installed.**

## How to play

1. Start a run and enter a level.
2. The **host** presses **F8** to open the Hide & Seek menu.
3. Pick the **seeker** (a player or Random) and the **monster** (a monster or Random).
4. Adjust times, attack range, speed and monster size if you like. Under the hunt time the menu recommends hide and hunt times for the current moon phase (maps get bigger every phase) – one click applies them.
5. Press **Start round**.

New to the mod? Open **How To Play** (the drawn rules) and **Monster Showcase** (every special with a drawn animation, its cooldown and which monsters have it) in the F8 menu.

### As the monster
| Control | Action |
|---|---|
| **Left mouse** | Attack – lunge forward, 40 damage to hiders in front of you (players have 100 HP) |
| **Right mouse** / **R** | Player scan – shows how many players are within 15 m (not where); those hiders get warned |
| **Middle mouse** / **F** | Special ability (see below) |
| **V** | Switch between third person (see your monster) and first person |

During the hiding phase the seeker can pick which monster they want to be.
The monster is faster, has infinite stamina and is invincible, but can't use the map. It can grab things, so doors and buttons work.
Running into hiders does nothing – the monster has to hurt them with its attack or special.

### Test mode (solo)
Enable **Test mode** in the F8 menu (or `Test / TestMode` in the config) and start a round alone.
You become the monster, training dummies stand around you as hiders, hiding time is only 3 s and there are no cooldowns.

| Key | Action |
|---|---|
| **F9** / **Shift+F9** | Next / previous monster model |
| **F10** | Spawn a dummy in front of you |
| **F11** | Toggle the debug overlay |

The overlay shows the monster's model info and whether your last attack hit (distance and angle).
Details are also written to `BepInEx/LogOutput.log`. Dummies only exist on the host's screen.

<a id="special-abilities"></a>
### Special abilities
Every monster has one special (**middle mouse** / **F**). The ability bar at the bottom shows the cooldowns (no cooldowns in test mode).

| Special | What it does | Cooldown | Monsters |
|---|---|---|---|
| Laser | 3 s beam, 50 damage per hit (every 0.5 s), walls block it; the Clown can't move while firing | 40 s | Clown |
| Shot | one long-range shot, 60 damage to the first hider in the line of fire | 15 s | Huntsman |
| Rapid Charge | 1.5x speed for 5 s with a visible aura, 20 damage every 0.5 s to hiders close by, no main attack meanwhile | 40 s | Reaper, Animal, Elsa, Bella |
| Flying Charge | attack form, fly for 6 s (jump = up, crouch = down), 10 damage every 0.5 s to hiders close by | 30 s | Apex Predator |
| Charge | stops for a moment, then dashes forward very fast (about 14 m, horizontal only) – 50 damage on contact | 30 s | Robe, Headgrab |
| Lunge | like Charge, but in the camera direction – also up/down and in the air – 50 damage on contact | 30 s | Headman, Chef |
| Tumble-Bash | rolls around fast for up to 5 s, steered with the camera (jump works, press again to stop), softly bouncing off walls, small enough to roll under tables; anyone hit tumbles, gets knocked away and takes 15 damage | 30 s | Tick |
| Radar Ping | position and distance of every hider within 30 m for 2 s; hiders are told whether they were detected | 120 s | Peeper, Loom |
| Flying Grab | invisible bubble (4 m) around the monster: hiders inside tumble and are stunned for 8 s so the monster can grab and carry them; no main attack meanwhile. The Oogly can always fly | 30 s | Oogly |
| Zero-G Grab | stun bubble (6 m) with zero gravity: hiders inside tumble, float and are stunned for 8 s so the monster can grab them; no main attack meanwhile | 30 s | Mentalist |
| Balloon Mines | places a balloon hovering in the air (5 balloons; with none free, the oldest hanging one is moved to you); when a hider gets close it explodes – 40 damage within 2.5 m | 3 s between balloons / moves; a popped one can be placed again after 40 s | Birthday Boy |
| Item Throw | grabs a nearby valuable (4 m); use again to throw it really fast – damage depends on speed and weight | 6 s (1 s after picking up) | Rugrat |
| Head bomb | throws its head – it bounces off walls and explodes after 4 s: explosion (50 damage, 4 m) and a uranium cloud (10 damage every 0.5 s for 5 s) | 40 s | Cleanup Crew |
| Puke | vomit stream forward for 2.5 s: 10 damage every 0.5 s and slows. The Spewer can always fly | 30 s | Spewer |
| Tornado | a small tornado wanders forward for 7 s, sucks in items, weapons and valuables and whirls them around, then flings everything away – flying items hurt hiders (20–90 damage). Hiders nearby are pulled in; within 2.5 m they're trapped, spun around and can't grab anything | 40 s | Bowtie |
| Lasso Grab | lasso in the camera direction (18 m): a hit hider is reeled in at a steady speed for 5 s (they can still move sideways); the monster is very slow meanwhile | 25 s | Snatcher |
| Launch | the closest hider within 6 m tumbles and gets launched in the camera direction (25 damage) | 30 s | Upscream |
| Impulse | hiders within 9 m tumble and get sucked towards the monster, then a shockwave blasts them away (40 damage) | 30 s | Trudge |
| Noxious Gas | spews 5 large gas clouds that linger for 10 s – hiders in the gas are tranquilized (very slow, sluggish camera) for 5 s – still in the gas after that means another 5 s | 40 s | Heart Hugger |
| Swarm | summons 5 real Gnomes / Bangers of the game that hunt hiders on their own for 30 s (they have to spot them first, and they ignore the monster) | 70 s | Gnome, Banger |
| Gamble | a random special – one of the specials the other monsters have right now (laser, tornado, lasso, gas, grabs, …) | 30 s | Gambit |

Hiders see which special the monster has while they hide and get a warning when it is used.

### Scores
Points add up over all rounds. They are shown after each round and in the F8 menu (the host can reset them).
- **Monster:** 1 point for every hider that dies – no matter what killed them (monster, traps, map hazards ...).
- **Hiders:** 1st to die = 0 points, 2nd = 1, 3rd = 2 and so on. Everyone who survives gets as many points as there
  were hiders. Example with 3 hiders: Anna dies first (0), Ben second (1), Carl survives (3).

### Phases
| Phase | What happens |
|---|---|
| **Item time** | Everyone stands still and picks their free item (**I**). Ends after 60 s or when everyone is done (**J** = skip). |
| **Hiding** | The monster is frozen and its screen is black. Hiders run and hide. |
| **Hunting** | The monster is released and hunts. |
| **End** | The monster wins when every hider is dead. The hiders win if at least one survives the hunt timer. |

Normal enemies are despawned while a round is running (can be turned off).

### Items
Every player picks 1 free item at round start (item time, **I** key), following the item gallery in How To Play:
- **Hiders:** tranq and pulse pistol, stun grenade, pulse grenade and mine, leaf blower, plus the shared items.
- **Monster:** every item except guns, melee weapons and the defibrillator.
- **Classic hunter:** a start weapon of choice, then every item except the defibrillator.
- **Shared:** duck bucket, walkie talkie, bridge, POCKET C.A.R.T., med-kit, defibrillator (hiders), orbs, drones, staffs, upgrades.

Weapons don't run out of energy during a round (the hiders' pistols still have only 2 shots per round). Owned items
come back to their owner at the next round start – unless they were used up.

### Classic mode
Switch it on in the F8 menu (`Round / Mode`). The hunter stays a normal player: while the others hide they press **I**
and pick a **melee weapon or gun of choice** (it appears in front of them). Melee weapons never break, guns can't be
recharged. The hunter may then press **I** again for a second item. Hiders can't grab melee weapons, may wrestle
guns but only fire the tranq / pulse pistol, and can't put the hunter's weapons into their inventory.
Items handed out by the mod (start weapon, free items) go back to their owner at the start of the next round –
weapons owned by a hider stay in the truck for that round and can't be used.

### After the round
Surviving hiders are shown to everyone, all dead players are revived and healed right where they are, and everyone
gets **60 seconds to look at the hiding spots** (the monster keeps its monster form). Press **J** to vote for skipping –
a majority skips it. Then everyone is teleported into the truck for a **15 s vote**: volunteer to be the next monster
(a random volunteer gets it) and vote whether to stay on this map or switch to one of three other maps. After that the
**next round starts by itself** (after a new map has loaded). At every round start all weapons, equipment and items
with a battery are recharged.
Can be turned off in the F8 menu.

## Configuration
`BepInEx/config/HideAndSeekMonster.cfg` (created after the first start).
The round settings of the **host** are used for everyone.

| Setting | Default | Description |
|---|---|---|
| General / MenuKey | F8 | Key to open the menu |
| General / DebugMenuKey | F7 | Debug menu (host only): revive, heal, teleport, spawn enemies / valuables / items / dummies, skip hiding, reset cooldowns |
| General / Language | English | English or Deutsch |
| Round / HideTime | 30 | Seconds to hide |
| Round / RoundTime | 300 | Seconds the monster has to hunt |
| Round / DisableEnemies | true | Despawn normal enemies during a round |
| Round / MapLevel | 0 | Level (map size) for new maps from the menu / map vote, 0 = keep current |
| Round / Mode | Monster | Monster: hunter becomes a monster. Classic: hunter stays a player with a melee weapon of choice |
| Round / FreeItem | true | Everyone – hiders and the monster – may take 1 free item at round start (I key); in Classic mode the hunter picks the start weapon first |
| Round / BanWeapons | true | Monster mode: guns, melee weapons and cart cannons can't be grabbed (tranq / pulse pistol allowed, but only 2 shots per round and no recharging) |
| Round / AfterRound | true | Reveal survivors, revive + heal, truck, vote for seeker and map |
| Round / PickSeconds | 60 | Round start: time to pick your item (I) before the hiding time starts – ends early when everyone is done (J = skip) |
| Round / InspectSeconds | 60 | After a round: time to look at the hiding spots (majority vote with J skips it), 0 = none |
| Round / AutoNextRound | true | The next round starts by itself after the 15 s vote in the truck |
| Round / SkipKey | J | Vote to skip the time to look at the hiding spots |
| Seeker / AttackRange | 2.8 | Reach of the attack in metres |
| Seeker / AttackCooldown | 1.2 | Seconds between attacks |
| Seeker / AttackDamage | 40 | Damage of the main attack |
| Seeker / ScanCooldown | 20 | Seconds between player scans (roar) |
| Seeker / ThirdPerson | true | Start in third person |
| Seeker / CameraDistance | 3.2 | Third-person camera distance |
| Seeker / SpeedMultiplier | 1.35 | Monster movement speed |
| Seeker / InfiniteStamina | true | Monster never runs out of stamina |
| Seeker / Invincible | false | Monster can't be damaged (off: hiders may kill it with traps and win) |
| Seeker / BlindWhileHiding | true | Black screen for the monster while the others hide |
| Seeker / MonsterScale | 1.0 | Size of the monster model |
| Round / SeekerChoosesMonster | true | Seeker picks their monster while the others hide |
| Seeker / CanGrab | true | Monster can grab (needed for doors) |
| Seeker / LaserDuration | 3 | Seconds the laser fires |
| Test / TestMode | false | Solo test mode with training dummies |
| Test / DummyCount | 4 | Dummies spawned around you in test mode |
| Controls / Attack, Roar, ToggleView | mouse L / mouse R / V | Input System paths, e.g. `<Keyboard>/f` |


## Credits
| | |
|---|---|
| **Flyguy** | Art and animation – logo, How To Play pages, Monster Showcase drawings (made with Aseprite) |
| **Napguin** | Programming (mostly AI) |

**Special thanks:** SongoCat ([twitch.tv/songocat](https://www.twitch.tv/songocat)), MadKitKat, Looley18, Stewberryicetea

## Coming soon
- More monsters and specials
- A **How to Play** with hand-drawn animations
- More features – stay tuned!

## Beta builds
Upcoming changes are tested as beta builds in [`test-builds/`](test-builds/) (not on Thunderstore).
See the top of [`thunderstore/CHANGELOG.md`](thunderstore/CHANGELOG.md) for what's new. Everyone in the lobby needs the same build.

## Bugs & ideas
Found a bug or have an idea for a monster's special? Open an
[issue](https://github.com/Napguin/REPO-Monster-Hide-and-Seek/issues) – a `BepInEx/LogOutput.log` from your mod
manager profile helps a lot.

## Development
- Changelog: [`thunderstore/CHANGELOG.md`](thunderstore/CHANGELOG.md)
- Build & publish guide: [`BUILD.md`](BUILD.md) – zips in `release/` are published to Thunderstore automatically,
  test builds go to `test-builds/`.
