using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Tiny input wrapper so the movement scripts work with BOTH the new Input System
/// and the legacy Input Manager. Rebind keys here later (or swap for your own input layer).
/// </summary>
public static class GameInput
{
    public static Vector2 Move()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k == null) return Vector2.zero;
        Vector2 v = new Vector2(
            (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f),
            (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f));
        return Vector2.ClampMagnitude(v, 1f);
#else
        return Vector2.ClampMagnitude(
            new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#endif
    }

    /// <summary>Raw mouse movement this frame, roughly in "mouse counts".</summary>
    public static Vector2 LookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse m = Mouse.current;
        return m != null ? m.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
#endif
    }

    public static bool SprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    public static bool JumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    public static bool CrouchPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.C);
#endif
    }

    public static bool ReloadPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    public static bool ReloadHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.isPressed;
#else
        return Input.GetKey(KeyCode.R);
#endif
    }

    public static bool ClickHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    public static bool ClickReleased()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }

    /// <summary>Cursor position in screen pixels (only meaningful while the cursor is free).</summary>
    public static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    public static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    public static bool ClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    public static bool InteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    public static bool DropPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.G);
#endif
    }

    public static bool DropHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.gKey.isPressed;
#else
        return Input.GetKey(KeyCode.G);
#endif
    }

    /// <summary>-1, 0 or +1 per frame (one step per mouse wheel notch, up = +1).</summary>
    public static int ScrollStep()
    {
#if ENABLE_INPUT_SYSTEM
        float y = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
        float y = Input.mouseScrollDelta.y;
#endif
        return Mathf.Abs(y) < 0.01f ? 0 : (y > 0f ? 1 : -1);
    }

    /// <summary>Right mouse button: aim down sights.</summary>
    public static bool AimPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }

    public static bool AimHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    /// <summary>Left mouse button pressed this frame: start pulling the trigger.</summary>
    public static bool FirePressed() => ClickPressed();

    /// <summary>Left mouse button held: trigger pulled.</summary>
    public static bool FireHeld() => ClickHeld();

    /// <summary>B: flick the selector switch (Safe / Semi / Burst / Auto).</summary>
    public static bool FireModePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.B);
#endif
    }

    /// <summary>T: press the magazine release (only used in reload interaction mode).</summary>
    public static bool MagReleasePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.T);
#endif
    }

    /// <summary>Y: press the bolt catch while the mouse is busy dragging the charging handle (locks the bolt open).</summary>
    public static bool BoltCatchPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Y);
#endif
    }
}