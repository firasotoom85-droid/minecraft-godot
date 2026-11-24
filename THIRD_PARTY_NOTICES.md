# Third-party notices

This repository's own code and documentation are licensed under the MIT License in the root LICENSE file. Third-party components retain their own licenses; the root project license does not replace them.

## Bundled development add-on

| Component | Location | License | Notice |
|---|---|---|---|
| Godot AI editor/MCP add-on | addons/godot_ai/ | MIT | The add-on's own LICENSE file is retained at addons/godot_ai/LICENSE. |

## Engine and runtime used for builds

| Component | Version used by this project | License information |
|---|---|---|
| Godot Engine .NET | 4.7.2 | Godot Engine is MIT-licensed; see the upstream [Godot license](https://github.com/godotengine/godot/blob/master/LICENSE.txt). |
| .NET SDK/runtime | 10.0.x | Microsoft .NET components and their notices are maintained upstream; see the [.NET runtime repository](https://github.com/dotnet/runtime) and its license files. |

The export workflow packages the Godot .NET export output. Review the upstream engine/runtime notices when updating the engine or SDK version.

## Project assets

The current project favors original procedural voxel materials, generated item icons and procedural sound. Do not assume a newly imported asset is covered by the project MIT license.

Before adding an external asset, record:
- Asset name and file path.
- Creator/rightsholder.
- Original source URL.
- Exact license name and license URL.
- Whether the asset was modified and how attribution is displayed.

Use original work or assets whose licenses permit the intended distribution. Never copy proprietary Minecraft textures, sounds, models or other protected assets.

## Updating this file

Update this inventory whenever a new add-on, package, font, texture, sound, model or other third-party component is introduced. Preserve each component's original license and required attribution with the distributed build.
