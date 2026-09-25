using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using slider.Models;

namespace slider;

public partial class MainWindow
{
    private PlaylistPeriod? periodDragCandidate;
    private ListBoxItem? periodDragContainer;
    private Point periodDragStart;
    private bool periodDragging;
    private int periodInsertionIndex;
    private PeriodInsertionAdorner? periodInsertionAdorner;

    private static bool IsPeriodInteractiveElement(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem)
        {
            if (source is ButtonBase or TextBoxBase or ScrollBar) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void Periods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (periodDragging) return;
        periodDragCandidate = null;
        if (e.ClickCount != 1 || IsPeriodInteractiveElement(e.OriginalSource as DependencyObject)) return;
        var item = ItemsControl.ContainerFromElement(DaysListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is not PlaylistPeriod period) return;
        periodDragCandidate = period;
        periodDragContainer = item;
        periodDragStart = e.GetPosition(DaysListBox);
        // Leave the event unhandled: a normal click still selects the item.
    }

    private void Periods_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (periodDragCandidate == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPeriodDrag(); return; }
        var point = e.GetPosition(DaysListBox);
        if (!periodDragging)
        {
            if (Math.Abs(point.X - periodDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - periodDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!DaysListBox.CaptureMouse()) { EndPeriodDrag(); return; }
            periodDragging = true;
            periodDragContainer!.Opacity = 0.45;
            DaysListBox.Cursor = Cursors.Hand;
            periodInsertionAdorner = new PeriodInsertionAdorner(DaysListBox);
            AdornerLayer.GetAdornerLayer(DaysListBox)?.Add(periodInsertionAdorner);
        }
        var viewer = FindPeriodScrollViewer(DaysListBox);
        if (viewer != null)
        {
            if (point.Y < 20) viewer.LineUp();
            else if (point.Y > DaysListBox.ActualHeight - 20) viewer.LineDown();
            DaysListBox.UpdateLayout();
        }
        UpdatePeriodInsertion(point);
        e.Handled = true;
    }

    private void UpdatePeriodInsertion(Point point)
    {
        periodInsertionIndex = 0;
        double lineY = 0;
        for (int i = 0; i < DaysListBox.Items.Count; i++)
        {
            if (DaysListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            // Hit-test against layout positions, not animated positions, to avoid jitter.
            var top = item.TranslatePoint(new Point(), DaysListBox).Y
                - (item.RenderTransform is TranslateTransform translation ? translation.Y : 0);
            if (point.Y < top + item.ActualHeight / 2)
            {
                periodInsertionIndex = i;
                lineY = top;
                break;
            }
            periodInsertionIndex = i + 1;
            lineY = top + item.ActualHeight + 4;
        }
        AnimatePeriodInsertionGap();
        if (periodInsertionAdorner != null)
        {
            periodInsertionAdorner.LineY = Math.Clamp(lineY, 2, Math.Max(2, DaysListBox.ActualHeight - 2));
            periodInsertionAdorner.InvalidateVisual();
        }
    }

    private void AnimatePeriodInsertionGap()
    {
        if (!periodDragging || periodDragCandidate == null) return;
        int sourceIndex = periods.IndexOf(periodDragCandidate);
        bool showGap = periodInsertionIndex != sourceIndex && periodInsertionIndex != sourceIndex + 1;
        for (int i = 0; i < DaysListBox.Items.Count; i++)
        {
            if (DaysListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            double offset = showGap && i != sourceIndex ? (i < periodInsertionIndex ? -6 : 6) : 0;
            AnimatePeriodOffset(item, offset);
        }
    }

    private static void AnimatePeriodOffset(ListBoxItem item, double target)
    {
        if (item.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            item.RenderTransform = transform;
        }
        // Base value stores the destination; repeated mouse moves must not restart the animation.
        if ((double)transform.GetAnimationBaseValue(TranslateTransform.YProperty) == target) return;
        double current = transform.Y;
        transform.Y = target;
        transform.BeginAnimation(TranslateTransform.YProperty, SystemParameters.ClientAreaAnimation
            ? new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            }
            : null);
    }

    private void Periods_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!periodDragging) { EndPeriodDrag(); return; }
        var point = e.GetPosition(DaysListBox);
        var period = periodDragCandidate;
        bool inside = new Rect(DaysListBox.RenderSize).Contains(point);
        UpdatePeriodInsertion(point);
        int insertion = periodInsertionIndex;
        EndPeriodDrag();
        if (inside && period != null) MovePeriod(period, insertion);
        e.Handled = true;
    }

    private void MovePeriod(PlaylistPeriod period, int insertion)
    {
        int oldIndex = periods.IndexOf(period);
        if (oldIndex < 0) return;
        int newIndex = Math.Clamp(insertion, 0, periods.Count);
        if (newIndex > oldIndex) newIndex--;
        var previousPositions = new Dictionary<PlaylistPeriod, double>();
        if (newIndex != oldIndex && SystemParameters.ClientAreaAnimation)
        {
            for (int i = 0; i < DaysListBox.Items.Count; i++)
            {
                if (DaysListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item &&
                    item.DataContext is PlaylistPeriod itemPeriod)
                    previousPositions[itemPeriod] = item.TranslatePoint(new Point(), DaysListBox).Y;
            }
        }
        if (newIndex != oldIndex)
        {
            periods.RemoveAt(oldIndex);
            periods.Insert(newIndex, period);
        }
        selectedPeriod = period;
        RefreshPeriodsList();
        DaysListBox.ScrollIntoView(period);
        DaysListBox.UpdateLayout();
        foreach (var entry in previousPositions)
        {
            if (DaysListBox.ItemContainerGenerator.ContainerFromItem(entry.Key) is not ListBoxItem item) continue;
            double offset = entry.Value - item.TranslatePoint(new Point(), DaysListBox).Y;
            if (Math.Abs(offset) < 0.5) continue;
            var transform = new TranslateTransform();
            item.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
        if (newIndex != oldIndex) ScheduleAutoSave();
    }

    private void EndPeriodDrag()
    {
        if (periodDragging)
        {
            for (int i = 0; i < DaysListBox.Items.Count; i++)
                if (DaysListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                    AnimatePeriodOffset(item, 0);
        }
        periodDragging = false;
        periodDragCandidate = null;
        if (periodDragContainer != null) periodDragContainer.ClearValue(OpacityProperty);
        periodDragContainer = null;
        if (periodInsertionAdorner != null)
            AdornerLayer.GetAdornerLayer(DaysListBox)?.Remove(periodInsertionAdorner);
        periodInsertionAdorner = null;
        DaysListBox.ClearValue(CursorProperty);
        if (DaysListBox.IsMouseCaptured) DaysListBox.ReleaseMouseCapture();
    }

    private void Periods_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (periodDragging) EndPeriodDrag();
    }

    private void Periods_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (periodDragging && e.Key == Key.Escape) { EndPeriodDrag(); e.Handled = true; }
    }

    private static ScrollViewer? FindPeriodScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindPeriodScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private sealed class PeriodInsertionAdorner : Adorner
    {
        public double LineY { get; set; }
        public PeriodInsertionAdorner(UIElement element) : base(element) { IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(170, 170, 170)), 2);
            drawingContext.DrawLine(pen, new Point(4, LineY), new Point(Math.Max(4, ActualWidth - 16), LineY));
        }
    }
}
