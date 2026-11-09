using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>Queues and draws diagnostic geometry and text overlays.</summary>
public class GizmosRenderer : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.Overlay;

    private const float FontSize = 28f;

    private List<LineGizmo> LineQueue = new();
    private List<CircleGizmo> CircleQueue = new();
    private List<ArcGizmo> ArcQueue = new();
    private List<TextGizmo> TextQueue = new();
    private List<RectGizmo> RectQueue = new();

    private new bool Enabled = false;
    private const float TextPadding = 4f;
    private Color TextBackgroundColor = new(0, 0, 0, 180);

    /// <summary>Toggles visibility of the diagnostic overlay.</summary>
    public void ToggleGizmos() => Enabled = !Enabled;

    /// <inheritdoc/>
    public override void Initialize() { }

    /// <inheritdoc/>
    public override void Update()
    {
        ClearGizmoQueues();
    }

    /// <summary>Clears queued diagnostic primitives after their frame has been drawn.</summary>
    private void ClearGizmoQueues()
    {
        LineQueue.Clear();
        CircleQueue.Clear();
        ArcQueue.Clear();
        TextQueue.Clear();
        RectQueue.Clear();
    }

    /// <summary>Queues a diagnostic line for the overlay pass.</summary>
    public void AddGizmoLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
    {
        LineQueue.Add(new LineGizmo(start, end, color, thickness));
    }

    /// <summary>Queues a diagnostic circle for the overlay pass.</summary>
    public void AddGizmoCircle(Vector2 center, float radius, Color color)
    {
        CircleQueue.Add(new CircleGizmo(center, radius, color));
    }

    /// <summary>Queues a diagnostic arc for the overlay pass.</summary>
    public void AddGizmoArc(Vector2 center, float radius, float startAngle, float endAngle, Color color)
    {
        ArcQueue.Add(new ArcGizmo(center, radius, startAngle, endAngle, color));
    }

    /// <summary>Queues a diagnostic rectangle for the overlay pass.</summary>
    public void AddGizmoRect(Rectangle rect, Color color, bool filled = false)
    {
        RectQueue.Add(new RectGizmo(rect, color, filled));
    }

    /// <summary>Queues a diagnostic text label for the overlay pass.</summary>
    public void AddGizmoText(Vector2 position, string text, Color color)
    {
        TextQueue.Add(new TextGizmo(position, text, color));
    }

    /// <inheritdoc/>
    public override void LateRender()
    {
        // Scale from virtual coordinates to actual screen pixels so gizmos
        // resize proportionally with the window (resolution-independent).
        var Scale = Matrix.CreateScale(
            Renderer.ScreenWidth / Renderer.VirtualWidth,
            Renderer.ScreenHeight / Renderer.VirtualHeight,
            1f);

        if (Enabled)
        {
            Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Renderer.WorldTransform);
            RenderGizmos();
            Renderer.EndDraw();
        }

        // Always render build number in bottom-left corner
        var BuildText = Window.BuildTag;
        var TextSize = Renderer.MeasureString("Inter", FontSize, BuildText);
        var BuildPos = new Vector2(15, Renderer.VirtualHeight - TextSize.Y - 15);

        Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend, transform: Scale);
        RenderTextWithBackground(new TextGizmo(BuildPos, BuildText, Color.Gray));
        Renderer.EndDraw();
    }

    /// <summary>Draws the queued diagnostic primitives and labels.</summary>
    private void RenderGizmos()
    {
        foreach (var line in LineQueue)
            RenderLine(line);

        foreach (var circle in CircleQueue)
            RenderCircle(circle);

        foreach (var arc in ArcQueue)
            RenderArc(arc);

        foreach (var rect in RectQueue)
            RenderRect(rect);

        foreach (var text in TextQueue)
            RenderTextWithBackground(text);
    }

    /// <summary>Draws a diagnostic label over a readable background.</summary>
    private void RenderTextWithBackground(TextGizmo text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        Vector2 textSize = Renderer.MeasureString("Inter", FontSize, text.Text);

        Rectangle backgroundRect = new(
            (int)(text.Position.X - TextPadding),
            (int)(text.Position.Y - TextPadding),
            (int)(textSize.X + TextPadding * 2),
            (int)(textSize.Y + TextPadding * 2)
        );

        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), backgroundRect, TextBackgroundColor);
        Renderer.DrawString("Inter", FontSize, text.Text, text.Position, text.Color);
    }

    /// <summary>Draws a queued line with its requested color and thickness.</summary>
    private void RenderLine(LineGizmo line)
    {
        Vector2 delta = line.End - line.Start;
        float length = delta.Length();

        if (length == 0) return;

        float rotation = (float)Math.Atan2(delta.Y, delta.X);

        Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), new Rectangle(
            (int)line.Start.X, (int)line.Start.Y,
            (int)length, (int)line.Thickness),
            null, line.Color, rotation,
            new Vector2(0, 0.5f));
    }

    /// <summary>Draws a queued circle at its requested position and radius.</summary>
    private void RenderCircle(CircleGizmo circle)
    {
        const int segments = 32;
        Vector2 prev = circle.Center + new Vector2(circle.Radius, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * (global::System.MathF.PI * 2) / segments;
            Vector2 next = circle.Center + new Vector2(
                (float)Math.Cos(angle) * circle.Radius,
                (float)Math.Sin(angle) * circle.Radius
            );

            RenderLine(new LineGizmo(prev, next, circle.Color, 1f));
            prev = next;
        }
    }

    /// <summary>Draws a queued arc over its requested angular range.</summary>
    private void RenderArc(ArcGizmo arc)
    {
        const int segments = 32;
        float angleRange = arc.EndAngle - arc.StartAngle;
        int numSegments = Math.Max(1, (int)(segments * Math.Abs(angleRange) / (global::System.MathF.PI * 2)));

        Vector2 prev = arc.Center + new Vector2(
            (float)Math.Cos(arc.StartAngle) * arc.Radius,
            (float)Math.Sin(arc.StartAngle) * arc.Radius
        );

        for (int i = 1; i <= numSegments; i++)
        {
            float angle = arc.StartAngle + (angleRange * i / numSegments);
            Vector2 next = arc.Center + new Vector2(
                (float)Math.Cos(angle) * arc.Radius,
                (float)Math.Sin(angle) * arc.Radius
            );

            RenderLine(new LineGizmo(prev, next, arc.Color, 1f));
            prev = next;
        }
    }

    /// <summary>Draws a queued rectangle using its requested outline or fill.</summary>
    private void RenderRect(RectGizmo rect)
    {
        if (rect.Filled)
        {
            Renderer.DrawSprite(Renderer.GetSolidTexture(Color.White), rect.Rect, rect.Color);
        }
        else
        {
            Vector2 topLeft = new(rect.Rect.X, rect.Rect.Y);
            Vector2 topRight = new(rect.Rect.Right, rect.Rect.Y);
            Vector2 bottomLeft = new(rect.Rect.X, rect.Rect.Bottom);
            Vector2 bottomRight = new(rect.Rect.Right, rect.Rect.Bottom);

            RenderLine(new LineGizmo(topLeft, topRight, rect.Color, 1f));
            RenderLine(new LineGizmo(topRight, bottomRight, rect.Color, 1f));
            RenderLine(new LineGizmo(bottomRight, bottomLeft, rect.Color, 1f));
            RenderLine(new LineGizmo(bottomLeft, topLeft, rect.Color, 1f));
        }
    }

    /// <inheritdoc/>
    public override void Dispose() { }
}
