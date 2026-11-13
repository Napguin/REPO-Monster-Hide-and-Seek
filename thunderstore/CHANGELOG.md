# Changelog

## 1.5.36 (beta)
- Tumble-Bash: much more control (turns 300°/s with the camera instead of 120°/s) and softer wall bounces – it
  mostly slides along the wall, only pushes off a little and loses half its speed for a moment.

## 1.5.35 (beta)
- **Tick – Tumble-Bash** (was Leap attack): rolls around fast for 4 s and bounces off walls; steer a little with the
  camera. Anyone hit tumbles, gets knocked away and takes 30 damage (once per bash). Cooldown 30 s.

## 1.5.34 (beta)
- **Fix: players saw different monsters** after the monster switched models while hiding. Each PC decided on its own,
  so a pick near the end of the hiding time was taken by the monster player but refused by the host. Now only the
  host decides and tells everyone; when the hunt starts the host sends the monster once more, so everyone matches.
- Swarm (Banger) confirmed working: the summoned Bangers chase visible hiders and explode next to them.

## 1.5.33 (beta)
- Swarm (Banger): the game spawns Bangers as a whole group. The mod destroyed the extra ones right away, which broke
  the group on the other players' PCs (error in the game's Banger director). Now the whole group (up to 8) is kept.

## 1.5.32 (beta)
- Balloon Mines: with all 5 balloons out you get "All 5 balloons are out – one has to pop first" (was "next in 0 s").
  When none is left but one is recharging, the ability bar shows that countdown.
- Swarm: positions of the real Gnomes / Bangers are taken from their physics body (Bangers roll around), and the host
  log shows every 2 s what each one sees (`[Swarm] Banger ...: nearest hider X m, sees ...`).

## 1.5.31 (beta)
- Noxious Gas: the slowdown comes in 5 s blocks – touching the gas gives 5 s, still inside when it runs out gives
  another 5 s (before, it lasted 5 s after leaving the gas).

## 1.5.30 (beta)
- Player Scan no longer flashes a "ROAR!" text first – only the scan result is shown. Remaining "Roar" labels
  in the help text are now "Scan".

## 1.5.29 (beta)
- **New cooldowns:** Rapid Charge 40 s, Flying Charge 20 s, Swarm 70 s, Tornado 40 s, Lunge 30 s, Charge 30 s,
  Head bomb 40 s, Laser 40 s, Gamble 30 s, Noxious Gas 40 s, Shot 15 s, Radar Ping 120 s, Flying / Zero-G Grab 30 s,
  Item Throw 6 s (1 s after picking up), Lasso 25 s, Puke 30 s, Leap (Tick) 30 s, Impulse 30 s, Launch 30 s.
- **Laser:** 50 damage per second for 3 s (was 50 every 0.5 s). The `LaserCooldown` config is gone (fixed 40 s).
- **Balloon Mines:** 5 balloons; each one comes back 40 s after it popped (no more popping the oldest).
- **Noxious Gas:** clouds last 10 s. Touching the gas tranquilizes you for 5 s; still inside after that = another 5 s.
- **Zero-G Grab (Mentalist):** lasts 8 s (was 4 s).

## 1.5.28 (beta)
- **Swarm attacks now:** the host sends every summoned Gnome / Banger after the closest hider it can actually see
  (15 m, line of sight), and touching a hider hurts – Gnome 10 damage per second, Banger 35 damage (3 m) and pops.
- **Zero-G Grab floats again:** the host (who runs the ragdoll physics) now lifts caught hiders slowly to ~2.5 m and
  lets them hover until the ability ends. Log lines `[Zero-G] host floats ...`.

## 1.5.27 (beta)
- **Gambit** now only rolls the specials the other monsters currently have (built from the monster list, so it
  follows every change automatically). Old specials no monster has anymore (Scream, Ground slam, Heart gas,
  Levitate, Throw) are gone from the pool. Item Throw stays out (needs two presses).

## 1.5.26 (beta)
- **Birthday Boy – Balloon Mines:** each use places a balloon hovering in the air (max 5 – the oldest pops when you
  place a 6th). Armed after 1 s; when a hider comes within ~1.8 m it explodes: 40 damage within 2.5 m.
- Snatcher Lasso reels players in a bit slower (2.5 m/s instead of 3.5 m/s).

