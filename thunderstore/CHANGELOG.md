# Changelog

## 1.5.0 (beta)
- **Damage instead of instant kills:** the main attack does 40 damage (players have 100 HP, config `AttackDamage`).
  Running into hiders no longer kills them. Specials do damage too: Laser 25 / 0.5 s, Shot 60, Leap 40, Throw 40.
- **Player Scan** (roar): shows how many players are within 15 m instead of the direction. Cooldown 20 s (`ScanCooldown`).
- **Radar Ping** (was X-ray): shows position and distance of every hider within 30 m for 1 s, 2 min cooldown. Every hider gets told
  whether they were detected or not.
- **Rapid Charge** (was Sprint): 5 s double speed, 20 damage every 0.5 s to hiders close by, no main attack meanwhile.
- Test mode: no cooldowns.
- **Specials closer to the game:** Cleanup Crew throws its head (bomb + uranium cloud), Spewer pukes,
  Mentalist levitates and slams, Trudge ground-slams, Heart Hugger pulls hiders in with gas,
  Bowtie's scream pushes hiders away, Gambit rolls a random special.
- Apex Predator shows its attack form during Flying Charge. Rapid / Flying Charge have a visible aura.
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
