using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DiagnosticFlashTool.App.ViewModels;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace DiagnosticFlashTool.App.Views;

public partial class FlowConfigView : UserControl
{
    private Point? _dragStartPoint;
    private FlowStepEditorRow? _draggedFlowStep;
    private DataGridRow? _dropTargetRow;
    private DropIndicatorAdorner? _dropIndicator;

    public FlowConfigView()
    {
        InitializeComponent();
    }

    private void FlowDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ClearDragState();
        if (!IsDragHandle(e.OriginalSource as DependencyObject)
            || FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not FlowStepEditorRow step)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(FlowDataGrid);
        _draggedFlowStep = step;
        FlowDataGrid.SelectedItem = step;
        e.Handled = true;
    }

    private void FlowDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ClearDragState();
    }

    private void FlowDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedFlowStep is null || _dragStartPoint is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearDragState();
            return;
        }

        var currentPoint = e.GetPosition(FlowDataGrid);
        var horizontalDistance = Math.Abs(currentPoint.X - _dragStartPoint.Value.X);
        var verticalDistance = Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y);
        if (horizontalDistance < SystemParameters.MinimumHorizontalDragDistance
            && verticalDistance < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var draggedStep = _draggedFlowStep;
        try
        {
            DragDrop.DoDragDrop(
                FlowDataGrid,
                new DataObject(typeof(FlowStepEditorRow), draggedStep),
                DragDropEffects.Move);
        }
        finally
        {
            ClearDragState();
        }
    }

    private void FlowDataGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(FlowStepEditorRow))
            || FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { Item: FlowStepEditorRow } targetRow)
        {
            e.Effects = DragDropEffects.None;
            ClearDropIndicator();
            e.Handled = true;
            return;
        }

        var insertAfter = e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2;
        ShowDropIndicator(targetRow, insertAfter);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void FlowDataGrid_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is DataGrid dataGrid && !dataGrid.IsMouseOver)
        {
            ClearDropIndicator();
        }
    }

    private void FlowDataGrid_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is not MainViewModel viewModel
                || e.Data.GetData(typeof(FlowStepEditorRow)) is not FlowStepEditorRow draggedStep
                || FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { Item: FlowStepEditorRow targetStep } targetRow)
            {
                return;
            }

            var sourceIndex = viewModel.FlowRows.IndexOf(draggedStep);
            var targetIndex = viewModel.FlowRows.IndexOf(targetStep);
            if (sourceIndex < 0 || targetIndex < 0 || ReferenceEquals(draggedStep, targetStep))
            {
                return;
            }

            if (e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2)
            {
                targetIndex++;
            }

            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            viewModel.MoveFlowStepToIndex(draggedStep, targetIndex);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        finally
        {
            ClearDropIndicator();
        }
    }

    private void ShowDropIndicator(DataGridRow row, bool insertAfter)
    {
        if (ReferenceEquals(_dropTargetRow, row) && _dropIndicator?.InsertAfter == insertAfter)
        {
            return;
        }

        ClearDropIndicator();
        var layer = AdornerLayer.GetAdornerLayer(row);
        if (layer is null)
        {
            return;
        }

        var sourceBrush = row.TryFindResource("PrimaryBrush") as Brush;
        var brush = sourceBrush?.CloneCurrentValue() ?? Brushes.DodgerBlue;
        _dropIndicator = new DropIndicatorAdorner(row, brush, insertAfter);
        _dropTargetRow = row;
        layer.Add(_dropIndicator);
    }

    private void ClearDragState()
    {
        _dragStartPoint = null;
        _draggedFlowStep = null;
        ClearDropIndicator();
    }

    private void ClearDropIndicator()
    {
        if (_dropIndicator is not null)
        {
            AdornerLayer.GetAdornerLayer(_dropIndicator.AdornedElement)?.Remove(_dropIndicator);
        }

        _dropIndicator = null;
        _dropTargetRow = null;
    }

    private static bool IsDragHandle(DependencyObject? element)
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is FrameworkElement { Tag: "FlowDragHandle" })
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is T parent)
            {
                return parent;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        return element switch
        {
            Visual => VisualTreeHelper.GetParent(element),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(element)
        };
    }

    private sealed class DropIndicatorAdorner : Adorner
    {
        private readonly Pen _pen;

        public DropIndicatorAdorner(UIElement adornedElement, Brush brush, bool insertAfter)
            : base(adornedElement)
        {
            InsertAfter = insertAfter;
            _pen = new Pen(brush, 2);
            _pen.Freeze();
            IsHitTestVisible = false;
        }

        public bool InsertAfter { get; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var y = InsertAfter ? ActualHeight - 1 : 1;
            drawingContext.DrawLine(_pen, new Point(0, y), new Point(ActualWidth, y));
        }
    }
}
