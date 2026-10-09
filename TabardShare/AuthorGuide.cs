namespace TabardShare
{
    /// <summary>Plain-text guide written into the drop folder on every launch, so pack authors always have it.</summary>
    internal static class AuthorGuide
    {
        public const string FileName = "HOW TO MAKE A TABARD.txt";
        public const string UvLayoutFileName = "Tabard - UV layout.png";

        public const string Text =
@"TABARDSHARE - HOW TO MAKE A TABARD PACK
=======================================

(This file is rewritten every time the game starts - don't keep notes in it.)

INSTALLING TABARDS
------------------
Put TabardPack .zip files in this 'Valheim Custom Tabards' folder and start the game.
Don't unzip them. Remove a zip to remove its tabards. Everyone on a server needs the same zips.


PACK LAYOUT
-----------
Each tabard is a FOLDER INSIDE THE ZIP. Each folder becomes ONE entry in the crafting menu, and
every pattern in it is a style of that entry (picked at the crafting station, like shield styles):

  MyTabards.zip
    BlackDog\                 <- the tabard's ID. Must be unique. Don't rename it later.
      Tabard.json             <- required
      Pattern1.png            <- style 1 artwork
      Pattern2.png            <- style 2 artwork (up to 16)
      Icon1.png               <- optional icon for style 1
      Icon2.png               <- optional icon for style 2
      BumpMap.png             <- optional
      MetallicGlossMap.png    <- optional
    GrandAthera\
      Tabard.json
      Pattern1.png

Rules:
- Files loose at the top of the zip won't work.
- Folders inside folders won't work - the tabard folder must be directly inside the zip.
- A folder without Tabard.json is skipped.
- File names are fixed (capitals don't matter). Images must be .png.
- The folder name (the ID) may only use letters, digits, - and _ ; anything else becomes _.
- Folders placed directly in 'Valheim Custom Tabards' are ignored. Zip them.
- More than 16 styles? Make a second tabard folder.


THE ARTWORK (PatternN.png)
--------------------------
- One image holds the WHOLE tabard: the FRONT on the LEFT half, the BACK on the RIGHT half,
  neck at the top, hem at the bottom.
- Paint over  _Templates\Tabard - UV layout.png  - it shows exactly where each panel goes.
  Use it as a bottom layer in your paint program and hide it before saving.
  Paint a little past the panel outlines so no edge shows through.
- Size: square. 1024 x 1024 is the reference; 2048 x 2048 for fine detail.
  Width and height should be multiples of 4 (all the sizes above are) so the game can compress it.
- Transparency CUTS HOLES. Anything less than 50% opaque is not drawn at all - you can see
  through it. Use this on purpose for ragged hems or cut-out shapes; otherwise keep the
  panels fully opaque.
- Number the patterns from 1: Pattern1.png, Pattern2.png ... Pattern16.png.


ICONS (IconN.png, optional)
---------------------------
If a style has no icon, one is made from the FRONT of its pattern.
To use your own, add Icon1.png, Icon2.png ... matching the Pattern numbers.
Square, any size; 128 x 128 is plenty.


Tabard.json (required)
----------------------
The file must exist, but every line in it is optional. Leave something out and you get:
name = the folder name, crafted at the workbench for 4 Leather scraps.
The smallest valid Tabard.json is just:  {}

{
  ""displayName"": ""Black Dog Tabard"",
  ""description"": ""Worn by the Black Dogs."",
  ""craftingStation"": ""workbench"",
  ""minStationLevel"": 1,
  ""hidden"": false,
  ""requirements"": [
    { ""item"": ""LeatherScraps"", ""amount"": 4 },
    { ""item"": ""DeerHide"", ""amount"": 2 }
  ]
}

- craftingStation: workbench, forge, stonecutter, artisan, blackforge, galdr, cauldron, none
  (or the game's own name, e.g. piece_workbench)
- requirements: use the game's item names (Wood, LeatherScraps, DeerHide, LinenThread ...).
- hidden: true means the tabard can't be crafted (existing ones still work).
- styleCount: only use the first N patterns (normally they are just counted).
If Tabard.json can't be read (a missing comma, say), that tabard is skipped and the BepInEx
log says exactly what's wrong.


ADVANCED (optional)
-------------------
Both follow the same layout as the patterns and apply to EVERY style of the tabard:
- BumpMap.png: a normal map (the usual blue-purple kind) for stitching, weave, embroidery.
- MetallicGlossMap.png: white = metal (studs, gold thread), black = cloth. Experimental.


REMOVING A PACK
---------------
Tabards from a pack that's been removed don't vanish from players' inventories - they turn into
""missing"" tabards (quarterly magenta and sable). Put the zip back and they return to normal.
(The list of tabards the mod has seen is kept in BepInEx\config\TabardShare\known-tabards.json.)


PLAYING WITH OTHERS
-------------------
If another player has a tabard from a pack you don't have, you'll see it as a magenta and black
""missing"" tabard - on them, on the ground or in a chest - and a message tells you to check chat,
where each missing tabard is listed. Nothing is lost: once you install the pack and restart,
those tabards look normal again. Everyone should use the same packs.


WHERE THINGS GO
---------------
- Players and authors use:  Documents\Valheim Custom Tabards\   (.zip files only)
- The mod keeps a working copy in BepInEx\config\TabardShare\Tabards\ - don't edit there;
  it's refreshed from the zips every launch.
- Folders starting with _ (like _Templates) are the mod's own helpers.
";
    }
}