## 1.5.25 (beta)
- Rugrat: the "no collision with the held item" fix now also works in single player / for the host (it
  didn't find the item there).

## 1.5.24 (beta)
- Rugrat Item Throw: the held item no longer collides with the Rugrat – big items can't push the monster around
  anymore when looking around. Collision comes back shortly after the throw.

## 1.5.23 (beta)
- **Snatcher (the pony)** now has **Lasso Grab** (was Leap attack).

## 1.5.22 (beta)
- **Pony – Lasso Grab:** throws a lasso where the camera looks (18 m). A hit hider is reeled in towards the monster at
  a steady speed for 5 s – they can still dodge sideways. The monster is very slow while it holds the rope.
  (Also in the Gambit's pool.)

## 1.5.21 (beta)
- **Bowtie Tornado traps players:** hiders that get within 2.5 m are knocked over and spun around inside the
  tornado until it fades, then flung out. While trapped they can't grab anything. Equipment / weapons are sucked in
  like valuables. Training dummies are carried around too.

## 1.5.20

### After the round – revive, truck & vote (GitHub issue #4)
Requested in [issue #4](https://github.com/Napguin/REPO-Monster-Hide-and-Seek/issues/4): dead players should come
back between rounds. Now, when a round ends:
- Surviving hiders are **shown to everyone** for 6 s, so you see where they were hiding.
- **Every dead player is revived and healed**, and everyone is **teleported back into the truck**.
- A **vote** follows: volunteer to be the next monster (a random volunteer gets it), and vote to stay on this map
  or switch to one of 3 other maps.
- Can be turned off in the F8 menu (`Round / AfterRound`).

### New and reworked specials
- **Bowtie – Tornado:** a small tornado wanders forward for 7 s, sucks in items and valuables, whirls them around
  and flings everything away at the end – flying items hurt hiders. Hiders nearby are pulled in a little.
- **Upscream – Launch:** the closest hider (6 m) tumbles and is launched where the monster looks (25 damage).
- **Trudge – Impulse:** hiders within 9 m tumble, get sucked towards the Trudge, then a shockwave blasts them away
  (40 damage).
- **Rugrat – Item Throw:** grab a nearby valuable, use again to throw it really fast – real physics, 20–90 damage
  depending on speed and weight.
- **Heart Hugger – Noxious Gas:** 5 gas clouds that drift out and linger for 10 s; hiders inside are tranquilized
  (very slow, sluggish camera).
- **Gnome & Banger are playable – Swarm:** summons 5 of the game's real Gnomes / Bangers that hunt hiders on their own
  for 30 s (they have to spot them first and ignore the monster).
- **Robe & Headgrab – Charge:** short wind-up, then a fast horizontal dash (~14 m), 60 damage on contact.
  WASD is locked during the charge and it always goes the full distance.
- **Headman & Chef – Lunge:** like Charge, but in the camera direction (also up/down and in the air).
- **Elsa & Bella** now have Rapid Charge.
- **Flying Grab / Zero-G Grab:** caught hiders now tumble and can't get up, so the monster can grab and carry them.
  Flying Grab lasts 8 s.
- **Gambit** can roll every special in the mod – new specials are added automatically.
- Clown laser: 50 damage per tick. Radar Ping shows hiders for 2 s.

### Maps & menu
- F8 menu: **map size (level 1–20)** and a **Load new map** button – higher level = bigger map and higher moon phase.
- F8 menu: **hide / hunt time recommendation by moon phase** (maps get bigger every phase), applied with one click.

### Fixes & polish
- The monster can't open the map (Tab) during a round.
- Flying monsters use your own jump / crouch keys, stop below the ceiling, and the third-person camera no longer
  looks through ceilings or walls.

### Coming soon
More monsters are being worked on, plus a **How to Play** with hand-drawn animations. More features are on the way!

## 1.5.0
- **Damage instead of instant kills:** the main attack does 40 damage (players have 100 HP, config `AttackDamage`).
  Running into hiders no longer kills them. Specials do damage too: Laser 25 / 0.5 s, Shot 60, Leap 40, Throw 40.
- **Player Scan** (roar): shows how many players are within 15 m instead of the direction. Cooldown 20 s (`ScanCooldown`).
- **Radar Ping** (was X-ray): shows position and distance of every hider within 30 m for 1 s, 2 min cooldown. Every hider gets told
  whether they were detected or not.
- **Rapid Charge** (was Sprint): 5 s at 1.5x speed, 20 damage every 0.5 s to hiders close by, no main attack meanwhile.
- Test mode: no cooldowns.
- **Oogly: Flying Grab** – invisible stun bubble around it, and it can always fly. **Mentalist: Zero-G Grab** – stun bubble
  with zero gravity. **Spewer** can always fly too (space up, ctrl down).
- Cleanup Crew's head bounces off walls and explodes after 4 s (blinking faster and faster).
- Clown can't move while charging / firing the laser.
- Clown laser stays on the crosshair (jumping no longer knocks it off for the rest of the round).
- Oogly model tilted 45° more upright.
- Monster sounds no longer stack and play at most 2.5 s (Spewer's long puke sound).
- **Specials closer to the game:** Cleanup Crew throws its head (bomb + uranium cloud), Spewer pukes,
  Mentalist levitates and slams, Trudge ground-slams, Heart Hugger pulls hiders in with gas,
  Bowtie's scream pushes hiders away, Gambit rolls a random special.
- Apex Predator shows its attack form during Flying Charge. Rapid / Flying Charge have a visible aura.
- Fix: monster chosen during the hiding phase (e.g. Reaper, Elsa) was invisible / in the wrong form – hiding the own body
  no longer resets the monster's animation.
- Third-person camera no longer goes through the ceiling with tall monsters.
- Apex Predator: the monster-form meshes are shown during Flying Charge (and the duck grows).
- Softer effects: weaker colours, no flickering stun overlay, no spheres/sparks drawn around the camera
  (tight rooms, first person), charge aura is a ring on the floor.
- Big scan / radar waves are rings on the floor now (no more tinted screen). Oogly: stray trail parts hidden.
- **Flying Charge** (new, Apex Predator): fly for 6 s (space up, ctrl down), 10 damage every 0.5 s to hiders close by.

## 1.4.0
- **Every monster has a special ability** (middle mouse / F):
  Laser, Shot, Sprint, Leap attack, X-ray vision, Throw, Shockwave – see the README for who has what.
- Specials are visible everywhere: monster cards in both menus, ability bar with cooldowns for the monster,
  hiders see the monster's special while hiding and get a warning when it is used.
- **Echo roar:** roaring now shows the monster arrows towards every hider within 15 m for 1.5 s
  (direction only, no distance). Hiders it finds get an "Echoed!" warning.
- Monster models: animators are pushed into a visible state (big form, spawned, not despawning/stunned)
  and the model is re-grounded after transforming – fixes Elsa and similar two-form monsters.
- New full-screen **start menu (F8)**: seeker, monster and rules side by side, scores, big start button.

## 1.3.1
- New full-screen **monster selection**: card grid, details with the monster's abilities, countdown bar,
  "Random monster" button, keyboard control (arrows/WASD, Enter), reopen with **C** during the hiding phase.
- If the monster dies, the round now ends and the hiders win (scores are counted).
- "Monster invincible" now blocks every way the game can hurt or kill a player, not just one.
- Multiplayer: kills (touch, attack, laser) use the real network position of other players and
  horizontal distance – abilities should now hit other players, not only test dummies.
- More robust check that monster actions really come from the monster's player.
- Log shows distances for every attack (helps with tuning).

## 1.3.0
- **Score system** (totals over all rounds, shown after every round and in the F8 menu; host can reset):
  - Monster: 1 point per hider eaten.
  - Hiders: whoever lasts longest gets as many points as there were hiders, the next one 1 less, …,
    the first one eliminated gets 0. Everyone alive at the end gets the full points. Aborted rounds don't count.
- **Seeker picks their own monster** during the hiding phase (can be switched off in the F8 menu).
- **Clown laser:** laser monsters fire the game's real laser with the middle mouse button or **F**
  (3 s, 12 s cooldown). Everything the beam touches dies; walls and objects block it.
- **Monster can open doors again:** grabbing is allowed (doors, buttons, carts are grabbed in R.E.P.O.).
  Can be switched off with `Seeker / CanGrab`.

## 1.2.3
- Fix for R.E.P.O. v0.5.0: `PlayerController.OverrideSpeed` changed, which crashed the whole monster control
  every frame (no camera, attack, roar, speed or stamina). Game methods that can change between versions are
  now called version-tolerantly, and every monster feature runs separately.
- Harmony patches are applied one by one; a missing target only disables that one feature.
- Monster models are stripped in dependency order: no more "Can't remove … depends on it" errors in the log.

## 1.2.2
- Third-person camera now hooks into R.E.P.O.'s own camera render step (CameraUpdate) in addition to the render callbacks.
- Camera distance and height adapt to the size of the monster.
- Safety: if the third-person camera doesn't work, your own monster body is hidden so it never blocks your view.
- Roar plays through the game's own sound system; falls back to a generated growl if a monster has no sounds.
- Roar also on **R**, and shows a "ROAR!" banner.
- Test overlay shows camera status and the last played sound.

## 1.2.1
- Fix: third-person camera did nothing (R.E.P.O.'s camera is not Unity's `Camera.main`).
- Fix: monster controls were blocked by a wrong chat check.
- Fix: test keys (F9/F10/F11) now work independently of the monster controls.
- Seeker position for kills/dummies now taken from the player controller.
- One failing part no longer stops the rest of the mod; errors are shown in the test overlay and the log.
- More diagnostics in the test overlay and the log.

## 1.2.0
- **Test mode** for solo testing: start a round alone, you become the monster and training dummies act as hiders.
- Attack and touch kills work on the dummies; the overlay shows hit/miss with distance and angle.
- Test keys: F9 next monster (Shift+F9 previous), F10 spawn a dummy in front of you, F11 toggle overlay.
- Debug overlay and detailed log for each monster model (renderers, size, sounds, animator).

## 1.1.0
- The seeker now **is** a real R.E.P.O. monster: the model is taken from the game's enemies (choose one or random).
- Third-person camera for the monster so you see your own monster body (toggle with V).
- Monster abilities: **Attack** (left mouse, lunge + kill in front of you) and **Roar** (right mouse, plays the monster's sound, hiders nearby see a red flash).
- Monster sounds come from the original enemy.
- Monster can't grab objects.
- New settings: attack range, cooldowns, camera distance, key bindings.

## 1.0.0
- Initial release: Hide & Seek game mode with monster seeker.
