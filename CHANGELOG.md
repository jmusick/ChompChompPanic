# Changelog

All notable changes to Chomp Chomp Panic! are listed here, newest first. Versions follow [semantic versioning](https://semver.org/); the current one is shown on the title screen.

## [0.3.0] - 2026-10-02

### Added

- **Diagonal avenues** now cut through the city here and there, running corner to corner across groups of blocks with sidewalks and street lamps. People and traffic use them, and they stop short of canals.

### Changed

- City blocks come in different sizes, from narrow strips to wide blocks, so the streets no longer form an even grid.
- People and vehicles merge into their lane quickly after a turn instead of drifting across the whole street.
- Road center lines have slightly longer dashes.

## [0.2.2] - 2026-10-02

### Fixed

- Vehicles you're big enough to eat no longer hurt you when you brush against them on the way in.

## [0.2.1] - 2026-10-02

### Added

- The game version is shown in the bottom-right corner of the title screen.

## [0.2.0] - 2026-10-02

### Changed

- Cars, jeeps and tanks no longer run from a kaiju too small to eat them. They keep driving, and running into one hurts: 4 health for cars and jeeps, 10 for tanks. Once you're big enough to eat them, they flee as before.

### Added

- Screen Shake on/off option in the Options menu.

## [0.1.0] - 2026-10-02

Everything before versioning began. This version was never tagged; it covers the work up to and including commit `6253d2b`.

### Added

- **Title screen** with the logo and a menu you can drive with keyboard, gamepad or mouse. Esc on the game-over screen returns to it.
- **Pause menu** (Esc, P or Start) with Resume, Options, Title Screen and Quit Game. The game, including sprite animations, freezes while paused.
- **Options** for music and sound-effect volume, reachable from the title screen and the pause menu, and remembered between sessions.
- **Music:** four Tokyo chiptune tracks that play shuffled with a crossfade. Music gets quieter while paused and on the game-over screen.
- **Sound effects** for chomping, crunching vehicles, gunfire, rockets and explosions, bullet hits, smashing buildings, footsteps, growing into the next prey tier, winning, losing and the menus. The kaiju sounds deeper as it grows, and distant gunfire is quieter.
- Windows build profile.

### Changed

- Sessions last 10 minutes instead of 20.
- The kaiju can only smash buildings no bigger than itself; bigger ones block its way.
- Soldiers, jeeps and tanks show up in larger numbers earlier in a session, instead of building up slowly.

### Fixed

- Characters of the same size no longer flicker when they overlap.
