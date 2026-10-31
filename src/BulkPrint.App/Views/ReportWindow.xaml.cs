using System.Windows;
using BulkPrint.Core;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;
using Microsoft.Win32;

namespace BulkPrint.App.Views;

public partial class ReportWindow : Window
{
    private readonly BatchReport _report;

    internal ReportWindow(BatchReport report)
    {
        InitializeComponent();
        _report = report;
        HeadlineText.Text = report.Headline;
        PrintedText.Text = report.Printed.ToString("N0");
        FailedText.Text = report.Failed.ToString("N0");
        SkippedText.Text = report.Skipped.ToString("N0");
        PagesText.Text = report.TotalPages.ToString("N0");
        DetailsText.Text = $"Printer: {report.PrinterName}   ·   {report.StartTime:dd MMM yyyy HH:mm} to {report.EndTime:HH:mm}   ·   Duration {report.DurationText}";
        // Failed documents first so they are easy to find.
        EntriesGrid.ItemsSource = report.Entries
            .OrderBy(e => e.Status == PrintStatus.Failed ? 0 : e.Status == PrintStatus.Printed ? 2 : 1)
            .ToList();
        RetryButton.Visibility = report.Failed > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool RetryRequested { get; private set; }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"Print report {_report.StartTime:yyyy-MM-dd HHmm}.csv",
        };
        if (dialog.ShowDialog(this) != true) return;
        Save(() => _report.SaveCsv(dialog.FileName), dialog.FileName);
    }

    private void OnExportPdf(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF file (*.pdf)|*.pdf",
            FileName = $"Print report {_report.StartTime:yyyy-MM-dd HHmm}.pdf",
        };
        if (dialog.ShowDialog(this) != true) return;
        Save(() => _report.SavePdf(dialog.FileName), dialog.FileName);
    }

    private void Save(Action save, string path)
    {
        try
        {
            save();
            MessageBox.Show(this, $"Report saved:\n{path}", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The report could not be saved: " + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        RetryRequested = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
