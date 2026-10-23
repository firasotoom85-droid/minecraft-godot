using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Startup validation for the hand-written content tables. Content is still
/// C# data, so a typo (a tile outside the atlas, a recipe pointing at an old
/// item, an enum value without a definition) would otherwise fail at runtime
/// in the mesher or crafting UI. Keep this cheap: it only reads tables, never
/// builds textures.
/// </summary>
public static class ContentValidation
{
    /// <summary>Returns every problem found; an empty list means valid content.</summary>
    public static List<string> Validate()
    {
        List<string> errors = [];

        foreach (BlockId id in Enum.GetValues<BlockId>())
        {
            int raw = (int)id;
            if (raw < 0 || raw >= BlockDb.BlockCount)
            {
                errors.Add($"block {id}: id {raw} exceeds BlockCount {BlockDb.BlockCount}");
                continue;
            }

            if (!BlockDb.IsDefined(id))
            {
                errors.Add($"block {id}: no BlockDef");
                continue;
            }

            BlockDb.BlockDef def = BlockDb.DefOf(id);
            if (string.IsNullOrWhiteSpace(def.Name))
            {
                errors.Add($"block {id}: empty name");
            }

            for (int face = 0; face < def.Tiles.Length; face++)
            {
                int tile = def.Tiles[face];
                if (tile < -1 || tile >= BlockDb.TileCount)
                {
                    errors.Add($"block {id}: face {face} tile {tile} outside 0..{BlockDb.TileCount - 1}");
                }
            }
        }

        foreach (BlockId id in BlockDb.Hotbar)
        {
            if (!BlockDb.IsDefined(id))
            {
                errors.Add($"hotbar: undefined block {id}");
            }
        }

        foreach (ItemId id in Enum.GetValues<ItemId>())
        {
            if (id == ItemId.None)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ItemDb.NameOf(id)))
            {
                errors.Add($"item {id}: empty name");
            }

            if (ItemDb.MaxStack(id) <= 0)
            {
                errors.Add($"item {id}: invalid max stack");
            }
        }

        foreach (ItemId item in ItemDb.Placeable)
        {
            if (ItemDb.BlockFor(item) is not { } block)
            {
                errors.Add($"item {item}: listed as placeable but has no block");
                continue;
            }

            if (!BlockDb.IsDefined(block))
            {
                errors.Add($"item {item}: maps to undefined block {block}");
            }

            if (ItemDb.BlockItem(block) != item)
            {
                errors.Add($"item {item}: block {block} does not round-trip");
            }
        }

        foreach (Recipe recipe in Recipes.All)
        {
            if (recipe.Pattern.Length == 0)
            {
                errors.Add($"recipe {recipe.Name}: empty pattern");
            }

            int width = 0;
            foreach (string row in recipe.Pattern)
            {
                width = Math.Max(width, row.Length);
            }

            if (width > 3)
            {
                errors.Add($"recipe {recipe.Name}: width {width} exceeds a 3x3 grid");
            }

            foreach (string row in recipe.Pattern)
            {
                foreach (char ch in row)
                {
                    if (ch != '.' && !recipe.Key.ContainsKey(ch))
                    {
                        errors.Add($"recipe {recipe.Name}: pattern key '{ch}' is not in Key");
                    }
                }
            }

            foreach ((char ch, ItemId item) in recipe.Key)
            {
                if (string.IsNullOrWhiteSpace(ItemDb.NameOf(item)))
                {
                    errors.Add($"recipe {recipe.Name}: key '{ch}' uses unknown item {item}");
                }
            }

            if (string.IsNullOrWhiteSpace(ItemDb.NameOf(recipe.Result)))
            {
                errors.Add($"recipe {recipe.Name}: unknown result {recipe.Result}");
            }

            if (recipe.ResultCount <= 0)
            {
                errors.Add($"recipe {recipe.Name}: result count {recipe.ResultCount} <= 0");
            }
        }

        return errors;
    }

    /// <summary>Log every problem once; used at boot, never throws in normal play.</summary>
    public static void ValidateOrWarn()
    {
        foreach (string error in Validate())
        {
            GD.PushError($"minecraft content: {error}");
        }
    }
}
