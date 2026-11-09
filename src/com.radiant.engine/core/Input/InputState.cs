using System;
using System.Numerics;

namespace com.radiant.engine.input;

/// <summary>Logical keys understood by Radiant. Values are independent of any platform key codes.</summary>
public enum Key
{
    None, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Escape, Enter, Space, Tab, Back, Delete, Insert, Home, End, PageUp, PageDown,
    Left, Right, Up, Down, LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
    LeftWindows, RightWindows, Apps, CapsLock, NumLock, Scroll, Pause, PrintScreen,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Add, Subtract, Multiply, Divide, Decimal,
    OemPlus, OemMinus, OemComma, OemPeriod, OemQuestion, OemSemicolon, OemQuotes, OemOpenBrackets, OemCloseBrackets, OemPipe, OemTilde, OemBackslash
}

public enum ButtonState { Released, Pressed }
public enum MouseButton { Left, Middle, Right, X1, X2 }

/// <summary>Immutable, allocation-free key snapshot. Default means no keys are held.</summary>
public readonly struct KeyboardState
{
    private readonly ulong Low, High;
    public KeyboardState(params Key[] Keys) : this(Keys.AsSpan()) { }
    public KeyboardState(ReadOnlySpan<Key> Keys)
    {
        ulong low = 0, high = 0;
        foreach (var key in Keys)
        {
            if (!Enum.IsDefined(key)) throw new ArgumentOutOfRangeException(nameof(Keys));
            int index = (int)key;
            if (index == 0) continue;
            if (index < 64) low |= 1UL << index;
            else high |= 1UL << (index - 64);
        }
        Low = low; High = high;
    }
    public bool IsKeyDown(Key Key)
    {
        int index = (int)Key;
        if (index <= 0 || index > (int)global::com.radiant.engine.input.Key.OemBackslash) return false;
        return index < 64 ? (Low & (1UL << index)) != 0 : (High & (1UL << (index - 64))) != 0;
    }
    public bool IsKeyUp(Key Key) => !IsKeyDown(Key);
}

/// <summary>Mouse position in window client pixels; wheel values are cumulative units (120 per notch).</summary>
public readonly record struct MouseState(int X = 0, int Y = 0, int ScrollWheelValue = 0,
    ButtonState LeftButton = ButtonState.Released, ButtonState MiddleButton = ButtonState.Released,
    ButtonState RightButton = ButtonState.Released, ButtonState XButton1 = ButtonState.Released,
    ButtonState XButton2 = ButtonState.Released, int HorizontalScrollWheelValue = 0)
{
    public Vector2 Position => new(X, Y);
    public bool IsDown(MouseButton Button) => (Button switch
    {
        MouseButton.Left => LeftButton, MouseButton.Middle => MiddleButton, MouseButton.Right => RightButton,
        MouseButton.X1 => XButton1, MouseButton.X2 => XButton2,
        _ => throw new ArgumentOutOfRangeException(nameof(Button))
    }) == ButtonState.Pressed;
}

/// <summary>One backend sample, including committed UTF-16 text since the previous capture.</summary>
public readonly record struct InputSnapshot(KeyboardState Keyboard, MouseState Mouse, string Text = "", bool IsFocused = true);

/// <summary>Stable input shared by every scene/system callback in one variable update.</summary>
public sealed class InputFrame
{
    public static InputFrame Empty { get; } = new(default, default);
    public InputSnapshot Current { get; }
    public InputSnapshot Previous { get; }
    public KeyboardState Keyboard => Current.Keyboard;
    public MouseState Mouse => Current.Mouse;
    public string Text => Current.Text ?? "";
    public bool IsFocused => Current.IsFocused;
    public Vector2 MouseDelta => Current.IsFocused && Previous.IsFocused ? Mouse.Position - Previous.Mouse.Position : Vector2.Zero;
    public int ScrollDelta => Current.IsFocused && Previous.IsFocused ? Mouse.ScrollWheelValue - Previous.Mouse.ScrollWheelValue : 0;

    public InputFrame(InputSnapshot Current, InputSnapshot Previous)
    {
        this.Current = Normalize(Current);
        this.Previous = Normalize(Previous);
    }
    private static InputSnapshot Normalize(InputSnapshot State) => State.IsFocused ? State :
        new(default, new MouseState(State.Mouse.X, State.Mouse.Y, State.Mouse.ScrollWheelValue,
            HorizontalScrollWheelValue: State.Mouse.HorizontalScrollWheelValue), "", false);
    public bool IsDown(Key Key) => Keyboard.IsKeyDown(Key);
    public bool WasPressed(Key Key) => Keyboard.IsKeyDown(Key) && Previous.Keyboard.IsKeyUp(Key);
    public bool WasReleased(Key Key) => Keyboard.IsKeyUp(Key) && Previous.Keyboard.IsKeyDown(Key);
    public bool IsDown(MouseButton Button) => Mouse.IsDown(Button);
    public bool WasPressed(MouseButton Button) => Mouse.IsDown(Button) && !Previous.Mouse.IsDown(Button);
    public bool WasReleased(MouseButton Button) => !Mouse.IsDown(Button) && Previous.Mouse.IsDown(Button);
}

/// <summary>Captures input once per variable update, on the loop thread. Text must be drained exactly once.</summary>
public interface IInputSource
{
    InputSnapshot Capture();
}

/// <summary>Default input for runtimes without input devices.</summary>
public sealed class NullInputSource : IInputSource
{
    public static NullInputSource Instance { get; } = new();
    private NullInputSource() { }
    public InputSnapshot Capture() => default;
}
