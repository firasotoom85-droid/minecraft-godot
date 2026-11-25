using Godot;

namespace Minecraft;

/// <summary>Modal overlay that owns the mouse and player input while open.</summary>
public interface IModalUi
{
    /// <summary>Close from outside (death screen, scene teardown).</summary>
    void ForceClose();
}

/// <summary>
/// Shared ownership of the mouse/input hand-back. Modals used to restore
/// captured input unconditionally, which made the death screen unclickable
/// when the player died with the inventory or a panel open.
/// </summary>
public static class UiFocus
{
    public const string ModalGroup = "ui_modal";

    /// <summary>Return input to play, keeping the cursor free while dead.</summary>
    public static void Restore(Player player)
    {
        player.InputLocked = false;
        Input.MouseMode = player.Stats.Dead
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
    }

    /// <summary>Give controller/keyboard navigation a deterministic starting focus.</summary>
    public static void FocusFirst(Control root)
    {
        Control? target = FindFocusable(root);
        target?.GrabFocus();
    }

    public static void RestoreFocus(Control? preferred, Control? fallback)
    {
        if (CanReceiveFocus(preferred) && preferred!.IsInsideTree() && preferred.IsVisibleInTree())
        {
            preferred.GrabFocus();
            return;
        }
        if (fallback != null && GodotObject.IsInstanceValid(fallback)
                && fallback.IsInsideTree() && fallback.IsVisibleInTree())
            FocusFirst(fallback);
    }

    internal static Control? FindFocusable(Node node)
    {
        if (node is CanvasItem item && !item.Visible)
            return null;
        if (node is Control self && CanReceiveFocus(self))
            return self;
        foreach (Node child in node.GetChildren())
        {
            Control? nested = FindFocusable(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private static bool CanReceiveFocus(Control? control)
    {
        if (control == null || !GodotObject.IsInstanceValid(control)
                || control.FocusMode == Control.FocusModeEnum.None)
            return false;
        if (control is BaseButton button && button.Disabled)
            return false;
        return true;
    }

    /// <summary>Close every open modal; used when the death screen takes over.</summary>
    public static void CloseAll(Node owner)
    {
        foreach (Node node in owner.GetTree().GetNodesInGroup(ModalGroup))
        {
            if (node is IModalUi modal)
            {
                modal.ForceClose();
            }
        }
    }
}
