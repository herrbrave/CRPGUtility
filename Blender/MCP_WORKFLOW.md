# Blender MCP workflow

How to drive a live, windowed Blender session from Claude Code via the Blender
Lab MCP add-on, and the gotchas discovered getting it to actually work
(Sunken Shrine of the Stag Lord session, Oct 2026). This is general-purpose —
not scoped to one scene.

## Setup (already done on this machine)

- **Add-on**: Blender Lab MCP — **not** the unrelated ahujasid/PyPI "Blender
  MCP" package. Auto-starts a server on `localhost:9876` inside Windows
  Blender once the add-on is active and Blender is running. Check the
  "BlenderMCP" sidebar panel in Blender if a connection fails — the server
  needs to actually be started there, not just installed.
- **Bridge repo**: `C:\Users\Peter\.hermes-tools\blender-mcp` (repo/tag:
  https://projects.blender.org/lab/blender_mcp, v1.0.3). Python project is
  under the repo's `mcp/` directory, not repo root.
- **Windows venv**: `C:\Users\Peter\.hermes-tools\blender-mcp-venv\Scripts\python.exe`
- **Helper scripts**: `C:\Users\Peter\.hermes-tools\test_blender_mcp.py`
  (read-only connectivity test), `C:\Users\Peter\.hermes-tools\blender_mcp_call.py`
  (general-purpose: call any tool with JSON args, or run a Python file via
  `execute_blender_code`).
- **Bridge entry point** (what the helper scripts launch):
  `from blmcp import main; main()`
- This session was **not** registered as a persistent MCP server in Claude
  Code's own config — it's invoked ad hoc via Bash, not as a native
  `mcp__*` tool. A session only sees natively-registered MCP servers at
  startup; having Blender open does not retroactively add one.

## Invoking it

This machine's Bash tool is **Git Bash, not WSL** — use `/c/Users/...`
paths, not `/mnt/c/Users/...` (WSL-style; resolves to nothing here).

```bash
# Connectivity test
"/c/Users/Peter/.hermes-tools/blender-mcp-venv/Scripts/python.exe" \
  "C:\Users\Peter\.hermes-tools\test_blender_mcp.py"

# Run a Python file inside the live Blender session
"/c/Users/Peter/.hermes-tools/blender-mcp-venv/Scripts/python.exe" \
  "C:\Users\Peter\.hermes-tools\blender_mcp_call.py" execute_blender_code \
  "C:\Users\Peter\.hermes-tools\some_script.py"

# Call any other tool with JSON args - use forward slashes in the JSON
# (backslashes get mangled by Git Bash's argv escaping into the Windows exe
# and cause `json.decoder.JSONDecodeError: Invalid \escape`)
"/c/Users/Peter/.hermes-tools/blender-mcp-venv/Scripts/python.exe" \
  "C:\Users\Peter\.hermes-tools\blender_mcp_call.py" render_viewport_to_path \
  '{"output_path": "C:/Users/Peter/.hermes-tools/out.png"}'
```

**Verify more than tool discovery.** A successful `list_tools` only proves
the stdio pipe works. Confirm Blender itself is actually connected by calling
`get_objects_summary` with `{}` and checking the reply has `isError: false`
*and* an inner `"status": "ok"` in the JSON payload.

## Available tools (26, as of this session)

- `execute_blender_code` / `execute_blender_code_for_cli` — run arbitrary
  Python in the live session (or a background process). This is the general-
  purpose one: full `bpy`/`bmesh` access, so geometry edits, material edits,
  anything.
- `get_objects_summary`, `get_object_detail_summary` — inspect scene state
  without writing a script.
- `get_blendfile_summary_*` (datablocks, missing files, linked libraries,
  path info, usage guess) — and `_for_cli` variants that open a file in a
  background process instead of using the live session.
- `render_viewport_to_path` — renders using the scene's **actual current
  render settings**. Use this for anything where materials/lighting/world
  need to be correct.
- `render_thumbnail_to_path` — explicitly **overrides render settings
  temporarily** for a fast preview. Looked identical across wildly different
  world/light edits in testing — don't use it to judge lighting or materials.
- `get_screenshot_of_area_as_image` / `get_screenshot_of_window_as_image` —
  captures the actual 3D viewport (solid/material preview shading),
  independent of scene lights entirely. Good for geometry sanity checks when
  scene lighting is mid-edit or broken.
- `get_screenshot_of_window_as_json` — window/area layout description.
- `jump_to_view3d_object_by_name` / `jump_to_view3d_object_data_by_name` —
  frame the viewport on an object. Prefer this over scripting
  `region_3d.view_distance` by hand (unreliable, see below).
- `jump_to_tab_by_name` / `jump_to_tab_by_space_type` — switch workspace tabs.
- `get_python_api_docs` / `search_api_docs` / `search_manual_docs` — look up
  Blender API/manual content instead of relying on memory (useful across
  Blender versions where the API has drifted).

## Gotchas (all confirmed this session)

1. **Render-cache staleness across repeated edit-then-render calls.** Editing
   scene/world/material properties via `execute_blender_code` correctly
   updates Blender's Python-level state — confirmed via
   `bpy.context.evaluated_depsgraph_get()` showing the right values — but
   Cycles' render engine can keep using a stale sync from earlier in the
   *same persistent session* and not reflect the edit, even across several
   separate edit-then-render tool calls. Diagnosed by loading the rendered
   PNG back with `bpy.data.images.load(...)` and reading `.pixels` directly —
   numeric pixel values are far more reliable than eyeballing a render for
   confirming whether something actually changed. No fully reliable in-session
   fix was found (`node_tree.update_tag()`, `view_layer.update()` helped
   sometimes, not always). When in doubt, verify via a fresh
   `blender --background` process instead of trusting repeated live renders.
2. **Output paths are sandboxed.** `render_thumbnail_to_path` and
   `render_viewport_to_path` both silently redirect output into
   `%LOCALAPPDATA%\Temp\blender_<session>\blender_mcp\<filename>` regardless
   of the `output_path` you pass. Always read the actual `filepath` the tool
   returns in its JSON result — don't assume the path you requested.
3. **Leftover state from earlier debugging bites later tests.** Objects you
   move/edit during interactive debugging (e.g. a camera repositioned to test
   something) stay moved for the rest of the session. Lost real time once to
   a "lighting bug" that was actually just a camera still pointed where an
   earlier unrelated test had left it. Explicitly re-set anything you depend
   on (camera transform, etc.) at the top of each check script rather than
   assuming prior state.
4. **Viewport navigation via scripted `view_distance` is unreliable.**
   Multiplying `region_3d.view_distance` after `view3d.view_selected()`
   over/undershot unpredictably. Prefer the dedicated `jump_to_view3d_object_by_name`
   tool, and expect to still need a manual follow-up zoom.
5. **Blender 5.2 API has drifted from 4.2** (the version this project's
   headless pipeline scripts target). Confirmed example:
   `scene.cycles.use_persistent_data` doesn't exist in 5.2's
   `CyclesRenderSettings`. Don't assume headless-script code ports to a live
   5.2 session unchanged — check with `get_python_api_docs`/`search_api_docs`
   if something errors with `AttributeError`.
6. **A silent coordinate bug is still your own bug.** MCP makes *noticing*
   mistakes fast (render, look, done in seconds) but doesn't prevent making
   them — e.g. a ring-loft helper that built vertex Y-coordinates without
   adding the object's Y-offset floated a mesh to the wrong place in world
   space with no error, just a render that was missing geometry. Live
   iteration shortens the loop from "wrong" to "caught," it doesn't replace
   checking the math.

## Recommended pattern

Use MCP as a **fast scratchpad**, not a replacement for the project's
scripted pipeline:

- Good fit: perceptual/visual debugging where blind numeric guessing is
  expensive (lighting, exposure, material look), and freeform/organic
  geometry iteration (sculana-adjacent work like the stag statue) where
  seeing the result after each change matters.
- Still port the final working code back into the numbered pipeline scripts
  once it's right. The live session's state isn't reproducible or version-
  controlled — only the scripts (and `layout.json`) are the source of truth.
  Every geometry change made live in this session was re-implemented in
  `05_set_dressing.py` and re-verified via a clean headless
  `01_blockout.py` → `08_procedural_materials.py` rebuild before being
  considered done.


## Animation authoring (Viking Raid bow draw, Oct 2026) -- additions

Pipeline for rigged-character animation (details in `Blender/Animations/Bow/`): import a rig FBX, place hands/feet with IK in real coordinates, bake to
plain FK keyframes, export a skeleton-only FBX with only the new actions, retarget in Unity as Humanoid. Gotchas, all confirmed:

1. **`execute_blender_code` requires `result` to be a dict** (a string is rejected with an error AFTER the code has run); `print` output comes back in `stdout`.
2. **Blender's IK drives the constrained bone's TAIL.** To put a WRIST on a target, constrain the FOREARM (chain 2) and give the hand a Copy Rotation
   (world space) from the same empty; constraining the hand bone puts the fingertips on the target (a 0.10 m "error").
3. **IK pole angle depends on the bone roll**: measure it once (search -180..180 in 15 deg steps for the solved elbow closest to the pole). A vague pole position
   is unreliable when the wrist is close to the shoulder (full draw ~0.2 m): solve the elbow analytically (two-bone, by direction hint or by height) and place the
   pole exactly on it; the fit residual then drops to ~0.
4. **`bpy.ops.nla.bake(visual_keying=True, bake_types={'POSE'}, use_current_action=False)`** works in the live session AND headless, and was exact (baked vs live IK
   0.0 m). Blender 5.x actions are layered: F-curves live under `action.layers[i].strips[j].channelbags[k].fcurves` (there is no `action.fcurves`).
5. **Headless: after removing objects, call `bpy.context.view_layer.update()` before iterating `view_layer.objects`**; stale entries come back as `None`.
6. **Workbench renders + numpy filmstrips** (load each render, concatenate, write via `bpy.data.images.new(...).pixels`) are the fastest pose-review loop and avoid the
   Cycles stale-render issue entirely. Orthographic front/side cameras make arm geometry obvious.
7. **The imported UAL armature object carries a 180 deg Z rotation**, and rig space is: forward = -Y, character's left = +X, up = +Z. The FBX round-trips with
   `axis_forward='-Z', axis_up='Y'`; in Unity the clip then needs a 180 deg root-rotation offset (Original orientation) -- measure it, do not assume (test: legs facing).
8. **Unity "Body Orientation" averages the torso into the root**: a deliberately twisted chest (archer, -45 deg) would rotate the whole character; use Original + offset.
9. **Calibrate props numerically**: in a PlayMode test at the hold pose, compute `Inverse(handRotation) * desiredWorldRotation` for the prop's local rotation instead of tuning by eye.

