# BulkPrint Manager

**Print Hundreds of Documents. One Simple Workflow.**

**Designed and Developed by Harish Vaishnav**

BulkPrint Manager is a Windows 10/11 (64-bit) desktop program that prints hundreds or thousands of
PDF, Word, Excel, PowerPoint, image and text files in one batch, without opening them one by one.
It works completely offline. The user does not need .NET, Microsoft Office, Adobe Reader or any other program.

---

## How to get `BulkPrintManager_Setup.exe` (no programming needed)

The installer is built by GitHub's free Windows build machines. You upload this folder once and
GitHub builds, installs and tests the program, then gives you the setup file to download.

1. **Create a free GitHub account** at <https://github.com/signup> (skip this if you have one).
2. **Create a repository:** click **+** (top right) > **New repository**. Name it `BulkPrintManager`.
   Choose **Public** (unlimited free build minutes) or **Private** (about 2,000 free minutes a month,
   and Windows builds count double). Click **Create repository**.
3. **Upload the files:** on the new repository page click **uploading an existing file**. Unzip
   `BulkPrintManager-source.zip` on your PC, open the `BulkPrintManager` folder, select **everything inside it**
   (including the `.github` folder) and drag it onto the page. Wait for the upload, then click **Commit changes**.
   - Check that the repository now shows a `.github` folder. If it does not, click **Add file > Create new file**,
     type `.github/workflows/build.yml` as the name, paste in the contents of that file from the zip, and commit.
4. **Watch the build:** open the **Actions** tab. A run called *Build BulkPrint Manager installer* starts by
   itself. It takes about 25 to 45 minutes (it downloads and packs the offline Office engine, builds the installer,
   then installs and tests it on a clean Windows machine).
5. **Download the installer:** open the finished run (green tick), scroll to **Artifacts**, and download
   **BulkPrintManager_Setup**. GitHub gives it to you as a zip. Inside is `BulkPrintManager_Setup.exe`.
   The **test-results** artifact holds the automatic test report (`installer-test.txt`), preview images of
   every sample file and the PDFs that were "printed" during the test.

**Optional, for a permanent download link:** on the repository page click **Releases > Create a new release**,
type a tag such as `v1.0.0`, and publish. The build runs again and attaches `BulkPrintManager_Setup.exe` to the release.

**If the build fails** (red cross), open the run, click the failed step, and copy the red error text. Send it
to whoever is maintaining the code (or to Claude) to fix.

### Building without the Office engine
Open **Actions > Build BulkPrint Manager installer > Run workflow** and untick *Bundle the offline Office engine*.
The installer is much smaller (roughly 50 to 80 MB instead of roughly 250 to 350 MB), but Word, Excel and PowerPoint files are then
marked *Cannot print* in the program.

### Windows SmartScreen
The installer is not code-signed unless you add a code-signing certificate. Without one, Windows may show
*"Windows protected your PC"* the first time. Click **More info > Run anyway**. To sign automatically, add two
repository secrets (**Settings > Secrets and variables > Actions**): `SIGNING_CERT_BASE64` (your .pfx file,
base64-encoded) and `SIGNING_CERT_PASSWORD`.

---

## Features

- **Import:** select many files, a whole folder (optionally with subfolders), or drag and drop. Right-click
  documents or folders in Windows Explorer and choose **Add to BulkPrint Manager** (Windows 11: under *Show more options*).
- **Queue:** handles 1,000+ documents; shows name, type, size, pages, copies and status. Remove, remove duplicates,
  clear, reorder by drag and drop or Move Up/Down, sort by name (natural order, so 2 comes before 10), folder, date, type or size.
- **Formats:** PDF (multi-page, large, password-protected), DOC, DOCX, RTF, XLS, XLSX, CSV, PPT, PPTX, JPG, JPEG,
  PNG, BMP, TIFF (multi-page) and TXT.
- **Printers:** finds all Windows printers, Windows default printer, live status (offline, out of paper, paper jam,
  door open, toner), paper size (A4, A3, A5, Letter, Legal and any size the driver offers), tray, colour or black
  and white, quality.
