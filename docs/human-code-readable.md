# Human-code-readable

Status: approved. Apply these rules when reviewing or editing maintained source files.

## Purpose

Make code easy to scan by separating meaningful steps with whitespace while
keeping simple operations compact. Preserve behavior, statement order, names,
and existing APIs. `CornellBoxScene.cs` is the initial C# example.

## Rules

1. Use one blank line between logical steps: preparation, guards, input capture,
   state snapshots, actions, state persistence, and final updates.
2. Keep related operations together. Consecutive input bindings, assignments to
   one object, and registrations belonging to the same stage form a group.
   Do not insert a blank line after every statement.
3. Keep short guards and simple conditional actions on one line:
   `if (!Renderer.IsActive) return;` and
   `if (pressed) Pipeline.SelectNextBuffer();`.
4. Remove an unnecessary braced block around a single short conditional action
   when doing so preserves control flow and scope. Keep braces where they prevent
   ambiguity, especially around nested conditions or `else` branches.
5. A tiny guard that records state and returns may stay compact:
   `if (Inspector.IsEditingText) { PreviousKeyboard = keyboard; return; }`.
   Keep braces for multiple statements. Expand longer or more complex bodies.
6. Keep closely related, same-type state snapshots together when clear:
   `float previousAngle = Angle, previousDistance = Distance;`.
7. Separate groups of members by purpose, such as initial configuration, exposed
   systems, and private state. Keep one blank line between methods.
8. Preserve existing indentation and brace placement for expanded blocks.
   Remove trailing whitespace and avoid consecutive blank lines.
9. Use comments to explain intent or constraints. Do not add comments that merely
   repeat the next statement or label every whitespace-separated group.
10. Keep this pass focused on readability. Do not rename symbols, extract methods,
    reorder work, change expressions, or fix unrelated behavior as part of linting.

## Example

```csharp
Meshes.Resolution = Upscaler.ResolveViewSize(Meshes.Resolution);

if (!Renderer.IsActive) return;

var keyboard = Keyboard.GetState();

if (Inspector.IsEditingText) { PreviousKeyboard = keyboard; return; }

float previousAngle = Angle, previousDistance = Distance;

if (keyboard.IsKeyDown(Keys.Left)) Angle = MathHelper.Max(-0.22f, Angle - DeltaTime * 0.3f);
if (keyboard.IsKeyDown(Keys.Right)) Angle = MathHelper.Min(0.22f, Angle + DeltaTime * 0.3f);
if (keyboard.IsKeyDown(Keys.Tab) && PreviousKeyboard.IsKeyUp(Keys.Tab)) Pipeline.SelectNextBuffer();

PreviousKeyboard = keyboard;

if ((Angle != previousAngle || Distance != previousDistance) && Meshes.CameraEntity == Camera) UpdateCamera();
```

## Repository lint review

- Review maintained source files using these rules. Respect each language's syntax;
  the inline conditional examples above are C# conventions.
- Exclude generated code, vendored dependencies, build output, and binary assets.
- Use human judgment for logical grouping and line length. These rules do not
  impose a fixed column limit or require every block to fit on one line.
- Report each finding with its file, line, rule, and proposed change. Review
  ambiguous cases instead of mechanically rewriting them.
- Preserve existing local changes. Check the diff for unintended behavior changes,
  whitespace errors, and altered control flow. Run relevant checks if a change
  goes beyond whitespace or a clearly equivalent single-statement brace edit.

This document is a review guide, not an installed formatter or executable linter.

## C# hover documentation

- Give each class and method one concise XML summary describing its purpose or
  contract. Mention a non-obvious constraint or side effect when it matters.
- Use `/// <inheritdoc/>` for overrides that keep the documented base contract.
  Document a partial class once, at its main declaration.
- Preserve useful existing documentation. Promote an introductory comment instead
  of repeating it, and avoid boilerplate parameter lists or obvious field comments.
- Emit the XML documentation alongside the assembly so DLL consumers retain
  IntelliSense descriptions.
