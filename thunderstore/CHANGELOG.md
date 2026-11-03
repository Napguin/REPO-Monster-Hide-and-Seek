# Changelog

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
