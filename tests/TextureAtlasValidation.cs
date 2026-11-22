using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Command-line atlas audit. Writes a Markdown report and exits non-zero for
/// either errors or warnings so it can be used in CI and release checks.
/// </summary>
public partial class TextureAtlasValidation : Node
{
    public override void _Ready()
    {
        try
        {
            TextureValidationReport report = TextureAtlasValidator.ValidateCurrent();
            string[] args = OS.GetCmdlineUserArgs();
            string outputPath = args.Length > 0
                    ? args[0]
                    : "user://procedural-texture-validation.md";

            using FileAccess? output = FileAccess.Open(outputPath, FileAccess.ModeFlags.Write);
            if (output == null)
                throw new InvalidOperationException($"Could not open texture validation output '{outputPath}': {FileAccess.GetOpenError()}.");

            output.StoreString(report.ToMarkdown());
            GD.Print($"TEXTURE_ATLAS_VALIDATION status={(report.IsClean ? "PASS" : report.IsValid ? "WARN" : "FAIL")} blocks={Enum.GetValues<BlockId>().Length} referenced_tiles={report.ReferencedTiles}/{report.TileCount} painted_tiles={report.PaintedTiles} item_mappings={report.PlaceableItems} errors={report.ErrorCount} warnings={report.WarningCount} report={outputPath}");
            foreach (TextureValidationIssue issue in report.Issues)
                GD.Print($"{issue.Severity} {issue.Code}: {issue.Message}");

            GetTree().Quit(report.IsClean ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"Texture atlas validation failed: {exception}");
            GetTree().Quit(2);
        }
    }
}
