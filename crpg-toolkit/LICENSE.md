# CRPG Toolkit — License Status

**`CRPG Toolkit Runtime`'s own license terms are not yet finalized.** This file exists so `package.json`'s `"license": "SEE LICENSE IN LICENSE.md"` field resolves to the truth (pending) rather than a broken reference — it is not a claim that terms have been decided, and no license text is invented here. The actual decision (license model, Asset Store terms, final wording) is tracked at the repository root in `Licenses/TODO.md` and needs the toolkit author's own business/legal input.

## Third-party content shipped in this package

This package includes SRD 5.1 content under the **Open Game License 1.0a**, physically separated from the toolkit's own runtime code:

- [`Licenses/OGL-1.0a.txt`](Licenses/OGL-1.0a.txt) — the Open Game License 1.0a, verbatim.
- [`Licenses/SRD5-Section15.txt`](Licenses/SRD5-Section15.txt) — the required Section 15 copyright notice chain, verbatim.

Per OGL §6, when this toolkit's own SRD5 module actually ships to end users, `SRD5-Section15.txt` needs one more paragraph appended naming this toolkit's own copyright holder — see `Licenses/TODO.md` at the repo root for that still-open item.

## Where this stands

- **Not resolved by this file**: the toolkit's own EULA/license text, Asset Store legal review, the SRD5 Section 15 copyright-addendum name/company/year, and a trademark/branding check. All four remain open, tracked at `Licenses/TODO.md`, and need the user's own input — nothing here should be read as closing them.
- **What this file does close**: the technical defect of `package.json` pointing at a license file that didn't exist, and the OGL/SRD5 notices living outside the package's own shipped boundary. Neither of those is a business decision.

This package is **not** release-ready or redistribution-cleared on the basis of this file alone.
