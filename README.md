# Hauntess

Hauntess is a funny Counter-Strike 2 plugin that turns haunted maps into true nightmare fuel by crushing visibility with thick, inky fog. It is great for spooky Workshop maps like **cs_deadhouse** and other haunted house layouts where you want players to feel lost in the dark.

## What it does

- Forces a dark, fog-heavy atmosphere across the current map.
- Disables common Workshop overrides that keep maps bright (gradient fog, cubemap fog, and post-processing volumes).
- Continuously re-applies the fog so map logic cannot reset it.
- Removes player visibility while outside the radius of fog.
- Removes player names & ui elements.
- Adds friendlyfire and increases ff damage of bullets.
- Removes banning from excess ff damage.
- !unhaunt & !haunt command fully functional.

## Usage

- The plugin runs automatically on map start and keeps fog synced every tick.
- You can also force the haunted atmosphere at any time with:

```
css_haunt
```

```
!haunt in chat to turn on hauntess.
!unhaunt in chat to turn off hauntess.
```

## Notes

- The fog settings are tuned for heavy darkness (short view distance, max density).
- If your map fights the fog, the plugin neutralizes typical lighting helpers before applying its own.

## Build

This project is a CounterStrikeSharp plugin.

```
dotnet build
```

## Credits
- @Kandru for FogOfWar.cs & pointing out the fix for player visibility.
