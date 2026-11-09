using System;
using System.Linq;
using System.Text;
using com.radiant.engine.input;
using Microsoft.Xna.Framework;
using Xna = Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.runtime;

/// <summary>Only this adapter translates MonoGame keyboard/mouse/text into Radiant input.</summary>
internal sealed class MonoGameInputSource : IInputSource, IDisposable
{
    private readonly GameWindow Window;
    private readonly Func<bool> Focused;
    private readonly StringBuilder PendingText = new();
    private static readonly (Key Radiant, Xna.Keys Native)[] Keys = Enum.GetValues<Key>()
        .Where(key => key != Key.None).Select(key => (key, Enum.Parse<Xna.Keys>(key.ToString()))).ToArray();

    public MonoGameInputSource(GameWindow Window, Func<bool> Focused)
    {
        this.Window = Window;
        this.Focused = Focused;
        Window.TextInput += OnTextInput;
    }
    private void OnTextInput(object Sender, TextInputEventArgs Args)
    {
        if (Focused()) PendingText.Append(Args.Character);
    }
    public InputSnapshot Capture()
    {
        var nativeKeyboard = Xna.Keyboard.GetState();
        var mouse = Xna.Mouse.GetState();
        Span<Key> pressed = stackalloc Key[Keys.Length];
        int count = 0;
        foreach (var pair in Keys)
            if (nativeKeyboard.IsKeyDown(pair.Native)) pressed[count++] = pair.Radiant;
        string text = PendingText.ToString();
        PendingText.Clear();
        return new(new KeyboardState(pressed[..count]), new MouseState(mouse.X, mouse.Y, mouse.ScrollWheelValue,
            Convert(mouse.LeftButton), Convert(mouse.MiddleButton), Convert(mouse.RightButton),
            Convert(mouse.XButton1), Convert(mouse.XButton2), mouse.HorizontalScrollWheelValue), text, Focused());
    }
    private static ButtonState Convert(Xna.ButtonState State) => State == Xna.ButtonState.Pressed ? ButtonState.Pressed : ButtonState.Released;
    public void Dispose()
    {
        Window.TextInput -= OnTextInput;
        PendingText.Clear();
    }
}
