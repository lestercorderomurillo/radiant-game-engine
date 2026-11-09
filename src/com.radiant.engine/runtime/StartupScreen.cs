using System;
using System.Diagnostics;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework.Graphics;
using NativeColor = Microsoft.Xna.Framework.Color;
using NativeRectangle = Microsoft.Xna.Framework.Rectangle;
using NativeVector = Microsoft.Xna.Framework.Vector2;

namespace com.radiant.engine.runtime;

/// <summary>Draws the official branding with live status and progress in backbuffer coordinates.</summary>
internal sealed class StartupScreen : IDisposable
{
    private readonly GraphicsDevice Device;
    private readonly SpriteBatch Batch;
    private readonly Texture2D Artwork;
    private readonly Texture2D Pixel;
    private readonly FontSystem Fonts;
    private readonly Stopwatch Animation = Stopwatch.StartNew();

    public StartupScreen(GraphicsDevice Device)
    {
        this.Device = Device;
        Batch = new SpriteBatch(Device);
        using var Image = typeof(StartupScreen).Assembly.GetManifestResourceStream("Radiant.Branding.Splash");
        Artwork = Texture2D.FromStream(Device, Image);
        Pixel = new Texture2D(Device, 1, 1);
        Pixel.SetData(new[] { NativeColor.White });
        Fonts = new FontSystem();
        using var Font = typeof(StartupScreen).Assembly.GetManifestResourceStream("Radiant.Branding.Font");
        using var Bytes = new MemoryStream();
        Font.CopyTo(Bytes);
        Fonts.AddFont(Bytes.ToArray());
    }

    public void Draw(LoadingProgress Progress, bool Failed, RenderTarget2D Target = null)
    {
        Device.SetRenderTarget(Target);
        Device.Clear(NativeColor.Black);
        int Width = Target?.Width ?? Device.PresentationParameters.BackBufferWidth;
        int Height = Target?.Height ?? Device.PresentationParameters.BackBufferHeight;
        float Scale = Math.Min(Width / 1536f, Height / 1024f);
        float Left = (Width - 1536 * Scale) / 2;
        float Top = (Height - 1024 * Scale) / 2;
        NativeRectangle Bounds(float X, float Y, float W, float H) => new(
            (int)(Left + X * Scale), (int)(Top + Y * Scale),
            Math.Max(1, (int)(W * Scale)), Math.Max(1, (int)(H * Scale)));

        Batch.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend,
            samplerState: Microsoft.Xna.Framework.Graphics.SamplerState.LinearClamp);
        // Only the approved logo, wordmark, wave and tagline are raster artwork.
        // The baked concept status/bar below y=870 are excluded, never displayed.
        Batch.Draw(Artwork, Bounds(0, 0, 1536, 870), new NativeRectangle(0, 0, 1536, 870), NativeColor.White);
        var Font = Fonts.GetFont(Math.Max(10, 26 * Scale));
        string Message = Progress.Message;
        float AvailableWidth = 1416 * Scale;
        while (Message.Length > 1 && Font.MeasureString(Message).X > AvailableWidth)
            Message = Message[..^2] + "…";
        Batch.DrawString(Font, Message, new NativeVector(Left + 60 * Scale, Top + 900 * Scale), NativeColor.White);
        Batch.Draw(Pixel, Bounds(60, 944, 1416, 14), new NativeColor(64, 64, 64));
        if (!Failed && Progress.Completion is double Completion)
        {
            if (Completion > 0) Batch.Draw(Pixel, Bounds(60, 944, (float)(1416 * Completion), 14), NativeColor.White);
        }
        else if (!Failed)
        {
            float Offset = (float)((Math.Sin(Animation.Elapsed.TotalSeconds * 2.2) + 1) / 2) * 1132;
            Batch.Draw(Pixel, Bounds(60 + Offset, 944, 284, 14), NativeColor.White);
        }
        Batch.End();
    }

    public void Dispose()
    {
        Fonts.Dispose();
        Pixel.Dispose();
        Artwork.Dispose();
        Batch.Dispose();
    }

    internal static NativeRectangle AboutBounds(int Width, int Height)
    {
        float Scale = Math.Min(0.6f, Math.Min(Width * 0.9f / 1536, Height * 0.9f / 1024));
        int PanelWidth = Math.Max(1, (int)(1536 * Scale));
        int PanelHeight = Math.Max(1, (int)(1024 * Scale));
        return new NativeRectangle((Width - PanelWidth) / 2, (Height - PanelHeight) / 2, PanelWidth, PanelHeight);
    }

    internal static NativeRectangle AboutCloseBounds(NativeRectangle Bounds)
    {
        int Size = Math.Min(28, Math.Max(12, Bounds.Width / 20));
        int Inset = Math.Min(12, Math.Max(2, Bounds.Width / 60));
        return new NativeRectangle(Bounds.Right - Size - Inset, Bounds.Top + Inset, Size, Size);
    }

    public void DrawAbout(NativeRectangle Bounds, string Version, NativeColor CloseColor, NativeColor CloseText,
        RenderTarget2D Target = null)
    {
        Device.SetRenderTarget(Target);
        Batch.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend,
            samplerState: Microsoft.Xna.Framework.Graphics.SamplerState.LinearClamp);
        Batch.Draw(Pixel, Device.Viewport.Bounds, new NativeColor(0, 0, 0, 170));
        Batch.Draw(Pixel, Bounds, NativeColor.Black);
        float Scale = Bounds.Width / 1536f;
        Batch.Draw(Artwork, new NativeRectangle(Bounds.X, Bounds.Y, Bounds.Width, (int)(870 * Scale)),
            new NativeRectangle(0, 0, 1536, 870), NativeColor.White);
        var Font = Fonts.GetFont(Math.Max(10, 24 * Scale));
        string Footer = Version + "  ·  Developed by Lester Cordero Murillo";
        while (Footer.Length > 1 && Font.MeasureString(Footer).X > Bounds.Width - 120 * Scale)
            Footer = Footer[..^2] + "…";
        Batch.DrawString(Font, Footer,
            new NativeVector(Bounds.X + 60 * Scale, Bounds.Y + 930 * Scale), new NativeColor(175, 175, 175));
        var Close = AboutCloseBounds(Bounds);
        Batch.Draw(Pixel, Close, CloseColor);
        var Center = new NativeVector(Close.Center.X, Close.Center.Y);
        foreach (float Angle in new[] { MathF.PI / 4, -MathF.PI / 4 })
            Batch.Draw(Pixel, Center, null, CloseText, Angle, new NativeVector(0.5f, 0.5f),
                new NativeVector(Close.Width * 0.45f, 1.5f), Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        Batch.End();
    }
}
