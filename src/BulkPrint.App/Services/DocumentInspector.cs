using BulkPrint.App.Rendering;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Services;

/// <summary>Reads page counts and detects password-protected or unreadable files after import.</summary>
internal sealed class DocumentInspector : IDocumentInspector
{
    private readonly PageSourceFactory _sources;
    private readonly IAppLogger _log;

    public DocumentInspector(PageSourceFactory sources, IAppLogger log)
    {
        _sources = sources;
        _log = log;
    }

    public bool OfficeEngineAvailable => _sources.Office.IsAvailable;

    public async Task InspectAsync(DocumentItem item, GlobalPrintSettings global, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(item.Path))
        {
            Fail(item, "The file could not be found. It may have been moved or deleted.");
            return;
        }
        if (new FileInfo(item.Path).Length == 0)
        {
            Fail(item, "The file is empty.");
            return;
        }

        switch (item.Kind)
        {
            case DocumentKind.Pdf:
                try
                {
                    var doc = await PdfLoader.LoadAsync(item.Path, item.Password);
                    item.PageCount = (int)doc.PageCount;
                    if (item.PageCount == 0) Fail(item, "This PDF has no pages.");
                }
                catch (Exception ex) when (PdfLoader.IsWrongPassword(ex))
                {
                    item.IsPasswordProtected = true;
                    item.Status = PrintStatus.NeedsPassword;
                    item.StatusDetail = "This PDF is password-protected. Enter its password to print it.";
                }
                catch (Exception ex)
                {
                    _log.Warn($"PDF could not be opened '{item.Path}': {ex.Message}");
                    Fail(item, "This PDF could not be opened. It may be damaged or not a real PDF file.");
                }
                break;

            case DocumentKind.Image:
                await Task.Run(() =>
                {
                    try
                    {
                        using var src = new ImagePageSource(item.Path);
                        item.PageCount = src.PageCount;
                    }
                    catch (Exception ex)
                    {
                        Fail(item, PrintEngine.FriendlyError(ex));
                    }
                }, ct);
                break;

            case DocumentKind.Text:
            case DocumentKind.Csv:
                await Task.Run(() =>
                {
                    try
                    {
                        bool landscape = global.Orientation == OrientationMode.Landscape;
                        using var src = _sources.Open(item, global, landscape, null, ct);
                        item.PageCount = src.PageCount;
                    }
                    catch (Exception ex)
                    {
                        Fail(item, "This file could not be read: " + PrintEngine.FriendlyError(ex));
                    }
                }, ct);
                break;

            case DocumentKind.Word:
            case DocumentKind.Excel:
            case DocumentKind.PowerPoint:
                if (!_sources.Office.IsAvailable)
                {
                    item.Status = PrintStatus.Unsupported;
                    item.StatusDetail = "The built-in Office engine is not installed, so this file cannot be printed faithfully. It will be skipped.";
                }
                else if (item.PageCount is null)
                {
                    item.StatusDetail = "Pages are counted when the document is converted for preview or printing.";
                }
                break;

            default:
                item.Status = PrintStatus.Unsupported;
                item.StatusDetail = "This file type is not supported.";
                break;
        }
    }

    public async Task<bool> TryUnlockAsync(DocumentItem item, string password, CancellationToken ct)
    {
        if (item.Kind != DocumentKind.Pdf) return false;
        try
        {
            var doc = await PdfLoader.LoadAsync(item.Path, password);
            item.Password = password;
            item.PageCount = (int)doc.PageCount;
            item.Status = PrintStatus.Pending;
            item.StatusDetail = "Unlocked with password (kept in memory only)";
            return true;
        }
        catch (Exception ex) when (PdfLoader.IsWrongPassword(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Warn($"Unlock failed for '{item.Path}': {ex.Message}");
            return false;
        }
    }

    public void CleanUp() => _sources.Office.CleanUp();

    private static void Fail(DocumentItem item, string message)
    {
        item.Status = PrintStatus.Failed;
        item.StatusDetail = message;
    }
}
