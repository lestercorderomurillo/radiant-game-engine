using System;
using System.Collections.Generic;

namespace com.radiant.engine.core;

public partial class Renderer
{
    private readonly Stack<RenderTarget2D[]> RenderTargetStack = new();
    private RenderTarget2D[] CurrentTargets;

    private void BindTarget(RenderTarget2D target)
    {
        Commands.SetTargets(target == null ? ReadOnlySpan<RenderTarget2D>.Empty : new[] { target });
        CurrentTargets = target == null ? null : new[] { target };
    }

    /// <summary>Saves a snapshot of the active render targets, with no nesting depth limit.</summary>
    public IRenderer PushTargets()
    {
        RenderTargetStack.Push(CurrentTargets);
        return this;
    }

    /// <summary>Restores the most recently saved render targets.</summary>
    public IRenderer PopTargets()
    {
        if (RenderTargetStack.Count == 0) return this;
        CommitTextures();
        var targets = RenderTargetStack.Peek();
        if (targets == null) BindTarget(SceneRT);
        else { Commands.SetTargets(targets); CurrentTargets = targets; }
        RenderTargetStack.Pop();
        return this;
    }

    /// <summary>Selects the scene surface when target is null.</summary>
    public IRenderer SetTarget(RenderTarget2D target)
    {
        CommitTextures();
        BindTarget(target ?? SceneRT);
        return this;
    }

    public IRenderer SetTargets(RenderTarget2D target0, RenderTarget2D target1) => SetTargets(new[] { target0, target1 });
    public IRenderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2) => SetTargets(new[] { target0, target1, target2 });
    public IRenderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2, RenderTarget2D target3) => SetTargets(new[] { target0, target1, target2, target3 });

    /// <summary>Binds targets and retains an independent snapshot for nested restoration.</summary>
    public IRenderer SetTargets(params RenderTarget2D[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var snapshot = (RenderTarget2D[])targets.Clone();
        CommitTextures();
        Commands.SetTargets(snapshot);
        CurrentTargets = snapshot;
        return this;
    }

    public IRenderer Clear(Color? color = null)
    {
        CommitTextures();
        Commands.Clear(color ?? Color.Black);
        return this;
    }
}
