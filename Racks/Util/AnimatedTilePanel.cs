#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
// Disambiguate against System.Windows.Forms / System.Drawing — the project uses
// UseWindowsForms=true so the bare names collide.
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;

namespace Racks.Util
{
    // Phone-style animated wrap panel. Lays children out in a grid, and when the
    // user picks one up and drags it, the other tiles animate aside to make room.
    // Approach is based on the standard WPF pattern: arrange every child at (0,0)
    // and use a per-child TranslateTransform to position it; animate that transform
    // when the layout changes (reflow), and update it directly without animation
    // for the floating item that's tracking the cursor.
    //
    // Drag activation uses the system drag threshold rather than a long-press —
    // that way click/double-click/right-click on items behave exactly as before
    // and only a real drag triggers reorder.
    public class AnimatedTilePanel : Panel
    {
        // Per-child transform layout: a TransformGroup with one TranslateTransform
        // (for slot position) and one ScaleTransform (for the "lift" effect).
        private const int TranslateIndex = 0;
        private const int ScaleIndex = 1;

        // Tuning knobs. Kept inline rather than as DPs because they aren't meant
        // to be skinned — they're the feel of the gesture.
        private const int ReflowMs = 180;
        private const int LiftMs = 120;
        private const int DropMs = 200;
        private const double LiftScale = 1.06;
        private const double LiftOpacity = 0.92;

        private int _columns;
        private int _rows;
        private Size _measuredSize;
        private double _slotHeight = 1;
        private bool _hasArrangedOnce;

