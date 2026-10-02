# LDraw for Unity

LDraw import/conversion tooling and runtime extracted from the infiniteless brick engine.

- **`LDraw/Editor/`** — importer window, conversion pipeline, library extractor, model browser,
  part/texture importers and part-mesh sync (editor-only tooling).
- **`LDraw/Runtime/`** — runtime part model: parsing, mesh building, materials, LOD/stud-distance
  utilities, model spawner, and the mod hooks.
- **`LDraw/Resources/`** — color table, material library, and part catalog assets.

The generated artifacts (materials, meshes, prefabs, textures, player-model meshes) and the
third-party LDraw part library are **not** included — they are produced by the importer from an
LDraw library you supply.

`LDraw.Editor.csproj` / `LDraw.Runtime.csproj` are the reference project files.
