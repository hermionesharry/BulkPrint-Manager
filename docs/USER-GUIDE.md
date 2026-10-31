# BulkPrint Manager: User Guide

**Designed and Developed by Harish Vaishnav**

*Print Hundreds of Documents. One Simple Workflow.*

Version 1.0 · Windows 10 and Windows 11 (64-bit) · Works completely offline

---

## 1. Install

1. Double-click **BulkPrintManager_Setup.exe**.
2. If Windows shows *"Windows protected your PC"*, click **More info > Run anyway** (this appears for programs
   that are not code-signed).
3. Choose whether to install for all users (needs administrator approval) or only for you.
4. Optional: create a desktop shortcut, and add **Add to BulkPrint Manager** to the right-click menu in File Explorer.
5. Click **Install**, then **Finish**.

Nothing else is needed: no internet connection, no Microsoft Office, no Adobe Reader, no .NET.

## 2. The main window

| Area | What it does |
|---|---|
| Toolbar (top) | Add Files, Add Folder, Include subfolders, Remove, Remove Duplicates, Clear Queue, Sort, Move up/down, Load/Save Profile |
| Print queue (centre) | Every document waiting to print: name, type, size, pages, copies and status. A blue gear icon means the document has its own settings. |
| Settings panel (right) | Printer, paper, layout, pages, copies, sides, colour, quality and batch options. These are the **global** settings used by every document without its own settings. |
| Selected document (right, bottom) | A small preview of the selected document. |
| Print bar (bottom) | **Print All**, Pause/Resume, Cancel, progress and time left. |
| Status bar | Messages, queue summary and the program credit. |

## 3. Add documents

- **Add Files** (Ctrl+O): pick one or many files.
- **Add Folder** (Ctrl+Shift+O): adds every supported file in a folder. Tick **Include subfolders** to include
  the folders inside it too.
- **Drag and drop** files or folders from File Explorer onto the window.
- **From File Explorer:** right-click files or a folder > **Add to BulkPrint Manager**
  (Windows 11: right-click > *Show more options* first).

Supported: PDF, DOC, DOCX, RTF, XLS, XLSX, CSV, PPT, PPTX, JPG, JPEG, PNG, BMP, TIF, TIFF, TXT.
Other file types are skipped and reported in the status bar.

**Password-protected PDFs** are detected straight away. You are asked for the password once, and you can tick
*Also try this password for the other protected files*. Passwords are kept only while the program is open and
are never saved. To enter passwords later: **Settings > Unlock password-protected files**.

**Word, Excel and PowerPoint** files are converted to PDF on your computer by the built-in Office engine just
before printing or previewing. Their page count appears after the first conversion.

## 4. Organise the queue

- **Remove** (Delete key), **Remove Duplicates**, **Clear Queue**. Your files are never deleted.
- **Reorder:** drag rows up or down, or use **Move up / Move down** (Alt+Up / Alt+Down).
- **Sort:** by name A to Z or Z to A (numbers sort naturally: *Invoice 2* before *Invoice 10*), by folder,
  date modified, file type or size.
- **Copies for one document:** click the **Copies** cell and type a number.
- **Other settings for some documents:** select them and click **Document Settings** (or right-click >
  *Document settings…*). Choose copies, pages, orientation, scaling, sides and colour, then **Apply**.
  **Reset to global settings** removes them.

## 5. Choose printer and settings

- **Printer:** choose from the list, or **Use default printer**. **Refresh** (F5) looks for printers again. The dot
  shows the printer status: green for ready, red for a problem such as *Out of paper* or *Printer is offline*.
