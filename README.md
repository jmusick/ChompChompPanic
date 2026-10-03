# Chomp Chomp Panic!

![Chomp Chomp Panic! logo](Assets/Art/Branding/chomp-chomp-panic-logo-v3.png)

A top-down pixel-art kaiju game set in an endless night-time Tokyo. You start small, eat whatever you can catch, and grow until you're chewing on tanks and swatting fighter jets out of the sky. Survive the full session to win.

A game by [Stone Dragon Media, LLC](https://stonedragonmedia.com).

Built with Unity 6 (6000.5.6f1) and the Universal Render Pipeline (2D).

## Gameplay

- **Eat to grow.** Anything smaller than you is food. Prey comes in tiers, each roughly twice the size of the last:
  people and soldiers → cars and jeeps → tanks → fighter jets.
- **Smash the city.** Buildings no bigger than you crumble when you walk through them. Bigger ones block your way.
- **The military fights back.** Riflemen, bazooka troops, jeeps, tanks and jets shoot at you. You have 100 health. Eating people heals you a little, and eating a rival kaiju heals you completely.
- **Rival kaiju.** Every so often another monster shows up. If it's bigger than you, it hunts you and can eat you. If it's smaller, it runs and you can eat it.
- **Survive the clock.** A session lasts 10 minutes. You lose if your health runs out or a rival eats you.
- **Unlock more kaiju.** Each time you survive a full session, you unlock the next kaiju: Mecha-Chomp the robot, then Octo-Chomp the octopus, then Moth-Chomp the giant moth. Once you have more than one, **Start Game** asks which kaiju you want to play. Each has its own look and sounds. Any kaiju you aren't playing can turn up as a rival, even before you've unlocked it.

## Controls

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Move | WASD / arrow keys | Left stick |
| Pause menu (resume, options, title, quit) | Esc / P | Start |
| Menu choose / adjust | Arrows / WASD | D-pad / left stick |
| Menu select | Enter / Space | A (South) |
| Menu back | Esc | B (East) |
| Restart (game over) | R / Space / Enter | A (South) / Start |
| Back to title (game over) | Esc | B (East) / Select |

The title and pause menus also work with the mouse. **Options** sets the music and sound-effect volume, which is saved between runs.

## Getting started

1. Install [Git LFS](https://git-lfs.com/) before cloning. Images, audio and fonts are stored in LFS.
   ```bash
   git lfs install
   ```
   ```bash
   git clone https://github.com/jmusick/ChompChompPanic.git
   ```
2. Open the folder in Unity Hub with editor version **6000.5.6f1**.
3. Open `Assets/Scenes/Title.unity` and press Play. The game scene is `Assets/Scenes/SampleScene.unity`.

## Art pipeline

All game art is pixel art drawn by Python scripts in `ArtSource/`. The scripts write PNG strips into `Assets/Art/`, and the Unity importer slices them into sprites.

1. Install Python 3 with Pillow (and numpy for the People and kaiju scripts).
2. Run the script for whatever you changed, for example:
   ```bash
   python ArtSource/City/build_city.py
   ```
3. In Unity, run **Chomp Chomp Panic > Import Art**. This slices every sheet listed in a `sprite_frames.json` and assigns the sprites to the `GameManager` in the open scene.

| Script | Output |
| --- | --- |
| `ArtSource/City/build_city.py` | City atlas: streets, buildings, parks, canals |
| `ArtSource/People/build_people.py` | Civilians |
| `ArtSource/Cars/build_cars.py` | Cars |
| `ArtSource/Military/build_military.py` | Soldiers, jeeps, tanks, jets, projectiles, muzzle flash, explosions |
| `ArtSource/Rivals/build_rivals.py` | Recolored rival kaiju (made from the player's sprites) |
| `ArtSource/Mecha/build_mecha.py` | Mecha-Chomp, the unlockable robot kaiju |
| `ArtSource/Octo/build_octo.py` | Octo-Chomp, the unlockable octopus kaiju |
| `ArtSource/Moth/build_moth.py` | Moth-Chomp, the unlockable moth kaiju |
| `ArtSource/Effects/build_effects.py` | Blood spurts and ground stains |
| `ArtSource/Kaiju/preview/build_sprites.py` | Player kaiju (cleans up a generated atlas) |

## Project layout

```
Assets/
  Scenes/      Title.unity (first in the build), SampleScene.unity (the game)
  Scripts/     Runtime C# (namespace ChompChompPanic)
  Editor/      ArtImporter.cs: the Import Art menu
  Art/         Generated sprites and atlases (don't hand-edit; regenerate from ArtSource)
ArtSource/     Python scripts that draw the art, plus preview sheets
```

## License

Chomp Chomp Panic! is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE.md). You can play, study, modify and share it for noncommercial purposes. Commercial use is not permitted without separate permission from the author.
