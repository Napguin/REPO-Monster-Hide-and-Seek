# Hide And Seek Monster

A **Hide & Seek** game mode for R.E.P.O.

One player becomes the **monster** (seeker). Everyone else has a short time to hide –
then the monster is released and kills every hider it touches.

> **Every player in the lobby needs this mod installed.**

## How to play

1. Start a run and enter a level.
2. The **host** presses **F8** to open the Hide & Seek menu.
3. Pick the seeker – a specific player or **Random**.
4. Adjust hide time, hunt time, kill range and monster speed if you like.
5. Press **Start round**.

### Phases
| Phase | What happens |
|---|---|
| **Hiding** | The monster is frozen and its screen is black. Hiders run and hide. |
| **Hunting** | The monster is released (faster, infinite stamina, invincible). Touching a hider kills it. |
| **End** | The monster wins when every hider is dead. The hiders win if at least one survives the hunt timer. |

Normal enemies are despawned while a round is running (can be turned off).

## Features
- Choose the seeker manually or randomly
- Seeker turns into a glowing, horned monster (visible to all other players)
- Configurable hide time, hunt time, kill range, monster speed and size
- Optional: blind monster while hiding, invincible monster, infinite stamina, disable normal enemies
- English and German texts

## Configuration
`BepInEx/config/HideAndSeekMonster.cfg` (created after the first start).
The round settings of the **host** are used for everyone.

| Setting | Default | Description |
|---|---|---|
| General / MenuKey | F8 | Key to open the menu |
| General / Language | English | English or Deutsch |
| Round / HideTime | 30 | Seconds to hide |
| Round / RoundTime | 300 | Seconds the monster has to hunt |
| Round / KillRange | 1.6 | Kill distance in metres |
| Round / DisableEnemies | true | Despawn normal enemies during a round |
| Seeker / SpeedMultiplier | 1.35 | Monster movement speed |
| Seeker / InfiniteStamina | true | Monster never runs out of stamina |
| Seeker / Invincible | true | Monster can't be damaged |
| Seeker / BlindWhileHiding | true | Black screen for the monster while the others hide |
| Seeker / MonsterScale | 1.0 | Size of the monster model |