- **Paper size and source/tray:** the lists show what your printer supports (A4, A3, A5, Letter, Legal and others).
- **Orientation:** *Automatic* turns each page to match its shape (recommended), or force *Portrait* or *Landscape*.
- **Scaling:** *Fit to page* (fits inside the printable area), *Actual size* (100%), or *Scale to fit paper*
  (edge to edge; the printer's margins may trim the edges).
- **Copies** and **Collate** (1,2,3 1,2,3 instead of 1,1 2,2 3,3).
- **Pages to print:** all, a range such as `1-3, 5, 8-` (8- means page 8 to the end), odd pages or even pages.
- **Sides:** single-sided, or double-sided flipping on the long edge (normal) or short edge (for top-bound pages).
  Greyed out if the printer cannot print double-sided.
- **Colour or black and white**, and **print quality** (Draft prints faster).
- **Batch options:**
  - *Separator page*: a blank page, or a page showing the next file's name, between documents.
  - *Delay between documents*: gives slow printers a rest.
  - *Max jobs waiting at printer*: BulkPrint Manager waits when the printer already has this many jobs (default 20).
  - *Let the printer make the copies*: faster, but leave it off if your printer ignores copy settings.

- **Save print output (On/Off):** for printers that create files instead of paper, such as
  *Microsoft Print to PDF*. **On** (default): every document is saved automatically as its own file
  (for example `Invoice 12.docx` becomes `Invoice 12.pdf`) in the folder shown, by default
  *Documents\BulkPrint Output*. Existing files are never overwritten (`Invoice 12 (2).pdf`), and a CSV report
  is saved next to them. Windows does **not** ask "Save Print Output As" for each document. **Off**: Windows asks
  for a file name for every document. Paper printers are not affected. Use **Change folder…** and **Open folder**.
- **Print without pop-ups** (on by default): Print All starts straight away and the whole batch runs in one go.
  No confirmation, no questions and no report window. Password-protected PDFs that were not unlocked are skipped,
  and if the printer reports a problem the batch pauses and says so in the status bar. The report is always
  available from **Print > Show last report**. Turn this off (here or in the **Settings** menu) if you want to
  confirm each batch and see the report automatically.

**Profiles** save all of these settings (and the printer) under a name such as *A4 Duplex B&W*:
**Save Profile** and **Load Profile** on the toolbar. Load Profile also lets you delete profiles.

## 6. Preview

Select a document and click **Preview** (or double-click it). The preview shows the page on the chosen paper
size and orientation with the chosen scaling. The dashed line is the printer's printable area. Use the arrow
buttons or the Left/Right keys to move between the pages that will be printed.

## 7. Print

1. Click **Print All** (Ctrl+P) and confirm.
2. Each document is sent as its own print job. The status column shows *Converting*, *Rendering*, *Sending*,
   then *Printed*, *Skipped* or *Failed*. Hover over a status to see details.
3. **Pause** stops after the current document. **Resume** continues. **Cancel** stops; unprinted documents stay
   in the queue.
4. To skip documents, select them and click **Skip**. Skipping the document that is printing stops it.
5. If the printer reports a problem (out of paper, jam, offline, door open), the batch pauses itself and tells
   you. Fix the printer, then click **Resume**.

Documents that were already printed are not printed again when you click Print All. To print everything again:
**Print > Reset statuses**. To retry only failures: **Print > Retry failed documents**.

## 8. After printing: the report

A report opens after every batch: documents printed, failed and skipped, pages sent, printer, start and end time
and duration, and every failed document with its reason. You can:

- **Export CSV…** or **Export PDF…**
- **Print failed documents again**
- See it later with **Print > Show last report**

## 9. Your queue is safe

The queue is saved automatically. If the program or the computer stops during a batch, the next time you open
BulkPrint Manager it offers to resume the documents that were not printed yet. You can also save a queue to
reopen later: **File > Save queue** and **File > Open saved queue** (`.bpq` files).

## 10. Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+O | Add files |
| Ctrl+Shift+O | Add folder |
| Ctrl+P | Print all |
| Ctrl+S | Save queue |
| Delete | Remove selected |
| Alt+Up / Alt+Down | Move selected up / down |
| F5 | Refresh printers |
| F1 | About BulkPrint Manager |

## 11. Troubleshooting

| Problem | What to do |
|---|---|
| A document shows **Failed** | Hover over the status for the reason. Common reasons: the file was moved or deleted, it is open in another program, or it is damaged. |
| **Password needed** | Settings > Unlock password-protected files. |
| **Cannot print** on Word/Excel/PowerPoint files | The Office engine is not installed. Reinstall with the full installer. |
| An Office document looks different | Office files are converted by a LibreOffice-based engine, not Microsoft Office. Check with Preview. |
| Printing does not start | Check the printer status dot. Make sure the printer is on and not set to "Use printer offline" in Windows. |
| Double-sided is greyed out | The printer driver does not offer double-sided printing. |
| Anything else | Help > Open log folder, and send the latest `bulkprint.log` file to support. |

Settings, profiles and the saved queue are stored in `%AppData%\BulkPrintManager`. Logs and temporary files are
in `%LocalAppData%\BulkPrintManager`. Uninstalling keeps these so a reinstall keeps your settings; you can delete
these folders by hand.

## 12. About

**Help > About BulkPrint Manager** shows the version, build date, credit and the licences of the bundled
components.

BulkPrint Manager, Designed and Developed by Harish Vaishnav.
Copyright © 2026 Harish Vaishnav. All rights reserved.