        // Drag state. Null when no drag in progress.
        private UIElement _draggedChild;
        private int _draggedIndexAtStart;
        private int _draggedIndexCurrent;
        private Point _pressOriginPanel;     // panel-coords of the press
        private Point _draggedSlotOrigin;    // slot position of the dragged item at pickup
        private double _draggedX;            // current translate of dragged item
        private double _draggedY;
        private bool _isDragging;
        private HashSet<UIElement> _knownChildren = new HashSet<UIElement>();
        // Hard-coded dimensions. You could make these DPs in the future.
        private bool _pressArmed;            // true between MouseDown and either reorder-start or release

        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(AnimatedTilePanel),
                new FrameworkPropertyMetadata(double.NaN,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(AnimatedTilePanel),
                new FrameworkPropertyMetadata(double.NaN,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        // Fires synchronously each time the dragged tile crosses into a new slot.
        // Host MUST move the corresponding item in its source ObservableCollection
        // (e.g., ObservableCollection.Move(from, to)) so the ItemsControl's
        // container generator updates the panel's children to match. We don't
        // mutate InternalChildren ourselves — that breaks the generator.
        public event Action<int, int> ItemMoveRequested;

        // Fires once at the end of a drag, after the drop animation begins. Use
        // this to persist the new order (e.g., write Instance.CustomOrderFiles).
        public event Action DragCompleted;

        // Fires when the user presses Shift and drags a tile past the system
        // drag threshold. Intended for outgoing OLE drag — the host should call
        // DragDrop.DoDragDrop(...) so the file can be dropped into Explorer or
        // another rack. Plain drag → reorder; Shift+drag → export.
        public event Action<UIElement> OutgoingDragRequested;

        public AnimatedTilePanel()
        {
            // MouseMove with handledEventsToo so we still see moves even if a child
            // marked the event handled (e.g., a selection handler).
            AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(OnPanelMouseMove), handledEventsToo: true);
            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPanelPreviewMouseDown), handledEventsToo: true);
            AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPanelMouseUp), handledEventsToo: true);
            LostMouseCapture += (_, _) => CancelOrFinishDrag(commit: true);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double itemW = SafeItemWidth();
            bool hasExplicitHeight = !double.IsNaN(ItemHeight) && ItemHeight > 0;
            double slotHeight = hasExplicitHeight ? ItemHeight : double.PositiveInfinity;

            int count = InternalChildren.Count;
            double maxDesiredHeight = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(itemW, slotHeight));
                if (!hasExplicitHeight && child.DesiredSize.Height > maxDesiredHeight)
                    maxDesiredHeight = child.DesiredSize.Height;
            }

            // Cache the height we'll use everywhere else this pass (Arrange + slot math).
            _slotHeight = hasExplicitHeight ? ItemHeight : Math.Max(1, maxDesiredHeight);

            if (count == 0) { _measuredSize = new Size(0, 0); return _measuredSize; }

            double width = double.IsInfinity(availableSize.Width) ? itemW : availableSize.Width;
            _columns = Math.Max(1, (int)Math.Floor(width / itemW));
            _rows = (int)Math.Ceiling(count / (double)_columns);

            _measuredSize = new Size(_columns * itemW, _rows * _slotHeight);
            return _measuredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var slot = new Size(SafeItemWidth(), _slotHeight);

            // Clean up removed children
            _knownChildren.RemoveWhere(c => !InternalChildren.Contains(c));

            int newChildIndex = 0;
            foreach (UIElement child in InternalChildren)
            {
                EnsureTransforms(child);
                child.Arrange(new Rect(new Point(0, 0), slot));

                if (!_knownChildren.Contains(child))
                {
                    _knownChildren.Add(child);
                    var scale = (ScaleTransform)((TransformGroup)child.RenderTransform).Children[ScaleIndex];
                    
                    // Initial state for pop-in animation
                    scale.ScaleX = 0;
                    scale.ScaleY = 0;

                    // Animate to 1 with a slight delay based on index for a cascade effect
                    var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(400))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(newChildIndex * 30), // staggered
                        EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                    newChildIndex++;
                }
            }

            ReflowAll(animate: _hasArrangedOnce);
            _hasArrangedOnce = true;
            return _measuredSize.Width > 0 ? _measuredSize : finalSize;
        }

        private double SafeItemWidth()
        {
            double w = ItemWidth;
            if (double.IsNaN(w) || w <= 0)
            {
                // Fall back to first child's desired width so the panel still works
                // before its ItemWidth is bound (and matches WrapPanel behavior).
                foreach (UIElement c in InternalChildren) return Math.Max(1, c.DesiredSize.Width);
                return 1;
            }
            return w;
        }

        private static void EnsureTransforms(UIElement child)
        {
            if (child.RenderTransform is TransformGroup g && g.Children.Count >= 2) return;
            child.RenderTransformOrigin = new Point(0.5, 0.5);
            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform());
            group.Children.Add(new ScaleTransform(1, 1));
            child.RenderTransform = group;
        }

        // Position every non-dragged child at its natural slot. If `animate` is true,
        // tween the TranslateTransform; otherwise snap. Called from ArrangeOverride
        // and from SwapElement when the drop index changes.
        private void ReflowAll(bool animate)
        {
            double itemW = SafeItemWidth();
            int i = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (!ReferenceEquals(child, _draggedChild))
                {
                    int col = i % _columns;
                    int row = i / _columns;
                    AnimateTo(child, col * itemW, row * _slotHeight, animate ? ReflowMs : 0);
                }
                i++;
            }
        }

        private static void AnimateTo(UIElement child, double x, double y, int durationMs)
        {
            EnsureTransforms(child);
            var translate = (TranslateTransform)((TransformGroup)child.RenderTransform).Children[TranslateIndex];
            if (durationMs <= 0)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.X = x;
                translate.Y = y;
                return;
            }
            translate.BeginAnimation(TranslateTransform.XProperty, MakeAnim(x, durationMs));
            translate.BeginAnimation(TranslateTransform.YProperty, MakeAnim(y, durationMs));
        }

        private static DoubleAnimation MakeAnim(double to, int durationMs, IEasingFunction easing = null)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs));
            if (easing != null)
            {
                anim.EasingFunction = easing;
            }
            else
            {
                anim.AccelerationRatio = 0.2;
                anim.DecelerationRatio = 0.8;
            }
            return anim;
        }

        // ---------- drag gesture ----------

        private void OnPanelPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Record the press but don't capture yet. We promote to a real drag
            // only on first MouseMove past the system drag threshold. Plain
            // mouse-down + mouse-up (a click) falls through untouched so the
            // Border's selection handler runs normally.
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var child = GetChildContaining(e.OriginalSource as DependencyObject);
            if (child == null) return;
            _pressArmed = true;
            _isDragging = false;
            _draggedChild = child;
            _draggedIndexAtStart = InternalChildren.IndexOf(child);
            _draggedIndexCurrent = _draggedIndexAtStart;
            _pressOriginPanel = e.GetPosition(this);
            EnsureTransforms(child);
            var translate = (TranslateTransform)((TransformGroup)child.RenderTransform).Children[TranslateIndex];
            _draggedSlotOrigin = new Point(translate.X, translate.Y);
            _draggedX = translate.X;
            _draggedY = translate.Y;
        }

        private void OnPanelMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pressArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pressArmed = false;
                _draggedChild = null;
                return;
            }
            Point now = e.GetPosition(this);

            if (!_isDragging)
            {
                double dx = Math.Abs(now.X - _pressOriginPanel.X);
                double dy = Math.Abs(now.Y - _pressOriginPanel.Y);
                if (dx < SystemParameters.MinimumHorizontalDragDistance &&
                    dy < SystemParameters.MinimumVerticalDragDistance) return;

                // We always begin an internal drag first. 
                // If the user drags outside the bounds, we will transition to an outgoing OLE drag.
                BeginDrag();
            }

            // If we are dragging, check if we've exited the panel bounds
            if (now.X < -20 || now.X > this.ActualWidth + 20 ||
                now.Y < -20 || now.Y > this.ActualHeight + 20)
            {
                var child = _draggedChild;
                CancelOrFinishDrag(commit: false);
                _pressArmed = false;
                _draggedChild = null;
                OutgoingDragRequested?.Invoke(child);
                return;
            }

            // Track the cursor: keep the dragged item under the pointer at the
            // same offset it was grabbed (so the tile doesn't snap to the cursor).
            _draggedX = _draggedSlotOrigin.X + (now.X - _pressOriginPanel.X);
            _draggedY = _draggedSlotOrigin.Y + (now.Y - _pressOriginPanel.Y);
            AnimateTo(_draggedChild, _draggedX, _draggedY, 0);

            // Pick the slot the centre of the dragged tile currently overlaps.
            double itemW = SafeItemWidth();
            double cx = _draggedX + itemW / 2.0;
            double cy = _draggedY + _slotHeight / 2.0;
            int col = Math.Max(0, Math.Min(_columns - 1, (int)Math.Floor(cx / itemW)));
            int row = Math.Max(0, (int)Math.Floor(cy / _slotHeight));
            int target = Math.Min(InternalChildren.Count - 1, row * _columns + col);

            if (target != _draggedIndexCurrent && target >= 0)
            {
                // Hand the move to the host. It mutates the source ObservableCollection
                // synchronously; ItemsControl's container generator processes the
                // Move notification and shuffles InternalChildren under us. The
                // dragged UIElement instance is preserved (containers are moved,
                // not regenerated, on a Move notification), so _draggedChild
                // remains valid — we just update its tracked index. ReflowAll
                // (called from the resulting Arrange pass) will animate the
                // displaced tiles to their new slots.
                int from = _draggedIndexCurrent;
                _draggedIndexCurrent = target;
                ItemMoveRequested?.Invoke(from, target);
                // Pin the dragged tile to the cursor regardless of any reflow
                // animation that the Arrange pass might have started on it.
                AnimateTo(_draggedChild, _draggedX, _draggedY, 0);
            }
        }

        private void OnPanelMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Mouse released without crossing the drag threshold: a plain click.
            // Clear armed state so the host's selection handler is the only
            // effect — don't fire DragCompleted (nothing was reordered).
            if (!_isDragging)
            {
                _pressArmed = false;
                _draggedChild = null;
                return;
            }
            CancelOrFinishDrag(commit: true);
        }

        private void BeginDrag()
        {
            _isDragging = true;
            Mouse.Capture(this);
            Panel.SetZIndex(_draggedChild, 1000);
            // Lift: scale up + slight fade.
            var scale = (ScaleTransform)((TransformGroup)_draggedChild.RenderTransform).Children[ScaleIndex];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, MakeAnim(LiftScale, LiftMs));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(LiftScale, LiftMs));
            _draggedChild.BeginAnimation(OpacityProperty,
                new DoubleAnimation(LiftOpacity, TimeSpan.FromMilliseconds(LiftMs)));
        }

        private void CancelOrFinishDrag(bool commit)
        {
            if (!_pressArmed && _draggedChild == null) return;
            var dragged = _draggedChild;
            bool wasDragging = _isDragging;
            int from = _draggedIndexAtStart;
            int to = _draggedIndexCurrent;

            _pressArmed = false;
            _isDragging = false;
            _draggedChild = null;

            if (dragged == null) return;

            if (IsMouseCaptured) ReleaseMouseCapture();

            if (wasDragging)
            {
                // Snap to the target slot.
                double itemW = SafeItemWidth();
                int col = to % _columns;
                int row = to / _columns;
                AnimateTo(dragged, col * itemW, row * _slotHeight, DropMs);

                var scale = (ScaleTransform)((TransformGroup)dragged.RenderTransform).Children[ScaleIndex];
                var ease = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, MakeAnim(1, LiftMs, ease));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(1, LiftMs, ease));
                dragged.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(LiftMs)));

                // Reset z-order after the drop animation completes so the tile stays
                // visually on top while it's snapping.
                var resetZ = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(DropMs + 30)
                };
                resetZ.Tick += (_, _) =>
                {
                    resetZ.Stop();
                    Panel.SetZIndex(dragged, 0);
                };
                resetZ.Start();

                if (commit && from != to) DragCompleted?.Invoke();
            }
        }

        private UIElement GetChildContaining(DependencyObject hit)
        {
            DependencyObject n = hit;
            while (n != null)
            {
                if (n is UIElement ue && InternalChildren.Contains(ue)) return ue;
                n = VisualTreeHelper.GetParent(n);
            }
            return null;
        }
    }
}
