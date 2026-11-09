# Radiant startup

Desktop `MonoGameWindow` / `GameClient` instances show the official Radiant splash by default
in a temporary 768×512 borderless, non-resizable window, without native close/minimize/maximize
buttons. On completion, the same native window restores the requested game size,
decorations, resizing and maximized state. Escape still cancels startup.
The artwork is embedded in the engine assembly, together with its status font, so loading
works from a published application or another working directory. It fits the window without
stretching and keeps black margins when the aspect ratio differs from 3:2.

The logo, wordmark, subtle particle wave and tagline are artwork. Status text and the white
bar are drawn live; the concept image's baked status and progress bar are never rendered.
The milestones are renderer preparation, scene initialization, system setup, scene setup,
and ready. These fractions describe completed stages, not estimated bytes or elapsed time.
The current synchronous stage can still block its window thread; expensive GPU scene work
must remain on that thread. There is no artificial minimum splash duration.

```csharp
new GameClient().Run(new MyScene(), new WindowOptions
{
    Title = "My game",
    Width = 1280,
    Height = 720
});
```

## Future updater integration

Supply `PrepareApplication` to do asynchronous application preparation before any scene
resources are created. The loader stays responsive while that callback runs on a worker.
Use the supplied token to cancel I/O when the window closes. Progress reports are thread-safe
and do not require a UI synchronization context. A null completion produces an animated
indeterminate bar; a finite value between 0 and 1 shows actual completion for that operation.

```csharp
new GameClient().Run(new MyScene(), new WindowOptions
{
    PrepareApplication = async (progress, cancellationToken) =>
    {
        progress.Report(new LoadingProgress("Checking for updates..."));
        // Application-owned service, not a built-in Radiant API:
        await updateService.CheckAsync(cancellationToken);
        progress.Report(new LoadingProgress("Update check complete", 1));
    }
});
```

The callback can similarly report download byte progress or asset preparation. Radiant does
not yet provide an update feed, downloader, package verification, installation, rollback or
restart protocol. No network requests or executable replacement happen by default. A future
application updater owns those policies and should return only when it is safe to start the game.
The callback must not touch the graphics device, scene, or ECS from its worker thread.

Uncaught callback or scene-loading exceptions stop startup, are written to stderr, and leave
“Startup failed. Press Escape to close.” on screen. Inspect `MonoGameWindow.StartupError` for the
exception. A hidden loader throws instead. Closing or Escape requests cancellation; callbacks
must honor it. Late progress reports after preparation ends are ignored.

## Custom windows and tests

`MonoGameWindow.IsLoading` stays true until startup completes (including a failed startup), and
`MonoGameWindow.LoadingProgress` exposes the current message and fraction. When overriding `Update`
or `Draw`, call the base implementation and return while `IsLoading` before accessing scene
resources. Timing is reset at handoff, so loading time does not become a simulation delta.

Set `ShowLoader = false` for frame-sensitive fixtures or hosts that supply their own UI. This
skips the visual stages and loads the scene synchronously once preparation finishes; it does
not skip the preparation callback. `HeadlessBackend` does not use the desktop loader or callback.

```sh
dotnet run --project tests/Radiant.Tests -c Release
dotnet run --project tests/Radiant.Scenes -c Release -- --startup-regression
```

The Vulkan regression covers live progress pixels, resizing, scene handoff, async preparation
failure, cancellation on close, and the hidden-loader path. It saves a real renderer capture
to `artifacts/startup/loader.png`.

## Artwork source

The approved design is `Content/branding/radiant-splash.png` (1536×1024), created with the
built-in image generator. Its exact generation prompt and reference names are preserved in
`Content/branding/radiant-splash.prompt.txt`. The rendering source region ends at y=870;
keep the logo and tagline above that line when revising the artwork. Status text starts at
(60, 900), and the progress track occupies (60, 944, 1416, 14) in design coordinates.

## About Radiant

**Help → About Radiant** (or `Inspector.ShowAbout()`) opens the same artwork as a centered
modal panel over the running engine. It has no title bar; a small X floats inside the upper
right corner and takes its normal, hover and text colors from the active inspector theme.
The footer displays the engine assembly version instead of fictitious loading progress.
Click X or press Escape to dismiss it. Scene updates pause while the panel is open, and
closing resets frame timing so the pause is not fed into simulation. Escape closes only the
panel; release and press it again to exit the application. The panel follows window resizing.
