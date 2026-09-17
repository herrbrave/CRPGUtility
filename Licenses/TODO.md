# Legal / licensing — follow up before release

Per spec §62, the shipped package needs this structure -- **now the actual layout, moved inside the package boundary in Task 5.3** (previously these two files sat at the repo root, outside `crpg-toolkit/`, meaning a real UPM/git-package install would never have shipped them at all -- a real technical defect, independent of the still-open business items below):

```
crpg-toolkit/
 ├ LICENSE.md                          (new in Task 5.3 -- states plainly that terms are pending; makes
 │                                       package.json's "SEE LICENSE IN LICENSE.md" resolve to the truth
 │                                       instead of a missing file)
 └ Licenses/
    ├ CRPG-Toolkit-License.txt         (the toolkit's own EULA/license — pending Asset Store review, NOT written yet)
    ├ OGL-1.0a.txt                     (done — verbatim, see below)
    └ SRD5-Section15.txt               (done — verbatim, see below)
```

**Done (2026-09-07):** `OGL-1.0a.txt` and `SRD5-Section15.txt` were transcribed from the official PDF (https://media.dndbeyond.com/compendium-images/srd/5.1/SRD-OGL_V5.1.pdf, pages 1-2) using `pdftotext -raw` — the raw content-stream order, not the `-layout` mode, because this PDF's two-column layout makes `-layout` interleave text from the wrong column. Verified byte-for-byte against the extracted PDF text (whitespace differences only — page-footer boilerplate stripped, mid-word line-wraps rejoined into paragraphs, and a font-rendering artifact in the source PDF that doubles some hyphens — e.g. "royalty--free" — normalized back to single hyphens); no wording, punctuation, or section numbering was altered.

**Done (Task 5.3):** moved both files from repo-root `Licenses/` into `crpg-toolkit/Licenses/` (their content unchanged) and added `crpg-toolkit/LICENSE.md`. This closes the packaging defect only -- none of the business items below are resolved by it, and the package is still not release-ready or redistribution-cleared on this basis alone.

**Still open (needs the user, not something to guess at):**
- `CRPG-Toolkit-License.txt` — the toolkit's own EULA/license text. This is a business decision (what terms to offer Asset Store buyers), not something to draft speculatively.
- Per OGL §6 ("Notice of License Copyright"), **when SRD5 content actually ships, `SRD5-Section15.txt` needs a new paragraph appended** naming this toolkit's own SRD5 module, its copyright year, and its copyright holder's name (e.g. "CRPG Toolkit SRD5 Rules Content Copyright 2026, [name/company].") — every contributor who redistributes OGC must add their own line to the Section 15 chain. Needs the user's chosen name/company and ship year; not fabricated here.
- Final Asset Store licensing arrangement for `CRPG Toolkit Runtime` itself — needs legal review, separate from the OGL content.
- Confirm package branding/naming doesn't use Wizards' protected product names or logos.
- Keep `CRPG.Rules.SRD5` content physically separate from `CRPG Toolkit Runtime` licensing so the two can carry different license terms.

**Unrelated to the package itself, noted here only for discoverability:** `ConsumerGame/` (Task 5.2's Gate D consumer project) uses Fate Condensed-derived mechanics under a separate CC-BY 3.0 license -- see `ConsumerGame/Licenses/Fate-Condensed-CC-BY.txt`. This is the consumer project's own licensing concern, not the crpg-toolkit package's.
