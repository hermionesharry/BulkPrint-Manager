# BulkPrint Manager: Test Plan and Status

BulkPrint Manager, Designed and Developed by Harish Vaishnav.

This file says what is tested automatically, where, and what still has to be checked by a person with real
printers. Fill in the manual table and keep it with each release.

## 1. Status at hand-over (31 Oct 2026)

| What | Status |
|---|---|
| Core logic (queue engine, page ranges, copies/collation/duplex plans, sorting, duplicates, reordering, settings, profiles, crash-safe session, reports CSV/PDF, logging) | **Compiled and passed 140 automated checks** on .NET 10 (Linux build machine) |
| Windows app (WPF windows, printing, PDF/image/text/Office rendering, installer) | **Written, syntax-checked and cross-checked against the core library, not yet compiled.** It needs Windows reference packages, so the first real compile happens in the GitHub Actions build. |
| Installer, silent install/uninstall, publisher and credit checks, print-to-PDF of every format | **Automated in the GitHub pipeline**, which has not run yet. Check `installer-test.txt` in the *test-results* artifact after the first run. |
| Real paper printing (physical and network printers) | **Manual** (section 3). |

Nothing should be called "released" until the pipeline is green and section 3 is completed.

## 2. Automated checks

**`tests/BulkPrint.Core.SelfTest`** (runs first in every build):
credit text; page range parsing and selection; print plans for copies, collation, duplex padding and separators;
natural sorting; file types; text, tab and CSV formatting and pagination; folder import with subfolders,
unsupported and Office lock files; duplicates; drag reordering and move up/down; per-document overrides;
settings/profile/session round-trip (passwords never saved, interrupted documents re-queued, missing files flagged);
report counts, CSV escaping, PDF export; log rotation; the batch engine (skips already-printed and locked documents,
failure isolation, separator placement, pause/resume/cancel, skip current document, auto-pause on printer problem,
waiting for a full printer queue, progress and page estimates).

**`build/test-installer.ps1`** (runs on a clean `windows-2022` machine in GitHub Actions):
silent install; files, Start menu and desktop shortcuts; *Installed apps* publisher = Harish Vaishnav;
.exe Company/Copyright = Harish Vaishnav; right-click menu registration; creates DOC/XLS/PPT samples with the
bundled engine; runs `BulkPrintManager.exe --selftest`, which:
- checks the credit in the program and its .exe properties,
- opens, page-counts and previews every sample (PDF, protected PDF, DOCX, DOC, RTF, XLSX, XLS, CSV, PPTX, PPT,
  JPG with EXIF rotation, PNG, BMP, 2-page TIFF, TXT),
- rejects a wrong PDF password and accepts the right one; rejects the damaged PDF,
- prints every sample to *Microsoft Print to PDF*,
- runs a 2-document batch with a page range, 2 collated copies, odd pages and a file-name separator page,
  and exports the report;

then silent uninstall and checks that program files, shortcuts and right-click entries are removed.
The *test-results* artifact contains every preview image and printed PDF so a person can look at them.

## 3. Manual checks on real hardware

Run on a clean Windows 10 x64 PC **and** a clean Windows 11 x64 PC (no Office, no Acrobat, no developer tools), offline.

| # | Check | Win 10 | Win 11 |
|---|---|---|---|
| 1 | Install from `BulkPrintManager_Setup.exe` with the network cable unplugged / Wi-Fi off; program starts | | |
| 2 | Splash shows the credit; status bar shows "Designed and Developed by Harish Vaishnav"; Help > About shows version, build date, credit, copyright, licences | | |
| 3 | Installer welcome and finish pages show the credit; Settings > Apps lists publisher Harish Vaishnav; .exe Properties > Details show Company and Copyright | | |
| 4 | Add 1,000+ files (mixed types) by folder with subfolders; window stays responsive; page counts fill in | | |
| 5 | Drag and drop files and folders from Explorer; right-click > Add to BulkPrint Manager with 20 files selected | | |
| 6 | Reorder by dragging; sort by each option; remove duplicates; clear queue | | |
| 7 | Physical USB printer: PDF, DOCX, XLSX, PPTX, JPG, TXT print correctly | | |
| 8 | Network printer: same as 7 | | |
| 9 | Double-sided long edge and short edge; odd page count with 2 collated copies starts copy 2 on a new sheet | | |
| 10 | Colour vs black and white; A4, A3 (if supported), Letter, Legal; tray selection | | |
| 11 | Page ranges, odd only, even only; per-document copies and settings (gear icon) | | |
| 12 | Fit to page, actual size, scale to fit paper; automatic orientation on a mixed portrait/landscape PDF | | |
| 13 | Password-protected PDF: one password for several files | | |
| 14 | Unplug the printer or remove paper mid-batch: batch pauses with a message; Resume continues | | |
| 15 | Pause, resume, cancel; close the program mid-batch, reopen, resume offered and works | | |
| 16 | Report: counts correct; CSV and PDF export open correctly; Print failed documents again | | |
| 17 | Credit never appears on any printed page (including separator pages) | | |
| 18 | Dark theme; 125%, 150% and 200% display scaling; keyboard-only use; Narrator reads buttons | | |
| 19 | Install a newer version over the old one: settings and profiles kept | | |
| 20 | Uninstall: program, shortcuts and right-click menu removed; user documents untouched | | |
