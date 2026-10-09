## Version 1.1.0
- Better fit: the tabard's shape has been adjusted to cut down on armour poking through it.
- Existing TabardPacks work unchanged - the pattern layout is exactly the same as 1.0.0.

## Version 1.0.0
First public release.
- A wearable tabard with its own model, worn over chest armour and under belts, straps and pauldrons. It has its own equipment slot, so it doesn't replace your cape, chest or utility item.
- Fitted for both male and female bodies, and moves with the legs.
- Custom designs from zip packs: one folder per tabard inside the zip, with a Tabard.json and Pattern1.png, Pattern2.png ... (up to 16 styles). Each folder is one crafting entry, and players pick the style at the crafting station, like vanilla shields.
- Each pattern image holds the front (left half) and back (right half). Transparent areas become holes, for ragged hems and cut-outs.
- Optional normal map and metal mask per tabard.
- Icons are made automatically from the front of each pattern if a pack doesn't include its own.
- The mod writes a guide (HOW TO MAKE A TABARD.txt) and a paint-over UV template into Documents\Valheim Custom Tabards.
- Tabards from packs you don't have (worn by another player, on the ground, in chests) show as a quarterly magenta and black "missing" tabard, with an on-screen notice and details in chat. Nothing is lost: install the pack and they're back.
- Removing a pack turns its tabards into "missing" tabards instead of deleting them.
- Pack problems (files at the top of the zip, missing Tabard.json, broken JSON, wrong image type) are explained in the BepInEx log.
