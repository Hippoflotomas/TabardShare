# TabardShare

A Valheim mod: a custom-modelled tabard in its own equipment slot, with designs loaded from TabardPack zips
that players share among themselves. The player-facing description is in
[`TabardShare/Package/README.md`](TabardShare/Package/README.md); an example pack is in [`Examples/`](Examples).

Built on [Jötunn](https://github.com/Valheim-Modding/Jotunn) from the JotunnModStub template.

## Layout
- `TabardShare/` - the plugin (C#, net48). `Package/` holds the Thunderstore manifest, icon, README and changelog.
- `TabardShareUnity/` - Unity project that builds the tabard model into the `tabardshare` asset bundle
  (`Tools > TabardShare > Build Asset Bundle`; it copies `Art/tabard.fbx` in first and writes the bundle to
  `TabardShare/Assets/tabardshare`, which is embedded in the DLL).
- `Art/tabard.fbx` - the model, exported from Blender. `TabardUVLayout.png` - its UV layout (also embedded,
  and written out for pack authors as the paint-over template).
- `Examples/` - a sample TabardPack.

## Building
- **Debug**: builds and copies the DLL to `BepInEx\plugins` (or `MOD_DEPLOYPATH`).
- **Release**: builds the Thunderstore package into `publishables/` (see `publishables/README.md`).
  Bump `PluginVersion` in `TabardShare.cs` and `version_number` in `Package/manifest.json` together.

## Changing the model
The UV layout is a contract with every published pack. Reshape the mesh freely, but **don't move the UVs**,
or existing packs will map wrongly.
