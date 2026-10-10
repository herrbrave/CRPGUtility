"""Run 01, 02, and check_views.py in one fresh headless process - bypasses
any live-session render staleness for trustworthy verification renders."""
import os
BASE = r"C:\Users\Peter\wkspaces\CRPGUnity\Blender\Characters\Goblin1\scripts"
for fname in ("01_body_base.py", "02_face_details.py", "check_views.py"):
    path = os.path.join(BASE, fname)
    print(f"--- running {fname} ---")
    exec(compile(open(path).read(), path, "exec"), {"__name__": "__main__"})