- **Print settings:** copies, collated or uncollated, all pages, page ranges (`1-3, 5, 8-`), odd or even pages,
  single or double-sided (long or short edge), portrait, landscape or automatic per page, fit to page,
  actual size, scale to fit paper. Global settings plus per-document overrides (marked with a gear icon).
- **Save print output (On/Off):** with *Microsoft Print to PDF* and other save-to-file printers, every document
  is saved automatically to a folder under its own name; no "Save As" box per document.
- **Print without pop-ups:** the whole batch runs in one go with no confirmation, questions or report window.
- **Batch control:** Print All, pause, resume, cancel, skip a document, retry failed documents, optional delay and
  separator page between documents, waits if the printer queue is full, pauses itself when the printer reports a problem.
- **Safe queue:** the queue is saved automatically. After a crash or restart, BulkPrint Manager offers to resume the unfinished batch.
- **Preview:** thumbnail of the selected document and a full preview with page navigation that shows the real
  paper size, orientation, scaling and the printer's printable area.
- **Reports:** summary after every batch (printed, skipped, failed with reasons, pages, time), export to CSV or PDF,
  and one click to print the failed documents again.
- **Profiles:** save and load named print profiles. Save and reopen print queues (`.bpq` files).
- **Other:** light and dark theme, keyboard shortcuts, high-DPI and screen-reader friendly, no internet access
  of any kind, no administrator rights needed to run. Log files: **Help > Open log folder**.

## How it works (technical summary)

| Part | Technology |
|---|---|
| App | C# / .NET 10, WPF (Windows 11 Fluent theme), self-contained single-file `.exe` (x64) |
| Printing | Windows print spooler through `System.Drawing.Printing`, one print job per document |
| PDF | `Windows.Data.Pdf`, the PDF engine built into Windows 10/11 (nothing extra to install) |
| Images, TXT, CSV | GDI+ (built into Windows) |
| Word, Excel, PowerPoint | Bundled LibreOffice engine (MPL-2.0), converted to PDF offline, one file at a time |
| Installer | Inno Setup 6, built and tested by GitHub Actions on `windows-2022` |

Project layout:

```
src/BulkPrint.Core        Platform-independent logic: queue engine, page ranges, print plans, settings, reports, view model
src/BulkPrint.App         Windows app: WPF windows, printing, rendering, Office conversion, single-instance, self-test
tests/BulkPrint.Core.SelfTest   140 automated checks of the core logic (runs on any OS)
tests/samples             Sample documents used by the installer test
installer/                Inno Setup script (publisher, credit, shortcuts, right-click menu, uninstaller)
build/                    Office engine download, installer test, signing and artwork scripts
.github/workflows/        The build pipeline
```

### Building on your own Windows PC (developers)
```
dotnet run --project tests/BulkPrint.Core.SelfTest -c Release
dotnet publish src/BulkPrint.App/BulkPrint.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o build/publish
copy THIRD-PARTY-NOTICES.txt build\publish\
copy docs\USER-GUIDE.md build\publish\
powershell -File build\fetch-libreoffice.ps1 -Destination build\publish\engine
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\BulkPrintManager.iss
```
Requires the .NET 10 SDK and Inno Setup 6. The setup file appears in `build\installer`.

### Self-test
`BulkPrintManager.exe --selftest <samples folder> <output folder>` checks the developer credit in the .exe
properties, opens and previews every sample, unlocks the password sample (`test1234`), confirms the damaged sample
is rejected, prints everything to *Microsoft Print to PDF* and writes `results.txt`.

## Known limitations

- Office documents are converted by LibreOffice, not Microsoft Office. Most print as expected, but complex
  layouts, uncommon fonts, macros or embedded objects can differ. Use Preview for important documents.
- Password-protected Word, Excel or PowerPoint files are not supported (they fail with a clear message).
  Password-protected PDFs are supported.
- Printer status (out of paper, jam, offline) depends on what the printer driver reports to Windows. Some drivers
  report nothing, in which case problems are not detected automatically.
- Pause takes effect after the document that is printing finishes.
- Duplex, colour, trays and quality are only offered when the printer driver supports them.
- Real paper output must be checked on real printers. The automated pipeline can only test printing to
  *Microsoft Print to PDF*.

## Licence

Copyright © 2026 Harish Vaishnav. All rights reserved.
Bundled third-party components and their licences are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
