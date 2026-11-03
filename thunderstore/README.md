# Hide And Seek Monster

A **Hide & Seek** game mode for R.E.P.O. where the seeker **becomes a real monster of the game**.

One player turns into a monster – e.g. a Huntsman, Robe, Trudge, … (taken from the game's own enemies).
Everyone else has a short time to hide. Then the monster is released and hunts them down.

> **Every player in the lobby needs this mod installed.**

## How to play

1. Start a run and enter a level.
2. The **host** presses **F8** to open the Hide & Seek menu.
3. Pick the **seeker** (a player or Random) and the **monster** (a monster or Random).
4. Adjust times, attack range, speed and monster size if you like.
5. Press **Start round**.

### As the monster
| Control | Action |
|---|---|
| **Left mouse** | Attack – lunge forward and kill hiders in front of you |
| **Right mouse** / **R** | Roar – plays the monster's sound, nearby hiders see a red flash |
| **V** | Switch between third person (see your monster) and first person |

The monster is faster, has infinite stamina, is invincible and cannot pick up objects.
Simply touching a hider also kills it.

### Test mode (solo)
Enable **Test mode** in the F8 menu (or `Test / TestMode` in the config) and start a round alone.
You become the monster, training dummies stand around you as hiders, hiding time is only 3 s.

| Key | Action |
|---|---|
| **F9** / **Shift+F9** | Next / previous monster model |
| **F10** | Spawn a dummy in front of you |
| **F11** | Toggle the debug overlay |

The overlay shows the monster's model info and whether your last attack hit (distance and angle).
Details are also written to `BepInEx/LogOutput.log`. Dummies only exist on the host's screen.

### Phases
| Phase | What happens |
|---|---|
| **Hiding** | The monster is frozen and its screen is black. Hiders run and hide. |
| **Hunting** | The monster is released and hunts. |
| **End** | The monster wins when every hider is dead. The hiders win if at least one survives the hunt timer. |

Normal enemies are despawned while a round is running (can be turned off).

## Configuration
`BepInEx/config/HideAndSeekMonster.cfg` (created after the first start).
The round settings of the **host** are used for everyone.

| Setting | Default | Description |
|---|---|---|
| General / MenuKey | F8 | Key to open the menu |
| General / Language | English | English or Deutsch |
| Round / HideTime | 30 | Seconds to hide |
| Round / RoundTime | 300 | Seconds the monster has to hunt |
| Round / TouchKillRange | 1.2 | Touching a hider within this distance kills it |
| Round / DisableEnemies | true | Despawn normal enemies during a round |
| Seeker / AttackRange | 2.8 | Reach of the attack in metres |
| Seeker / AttackCooldown | 1.2 | Seconds between attacks |
| Seeker / RoarCooldown | 8 | Seconds between roars |
| Seeker / ThirdPerson | true | Start in third person |
| Seeker / CameraDistance | 3.2 | Third-person camera distance |
| Seeker / SpeedMultiplier | 1.35 | Monster movement speed |
| Seeker / InfiniteStamina | true | Monster never runs out of stamina |
| Seeker / Invincible | true | Monster can't be damaged |
| Seeker / BlindWhileHiding | true | Black screen for the monster while the others hide |
| Seeker / MonsterScale | 1.0 | Size of the monster model |
| Test / TestMode | false | Solo test mode with training dummies |
| Test / DummyCount | 4 | Dummies spawned around you in test mode |
| Controls / Attack, Roar, ToggleView | mouse L / mouse R / V | Input System paths, e.g. `<Keyboard>/f` |
