# DomeCypress

An Everglades cypress dome habitat built in Blender and exported to Unity for fulldome display.

## Repository layout

| Path | What it is |
|---|---|
| `domecypress.blend`, `roots.blend` | Main Blender scenes |
| `build_*.py`, `make_*.py`, `export_*.py` | Blender Python scripts that generate the swamp, textures and the Unity export |
| `textures/` | Generated texture atlases |
| `ghostorchid/` | Ghost orchid model and textures |
| `domecypress/` | Unity project (open this folder in Unity Hub) |
| `*.png` at the root | Preview renders |

## Cloning

Large files (`.blend`, `.fbx`, images) are stored with [Git LFS](https://git-lfs.com). Install it once before cloning:

```bash
git lfs install
git clone https://github.com/zevenrodriguez/DomeCypress.git
```

## Opening in Unity

Add `domecypress/` in Unity Hub. The Unity version is listed in `domecypress/ProjectSettings/ProjectVersion.txt`. The `Library/` folder is not in the repo, so the first open takes a few minutes while Unity re-imports assets.
