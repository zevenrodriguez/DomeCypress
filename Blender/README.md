# Blender

Everything used to build the cypress dome in Blender and export it to the Unity project (`../domecypress`).

| Folder / file | What it is |
|---|---|
| `domecypress.blend` | Main swamp scene (CypressDome, DuckweedSwamp, HybridSwamp, lush layer) |
| `roots.blend` | Ghost orchid roots scene (title sequence) |
| `scripts/` | Blender Python scripts: run from Blender's Scripting tab |
| `textures/` | Leaf, understory and litter texture atlases the .blend and scripts use |
| `ghostorchid/` | Ghost orchid model and textures (Meshy export) |
| `renders/` | Preview renders |
| `backups/` | Older exports kept for safety |

## Scripts

| Script | Does |
|---|---|
| `build_cypress_dome.py`, `build_duckweed_swamp.py`, `build_hybrid_swamp.py` | Build the swamp scenes |
| `build_lush_layer.py` | Extra canopy, ferns, moss, litter; exports `CypressLush.fbx` to Unity |
| `export_cypress_unity.py` | Exports `CypressDome.fbx`, the grass LODs and `cypress_scene.json` to Unity |
| `export_tree_anchors.py` | Writes the trunk positions (`tree_anchors.json`) for the orchid interaction |
| `make_*.py` | Generate the texture atlases in `textures/` |

The scripts find the Unity project automatically whether the `.blend` sits in `DomeCypress/` or here in
`DomeCypress/Blender/`. Keep `textures/` next to the `.blend` - the scenes and scripts load them from there.
