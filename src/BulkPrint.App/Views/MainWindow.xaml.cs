using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BulkPrint.Core.Models;
using BulkPrint.Core.ViewModels;

namespace BulkPrint.App.Views;

public partial class MainWindow : Window
{
    private const string RowDragFormat = "BulkPrintManager.QueueRows";
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _statusTimer;
    private Point _dragStart;
    private DataGridRow? _pressedRow;
    private bool _deferSingleSelect;
    private bool _dragging;
    private bool _closingConfirmed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        _vm.SelectionRestoreRequested += RestoreSelection;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) =>
        {
            if (!_vm.IsPrinting) await _vm.RefreshPrinterStatusAsync();
        };
        _statusTimer.Start();
        Closing += OnClosing;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ----- Closing --------------------------------------------------------------------------
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (!_vm.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }
        _closingConfirmed = true;
        _statusTimer.Stop();
        _vm.Shutdown();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    // ----- Toolbar --------------------------------------------------------------------------
    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (SortButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = SortButton;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    // ----- Selection ------------------------------------------------------------------------
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SetSelection(QueueGrid.SelectedItems.OfType<DocumentItem>());
        _vm.SelectedDocument = QueueGrid.SelectedItem as DocumentItem;
    }

    private void RestoreSelection(IReadOnlyList<DocumentItem> items)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            QueueGrid.SelectedItems.Clear();
            foreach (var item in items) QueueGrid.SelectedItems.Add(item);
            if (items.Count > 0) QueueGrid.ScrollIntoView(items[0]);
        });
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is null) return;
        if (FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is { IsEditing: true }) return;
        if (_vm.PreviewCommand.CanExecute(null)) _vm.PreviewCommand.Execute(null);
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not DocumentItem item) return;
        // Let the binding write the new value first.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _vm.OnItemEdited(item));
    }

    // ----- Reordering by drag and drop -------------------------------------------------------
    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(QueueGrid);
        _pressedRow = null;
        _deferSingleSelect = false;
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ScrollBar>(source) is not null || FindAncestor<DataGridColumnHeader>(source) is not null) return;
        if (FindAncestor<DataGridCell>(source) is { IsEditing: true }) return;

        _pressedRow = FindAncestor<DataGridRow>(source);
        // Keep a multi-selection when the user starts dragging one of the selected rows.
        if (_pressedRow is { IsSelected: true } && QueueGrid.SelectedItems.Count > 1 &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            _deferSingleSelect = true;
            e.Handled = true;
        }
    }

    private void OnGridMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferSingleSelect && !_dragging && _pressedRow?.Item is DocumentItem item)
        {
            QueueGrid.SelectedItems.Clear();
            QueueGrid.SelectedItem = item;
        }
        _deferSingleSelect = false;
        _pressedRow = null;
    }

    private void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedRow is null || _dragging || !_vm.CanEditQueue) return;
        var pos = e.GetPosition(QueueGrid);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var items = QueueGrid.SelectedItems.OfType<DocumentItem>().ToList();
        if (_pressedRow.Item is DocumentItem pressed && !items.Contains(pressed)) items = new List<DocumentItem> { pressed };
        if (items.Count == 0) return;

        _dragging = true;
        try
        {
            var data = new DataObject(RowDragFormat, items);
            DragDrop.DoDragDrop(QueueGrid, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            _deferSingleSelect = false;
            _pressedRow = null;
        }
    }

    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(RowDragFormat))
            e.Effects = _vm.CanEditQueue ? DragDropEffects.Move : DragDropEffects.None;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = _vm.CanEditQueue ? DragDropEffects.Copy : DragDropEffects.None;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnGridDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(RowDragFormat) is List<DocumentItem> rows)
        {
            int target = _vm.Documents.Count;
            var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row is not null)
            {
                target = row.GetIndex();
                if (e.GetPosition(row).Y > row.ActualHeight / 2) target++;
            }
            _vm.MoveSelectedTo(rows, target);
            return;
        }
        await DropFilesAsync(e);
    }

    // ----- Files dropped from Explorer -------------------------------------------------------
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _vm.CanEditQueue ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        await DropFilesAsync(e);
    }

    private async Task DropFilesAsync(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            try
            {
                await _vm.AddPathsAsync(paths);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "BulkPrint Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
