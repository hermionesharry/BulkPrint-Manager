using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Views;

public partial class PreviewWindow : Window
{
    private readonly IPreviewService _preview;
    private readonly DocumentItem _item;
    private readonly DocumentPrintOptions _options;
    private readonly GlobalPrintSettings _global;
    private CancellationTokenSource? _cts;
    private int _sheet = 1;
    private int _count;

    internal PreviewWindow(IPreviewService preview, DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global)
    {
        InitializeComponent();
        _preview = preview;
        _item = item;
        _options = options;
        _global = global;
        FileText.Text = item.FileName;
        Title = $"Print preview - {item.FileName}";
        Loaded += async (_, _) => await ShowSheetAsync(1);
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task ShowSheetAsync(int number)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        BusyPanel.Visibility = Visibility.Visible;
        BusyText.Text = FileTypes.NeedsOfficeEngine(_item.Kind) && _item.ConvertedPdfPath is null
            ? "Converting the document with the built-in Office engine…"
            : "Preparing preview…";
        ErrorText.Visibility = Visibility.Collapsed;
        PrevButton.IsEnabled = NextButton.IsEnabled = false;

        // Render with enough pixels to stay sharp on high-DPI screens.
        var dpi = VisualTreeHelper.GetDpi(this);
        double ppi = Math.Clamp(110 * dpi.DpiScaleY, 90, 240);

        try
        {
            var result = await _preview.RenderAsync(_item, _options, _global, number, ppi, allowConversion: true, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (result.Error is not null)
            {
                PageImage.Source = null;
                ErrorText.Text = result.Error;
                ErrorText.Visibility = Visibility.Visible;
                PageText.Text = "";
                DescriptionText.Text = "";
                return;
            }
            PageImage.Source = result.Image as ImageSource;
            _sheet = result.SheetNumber;
            _count = result.SheetCount;
            PageText.Text = $"Page {_sheet} of {_count}";
            DescriptionText.Text = result.Description;
            PrevButton.IsEnabled = _sheet > 1;
            NextButton.IsEnabled = _sheet < _count;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PageImage.Source = null;
            ErrorText.Text = "This document could not be previewed: " + PrintEngine.FriendlyError(ex);
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!cts.IsCancellationRequested) BusyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (_sheet > 1) await ShowSheetAsync(_sheet - 1);
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_sheet < _count) await ShowSheetAsync(_sheet + 1);
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.PageUp && _sheet > 1)
        {
            e.Handled = true;
            await ShowSheetAsync(_sheet - 1);
        }
        else if (e.Key is Key.Right or Key.PageDown && _sheet < _count)
        {
            e.Handled = true;
            await ShowSheetAsync(_sheet + 1);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
