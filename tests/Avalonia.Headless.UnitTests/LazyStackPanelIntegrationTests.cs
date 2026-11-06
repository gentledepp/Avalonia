#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalonia.Headless.UnitTests;

/// <summary>
/// Headless integration tests for <see cref="LazyStackPanel"/> and <see cref="VirtualizingStackPanel"/>: a list in a
/// headless window, real input (mouse wheel, keys, scrollbar thumb and track) and dispatcher/render frames instead
/// of forced layout passes.
/// After each step the rows on the screen are checked: contiguous, in collection order, no gap or overlap, the
/// viewport filled, and <c>0 &lt;= Offset &lt;= max(0, Extent - Viewport)</c>.
/// </summary>
public class LazyStackPanelIntegrationTests
{
    private const double Tolerance = 0.5;
    private const double WheelStep = 50; // ScrollSize of both panels for one wheel notch
    private static readonly Point s_wheelPoint = new(100, 150);

    // ---------------------------------------------------------------------------------------------------------------
    // Mouse wheel and Shift+wheel
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 40 wheel notches down move the content by exactly one step each, Shift+wheel (horizontal, disabled) does not
    /// move it, and wheeling up returns to row 0 at 0 with Offset 0.
    /// </summary>
#if NUNIT
    [AvaloniaTheory, TestCase(false), TestCase(true)]
#elif XUNIT
    [AvaloniaTheory, InlineData(false), InlineData(true)]
#endif
    public void Wheel_And_Shift_Wheel(bool lazy)
    {
        using var h = Host.Show(lazy, Periodic(500), PlainRow);
        var pos = h.ContentPosition();
        AssertClose(0, pos, "start");

        for (var i = 0; i < 40; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, -1));
            h.Check($"wheel down {i}");
            var p = h.ContentPosition();
            AssertClose(WheelStep, p - pos, $"wheel down {i}: content moved");
            pos = p;
        }

        for (var i = 0; i < 3; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, -1), RawInputModifiers.Shift);
            h.Check($"shift wheel {i}");
            AssertClose(pos, h.ContentPosition(), $"shift wheel {i}: content moved");
        }

        for (var i = 0; i < 45; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, 1));
            h.Check($"wheel up {i}");
            var p = h.ContentPosition();
            AssertClose(Math.Max(0, pos - WheelStep), p, $"wheel up {i}: content position");
            pos = p;
        }

        var first = h.OnScreen()[0];
        AssertHelper.Equal(0, first.Index);
        AssertClose(0, first.Top, "row 0 top at the top");
        AssertClose(0, h.ScrollViewer.Offset.Y, "Offset at the top");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Keys
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Keys in a <see cref="ListBox"/>: a row clicked with the mouse, then Down x12 (selected row fully visible, the
    /// content moves only when the row was not fully visible), PageDown/PageUp (a viewport), End (last row
    /// bottom-aligned), Up x3, Home (row 0 at 0, Offset 0).
    /// </summary>
#if NUNIT
    [AvaloniaTheory, TestCase(false), TestCase(true)]
#elif XUNIT
    [AvaloniaTheory, InlineData(false), InlineData(true)]
#endif
    public void Keys_Up_Down_PageUp_PageDown_Home_End(bool lazy)
    {
        using var h = Host.Show(lazy, Periodic(200), PlainRow);
        var lb = (ListBox)h.List;

        h.Click(h.OnScreen()[0].Container);
        AssertHelper.Equal(0, lb.SelectedIndex);
        AssertHelper.True(h.OnScreen()[0].Container.IsFocused, "row 0 focused after the click");

        for (var i = 1; i <= 12; i++)
        {
            var before = h.ContentPosition();
            var wasFullyVisible = h.IsFullyVisible(i);
            h.Press(Key.Down);
            h.Check($"Down {i}");
            AssertHelper.Equal(i, lb.SelectedIndex);
            AssertHelper.True(h.IsFullyVisible(i), $"Down {i}: selected row fully visible");
            AssertHelper.True(lb.ContainerFromIndex(i)!.IsFocused, $"Down {i}: selected row focused");
            if (wasFullyVisible)
                AssertClose(before, h.ContentPosition(), $"Down {i}: content moved for a visible row");
        }

        var pos = h.ContentPosition();
        var viewport = h.ScrollViewer.Viewport.Height;
        h.Press(Key.PageDown);
        h.Check("PageDown");
        AssertClose(viewport, h.ContentPosition() - pos, "PageDown: content moved");

        h.Press(Key.PageUp);
        h.Check("PageUp");
        AssertClose(pos, h.ContentPosition(), "PageUp: content position");

        h.Press(Key.End);
        h.Check("End");
        var last = h.Items.Count - 1;
        AssertHelper.Equal(last, lb.SelectedIndex);
        var lastSlot = h.OnScreen().Last();
        AssertHelper.Equal(last, lastSlot.Index);
        AssertClose(viewport, lastSlot.Top + lastSlot.Height, "End: last row bottom");
        AssertClose(h.ScrollViewer.Extent.Height - viewport, h.ScrollViewer.Offset.Y, "End: Offset = Extent - Viewport");

        for (var i = 1; i <= 3; i++)
        {
            h.Press(Key.Up);
            h.Check($"Up {i}");
            AssertHelper.Equal(last - i, lb.SelectedIndex);
            AssertHelper.True(h.IsFullyVisible(last - i), $"Up {i}: selected row fully visible");
        }

        h.Press(Key.Home);
        h.Check("Home");
        AssertHelper.Equal(0, lb.SelectedIndex);
        AssertHelper.True(lb.ContainerFromIndex(0)!.IsFocused, "Home: row 0 focused");
        var first = h.OnScreen()[0];
        AssertHelper.Equal(0, first.Index);
        AssertClose(0, first.Top, "Home: row 0 top");
        AssertClose(0, h.ScrollViewer.Offset.Y, "Home: Offset");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Scrollbar thumb and track
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The thumb dragged with the mouse 1 px at a time, 120 px down and back, over 40 px rows 0-299 and 160 px rows
    /// after, so the average changes during the drag. Every step moves the content in the drag direction, never back.
    /// </summary>
#if NUNIT
    [AvaloniaTheory, TestCase(false), TestCase(true)]
#elif XUNIT
    [AvaloniaTheory, InlineData(false), InlineData(true)]
#endif
    public void Thumb_Drag_In_Small_Steps(bool lazy)
    {
        using var h = Host.Show(lazy, Regions(400), PlainRow);
        var thumb = h.VerticalScrollBar.GetVisualDescendants().OfType<Thumb>().First();
        var start = h.Center(thumb);
        var pos = h.ContentPosition();
        var backSteps = new List<string>();
        var stillSteps = 0;

        h.Window.MouseMove(start);
        h.Window.MouseDown(start, MouseButton.Left);

        for (var i = 1; i <= 120; i++)
        {
            h.Window.MouseMove(start + new Point(0, i));
            h.Check($"drag down {i}");
            var p = h.ContentPosition();
            if (p < pos - Tolerance)
                backSteps.Add($"down {i}: {pos:F1} -> {p:F1}");
            else if (p <= pos + Tolerance)
                stillSteps++;
            pos = p;
        }

        var bottom = pos;

        for (var i = 119; i >= 0; i--)
        {
            h.Window.MouseMove(start + new Point(0, i));
            h.Check($"drag up {i}");
            var p = h.ContentPosition();
            if (p > pos + Tolerance)
                backSteps.Add($"up {i}: {pos:F1} -> {p:F1}");
            else if (p >= pos - Tolerance)
                stillSteps++;
            pos = p;
        }

        h.Window.MouseUp(start, MouseButton.Left);
        h.Check("released");

        AssertHelper.True(bottom > 1000, $"the drag moved the content ({bottom:F1} px)");
        AssertHelper.True(backSteps.Count == 0, "steps against the drag direction: " + string.Join("; ", backSteps));
        AssertHelper.True(stillSteps == 0, $"{stillSteps} of 240 one-pixel thumb steps did not move the content");
        AssertClose(0, pos, "thumb back at the start: content position");
    }

    /// <summary>
    /// A mouse click on the track below the thumb scrolls down by
    /// <see cref="RangeBase.LargeChange"/> (one viewport), on the down arrow by <see cref="RangeBase.SmallChange"/>,
    /// then the up arrow and the track above the thumb go back.
    /// </summary>
#if NUNIT
    [AvaloniaTheory, TestCase(false), TestCase(true)]
#elif XUNIT
    [AvaloniaTheory, InlineData(false), InlineData(true)]
#endif
    public void Track_And_Arrow_Clicks(bool lazy)
    {
        using var h = Host.Show(lazy, Periodic(300), PlainRow);
        var bar = h.VerticalScrollBar;
        AssertClose(h.ScrollViewer.Viewport.Height, bar.LargeChange, "LargeChange = viewport");

        void ClickPart(string name, double expected)
        {
            var part = bar.GetVisualDescendants().OfType<Control>().First(x => x.Name == name);
            var before = h.ContentPosition();
            h.Click(part);
            h.Check(name);
            AssertClose(expected, h.ContentPosition() - before, $"{name}: content moved");
        }

        ClickPart("PART_PageDownButton", bar.LargeChange);
        ClickPart("PART_PageDownButton", bar.LargeChange);
        ClickPart("PART_LineDownButton", bar.SmallChange);
        ClickPart("PART_LineUpButton", -bar.SmallChange);
        ClickPart("PART_PageUpButton", -bar.LargeChange);
        ClickPart("PART_PageUpButton", -bar.LargeChange);
        AssertClose(0, h.ContentPosition(), "back at the top");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Async growth from a DispatcherTimer
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Rows start as 40 px placeholders and "load" to their real height (60-200 px) in a
    /// <see cref="DispatcherTimer"/> tick, between wheel steps, with the real render timer running the layout.
    /// The grown row is, in turn, a buffer row above the screen, the anchor row and a row on the screen below it.
    /// Every layout pass: the invariants, and the rows on the screen above the grown row (all of them when it is above
    /// the screen; the grown row's own top too) keep their screen position. At the end the extent settles: wheeling to
    /// the end gives the last row bottom = viewport bottom and Offset = Extent - Viewport.
    /// </summary>
#if NUNIT
    [AvaloniaTheory, TestCase(false), TestCase(true)]
#elif XUNIT
    [AvaloniaTheory, InlineData(false), InlineData(true)]
#endif
    public async Task Async_Growth_From_A_DispatcherTimer_While_Scrolling(bool lazy)
    {
        var rows = Enumerable.Range(0, 300).Select(i => new Row(i, 40)).ToList();
        using var h = Host.Show(lazy, rows, PlainRow);
        var errors = new List<string>();
        List<Dictionary<int, double>>? keep = null; // alternatives: one of them must hold
        var label = "";
        int? armed = null;
        var grown = new HashSet<string>(); // growth steps followed by at least one compared layout pass
        var growths = 0; // growths of realized rows
        var laidOutByTheLoop = 0; // of them, laid out by the dispatcher loop's own render timer

        h.Panel.LayoutUpdated += (_, _) =>
        {
            errors.AddRange(h.Validate(label + " (layout pass)"));
            if (keep is null)
                return;
            var onScreen = h.OnScreen();
            grown.Add(label);
            List<string>? moved = null;
            foreach (var alternative in keep)
            {
                var m = onScreen
                    .Where(s => alternative.TryGetValue(s.Index, out var top) && Math.Abs(s.Top - top) > Tolerance)
                    .Select(s => $"{label}: row {s.Index} moved {alternative[s.Index]:F1} -> {s.Top:F1}")
                    .ToList();
                if (m.Count == 0)
                    return;
                moved ??= m;
            }

            errors.AddRange(moved!);
        };

        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background, (_, _) =>
        {
            if (armed is not { } index)
                return;
            armed = null;
            var onScreen = h.OnScreen();
            var first = onScreen[0].Index;
            // The rows above the grown row and its own top stay (all rows when it is above the screen).
            keep = [onScreen.Where(s => index < first || s.Index <= index).ToDictionary(s => s.Index, s => s.Top)];
            // VSP: when the anchor row grows, either its top stays or its top moves up by the
            // growth and the rows below it stay, depending on the VSP's own anchor.
            if (!lazy && index == first)
                keep.Add(onScreen.Where(s => s.Index > index).ToDictionary(s => s.Index, s => s.Top));
            rows[index].Height = rows[index].Height == 40 ? 60 + (index * 53) % 141 : rows[index].Height + 30;
        });
        timer.Start();

        try
        {
            for (var step = 0; step < 45; step++)
            {
                h.Window.MouseWheel(s_wheelPoint, new Vector(0, step < 30 ? -1 : 1));
                h.Check($"wheel {step}");

                var first = h.OnScreen()[0].Index;
                var kind = step % 3;
                var target = kind switch { 0 => first - 1, 1 => first, _ => first + 2 };
                if (target < 0)
                    continue;
                label = $"step {step}: grow row {target} ({(kind == 0 ? "above" : kind == 1 ? "anchor" : "below")}, first {first})";
                armed = target;
                if (h.List.ContainerFromIndex(target) is { IsVisible: true })
                    growths++; // a realized row: its growth must reach a layout pass

                for (var wait = 0; armed is not null && wait < 200; wait++)
                    await Task.Delay(2);
                AssertHelper.True(armed is null, label + ": the timer did not tick");
                await Task.Delay(40); // real render frames (60 fps render timer)
                if (grown.Contains(label))
                    laidOutByTheLoop++;
                Frame();
                keep = null;
                h.Check(label);
            }
        }
        finally
        {
            timer.Stop();
        }

        AssertHelper.True(errors.Count == 0, $"{errors.Count} errors: " + string.Join("; ", errors.Take(10)));
        AssertHelper.True(growths >= 40 && laidOutByTheLoop == growths,
            $"{growths} growths of realized rows, {laidOutByTheLoop} laid out by the dispatcher loop");

        // The extent settles: wheel to the end.
        label = "to the end";
        var pos = h.ContentPosition();
        for (var i = 0; i < 400; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, -1));
            var p = h.ContentPosition();
            if (Math.Abs(p - pos) < 0.01)
                break;
            pos = p;
        }

        h.Check("at the end");
        var last = h.OnScreen().Last();
        AssertHelper.Equal(rows.Count - 1, last.Index);
        AssertClose(h.ScrollViewer.Viewport.Height, last.Top + last.Height, "end: last row bottom");
        AssertClose(h.ScrollViewer.Extent.Height - h.ScrollViewer.Viewport.Height, h.ScrollViewer.Offset.Y,
            "end: Offset = Extent - Viewport");
        AssertHelper.True(errors.Count == 0, $"{errors.Count} errors: " + string.Join("; ", errors.Take(10)));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Expander
    // ---------------------------------------------------------------------------------------------------------------

    public enum ExpanderRow
    {
        /// <summary>The anchor row (first visible, cut at the top), its header clicked with the mouse.</summary>
        Anchor,
        /// <summary>A realized buffer row just above the screen, through the bound model.</summary>
        Above,
        /// <summary>A row 20 rows above the screen (not realized), through the bound model.</summary>
        FarAbove,
        /// <summary>A row on the screen below the anchor, its header clicked with the mouse.</summary>
        Below,
    }

    /// <summary>
    /// An <see cref="Expander"/> in a row opens and closes. Anchor row: its top stays, rows below move.
    /// A row above the screen: nothing on the screen moves. A row below the anchor: the rows down to it stay.
    /// </summary>
#if NUNIT
    [AvaloniaTheory]
    [TestCase(false, ExpanderRow.Anchor), TestCase(true, ExpanderRow.Anchor)]
    [TestCase(false, ExpanderRow.Above), TestCase(true, ExpanderRow.Above)]
    [TestCase(false, ExpanderRow.FarAbove), TestCase(true, ExpanderRow.FarAbove)]
    [TestCase(false, ExpanderRow.Below), TestCase(true, ExpanderRow.Below)]
#elif XUNIT
    [AvaloniaTheory]
    [InlineData(false, ExpanderRow.Anchor), InlineData(true, ExpanderRow.Anchor)]
    [InlineData(false, ExpanderRow.Above), InlineData(true, ExpanderRow.Above)]
    [InlineData(false, ExpanderRow.FarAbove), InlineData(true, ExpanderRow.FarAbove)]
    [InlineData(false, ExpanderRow.Below), InlineData(true, ExpanderRow.Below)]
#endif
    public void Expander_Opens_And_Closes(bool lazy, ExpanderRow which)
    {
        using var h = Host.Show(lazy, Periodic(200), ExpanderRowTemplate);

        // Scroll until the anchor row is cut at the top and its expander header is fully on the screen.
        for (var i = 0; i < 100; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, -1));
            h.Check($"setup wheel {i}");
            var a = h.OnScreen()[0];
            if (a.Index >= 25 && a.Top < -1 && h.Top(Header(a.Container)) >= 0)
                break;
        }

        var anchor = h.OnScreen()[0];
        AssertHelper.True(anchor.Top < -1, $"setup: anchor row {anchor.Index} cut at the top ({anchor.Top:F1})");

        var target = which switch
        {
            ExpanderRow.Anchor => anchor.Index,
            ExpanderRow.Above => anchor.Index - 1,
            ExpanderRow.FarAbove => anchor.Index - 20,
            // The first row below the anchor whose expander header is fully on the screen.
            _ => h.OnScreen().Skip(1).First(s =>
                h.Top(Header(s.Container)) + Header(s.Container).Bounds.Height <= h.ScrollViewer.Viewport.Height).Index,
        };

        if (which == ExpanderRow.Above)
            AssertHelper.True(h.List.ContainerFromIndex(target) is { IsVisible: true }, $"setup: row {target} realized");
        if (which == ExpanderRow.FarAbove)
            AssertHelper.True(h.List.ContainerFromIndex(target) is null, $"setup: row {target} not realized");

        foreach (var open in new[] { true, false })
        {
            var before = h.OnScreen();
            var keep = before.Where(s => target < before[0].Index || s.Index <= target).ToList();
            if (which is ExpanderRow.Anchor or ExpanderRow.Below)
                h.Click(Header(h.List.ContainerFromIndex(target)!));
            else
                h.Items[target].IsExpanded = open;
            Frame(2);

            var step = $"{which} row {target} {(open ? "opened" : "closed")}";
            h.Check(step);
            AssertHelper.Equal(open, h.Items[target].IsExpanded);
            var after = h.OnScreen().ToDictionary(s => s.Index, s => s.Top);
            foreach (var s in keep)
            {
                AssertHelper.True(after.TryGetValue(s.Index, out var top), $"{step}: row {s.Index} left the screen");
                AssertClose(s.Top, top, $"{step}: row {s.Index} top");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Multiline TextBox grows while typing
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A multiline <see cref="TextBox"/> row (clicked with the mouse) grows by a line per Enter, 15 times.
    /// Anchor row (top at 0): its top stays. A row in the middle: its top and the rows above stay until the caret
    /// reaches the viewport bottom; after that the caret is kept visible. The caret line is always on the screen.
    /// </summary>
#if NUNIT
    [AvaloniaTheory]
    [TestCase(false, true), TestCase(true, true), TestCase(false, false), TestCase(true, false)]
#elif XUNIT
    [AvaloniaTheory]
    [InlineData(false, true), InlineData(true, true), InlineData(false, false), InlineData(true, false)]
#endif
    public void Multiline_TextBox_Grows_While_Typing(bool lazy, bool anchorRow)
    {
        var rows = Enumerable.Range(0, 200).Select(i => new Row(i, 0) { Text = $"row {i}" }).ToList();
        using var h = Host.Show(lazy, rows, MultilineTextBoxRow, listBox: false);

        for (var i = 0; i < 20; i++)
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, -1));
        h.Check("setup wheel");
        var aligned = h.OnScreen()[0].Index - 3;
        h.List.ScrollIntoView(aligned);
        Frame(2);
        h.Check("setup ScrollIntoView");
        var first = h.OnScreen()[0];
        AssertHelper.Equal(aligned, first.Index);
        AssertClose(0, first.Top, "setup: row above top-aligned");

        var index = anchorRow ? first.Index : first.Index + 3;
        var textBox = (TextBox)h.List.ContainerFromIndex(index)!.GetVisualDescendants().OfType<TextBox>().First();
        h.Click(textBox);
        AssertHelper.True(textBox.IsFocused, "the clicked text box has the focus");
        h.Press(Key.End, RawInputModifiers.Control);

        var startTop = h.Top(textBox);
        var above = h.OnScreen().Where(s => s.Index < index).ToDictionary(s => s.Index, s => s.Top);
        var height = textBox.Bounds.Height;
        var broughtIntoView = false;

        for (var i = 1; i <= 15; i++)
        {
            h.Window.KeyTextInput("abc");
            h.Press(Key.Enter);
            Frame();
            var step = $"line {i}";
            h.Check(step);
            AssertHelper.True(textBox.Bounds.Height > height, $"{step}: the text box grew ({height:F1} -> {textBox.Bounds.Height:F1})");
            height = textBox.Bounds.Height;
            AssertHelper.Equal(i + 1, textBox.Text!.Split('\n').Length);

            var top = h.Top(textBox);
            var bottom = top + height;
            var viewport = h.ScrollViewer.Viewport.Height;
            // The caret rectangle is inside the padding (4) and the border (1); its line box is up to 1 px shorter.
            AssertHelper.True(bottom <= viewport + textBox.Padding.Bottom + 2 + Tolerance,
                $"{step}: caret line on the screen (text box bottom {bottom:F1}, viewport {viewport:F1})");

            if (Math.Abs(top - startTop) <= Tolerance && !broughtIntoView)
            {
                foreach (var s in h.OnScreen().Where(s => above.ContainsKey(s.Index)))
                    AssertClose(above[s.Index], s.Top, $"{step}: row {s.Index} above the text box");
            }
            else
            {
                // Only allowed when the caret reached the viewport bottom: the content moved up to keep it visible.
                AssertHelper.False(anchorRow, $"{step}: anchor text box top moved {startTop:F1} -> {top:F1}");
                AssertHelper.True(top < startTop, $"{step}: text box moved down {startTop:F1} -> {top:F1}");
                AssertHelper.True(bottom >= viewport - textBox.Padding.Bottom - 1 - Tolerance,
                    $"{step}: text box moved {startTop:F1} -> {top:F1} before the caret reached the bottom ({bottom:F1})");
                broughtIntoView = true;
            }
        }

        AssertHelper.True(textBox.IsFocused, "the text box keeps the focus");
        AssertHelper.Equal(16, rows[index].Text!.Split('\n').Length);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Focused TextBox row scrolls out and back
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A form-list row (an <see cref="ItemsControl"/>, a <see cref="TextBox"/> per row) is clicked and typed in,
    /// scrolled out with the wheel (15 notches) and back. Lazy keeps the row and the focus, typing after the scroll back
    /// reaches the same text box. The VSP loses the focus; typing then reaches nothing.
    /// </summary>
#if NUNIT
    [AvaloniaTheory, TestCase(false), TestCase(true)]
#elif XUNIT
    [AvaloniaTheory, InlineData(false), InlineData(true)]
#endif
    public void Focused_TextBox_Row_Scrolled_Out_And_Back(bool lazy)
    {
        var rows = Enumerable.Range(0, 200).Select(i => new Row(i, 0) { Text = $"row {i}" }).ToList();
        using var h = Host.Show(lazy, rows, SingleLineTextBoxRow, listBox: false);
        var container = h.List.ContainerFromIndex(2)!;
        var textBox = container.GetVisualDescendants().OfType<TextBox>().First();
        h.Click(textBox);
        AssertHelper.True(textBox.IsFocused, "the clicked text box has the focus");
        h.Press(Key.End);
        h.Window.KeyTextInput("x");
        AssertHelper.Equal("row 2x", rows[2].Text);

        for (var i = 0; i < 15; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, -1));
            h.Check($"wheel down {i}");
        }

        AssertHelper.True(h.OnScreen()[0].Index > 10, "row 2 is far out");
        AssertHelper.Equal(lazy, textBox.IsFocused);
        AssertHelper.Equal(lazy, ReferenceEquals(h.Window.FocusManager?.GetFocusedElement(), textBox));
        AssertHelper.Equal(lazy, ReferenceEquals(h.List.ContainerFromIndex(2), container) && container.IsVisible);

        for (var i = 0; i < 20; i++)
        {
            h.Window.MouseWheel(s_wheelPoint, new Vector(0, 1));
            h.Check($"wheel up {i}");
        }

        AssertHelper.Equal(0, h.OnScreen()[0].Index);
        var back = h.List.ContainerFromIndex(2)!.GetVisualDescendants().OfType<TextBox>().First();
        AssertHelper.Equal("row 2x", back.Text);
        AssertHelper.Equal(lazy, ReferenceEquals(back, textBox));
        AssertHelper.Equal(lazy, back.IsFocused);

        h.Window.KeyTextInput("y");
        AssertHelper.Equal(lazy ? "row 2xy" : "row 2x", rows[2].Text);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Data and templates
    // ---------------------------------------------------------------------------------------------------------------

    public sealed class Row : INotifyPropertyChanged
    {
        private double _height;
        private string? _text;
        private bool _isExpanded;

        public Row(int id, double height)
        {
            Id = id;
            _height = height;
        }

        public int Id { get; }

        public double Height
        {
            get => _height;
            set => Set(ref _height, value);
        }

        public string? Text
        {
            get => _text;
            set => Set(ref _text, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => $"Row {Id}";

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Periodic heights 40-160 px.</summary>
    private static List<Row> Periodic(int count) =>
        Enumerable.Range(0, count).Select(i => new Row(i, 40 + (i * 37) % 121)).ToList();

    /// <summary>40 px rows 0-299, 160 px after.</summary>
    private static List<Row> Regions(int count) =>
        Enumerable.Range(0, count).Select(i => new Row(i, i < 300 ? 40 : 160)).ToList();

    private static Control PlainRow() => new Border
    {
        Background = Brushes.LightGray,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(0, 0, 0, 1),
        [!Layoutable.HeightProperty] = new Binding(nameof(Row.Height)),
        Child = new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(Row.Id)) },
    };

    private static Control ExpanderRowTemplate() => new StackPanel
    {
        Children =
        {
            new Border
            {
                Background = Brushes.LightGray,
                [!Layoutable.HeightProperty] = new Binding(nameof(Row.Height)),
                Child = new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(Row.Id)) },
            },
            new Expander
            {
                Header = "details",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                [!Expander.IsExpandedProperty] = new Binding(nameof(Row.IsExpanded)) { Mode = BindingMode.TwoWay },
                Content = new Border { Height = 150, Background = Brushes.LightBlue },
            },
        },
    };

    private static Control MultilineTextBoxRow() => new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 0,
        [!TextBox.TextProperty] = new Binding(nameof(Row.Text)) { Mode = BindingMode.TwoWay },
    };

    private static Control SingleLineTextBoxRow() => new TextBox
    {
        MinHeight = 0,
        [!TextBox.TextProperty] = new Binding(nameof(Row.Text)) { Mode = BindingMode.TwoWay },
    };

    private static Control Header(Control container) =>
        container.GetVisualDescendants().OfType<Expander>().First()
            .GetVisualDescendants().OfType<ToggleButton>().First();

    // ---------------------------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One frame as an application gets it: the dispatcher jobs and a render timer tick (layout and render), no forced
    /// <c>UpdateLayout</c>.
    /// </summary>
    private static void Frame(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertClose(double expected, double actual, string message) =>
        AssertHelper.True(Math.Abs(expected - actual) <= Tolerance, $"{message}: expected {expected:F2}, actual {actual:F2}");

    private readonly record struct Slot(int Index, Control Container, double Top, double Height);

    private sealed class Host : IDisposable
    {
        private Host(bool lazy, Window window, ItemsControl list, ObservableCollection<Row> items)
        {
            Lazy = lazy;
            Window = window;
            List = list;
            Items = items;
            ScrollViewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Presenter = ScrollViewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            Panel = list.ItemsPanelRoot!;
            VerticalScrollBar = ScrollViewer.GetVisualDescendants().OfType<ScrollBar>()
                .First(x => x.Orientation == Orientation.Vertical);
        }

        public bool Lazy { get; }
        public Window Window { get; }
        public ItemsControl List { get; }
        public ObservableCollection<Row> Items { get; }
        public ScrollViewer ScrollViewer { get; }
        public ScrollContentPresenter Presenter { get; }
        public Panel Panel { get; }
        public ScrollBar VerticalScrollBar { get; }

        public static Host Show(bool lazy, IList<Row> rows, Func<Control> build, bool listBox = true)
        {
            var items = new ObservableCollection<Row>(rows);
            ItemsControl list = listBox ? new ListBox { Padding = new Thickness(0) } : new ItemsControl
            {
                Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                    [~ScrollViewer.AllowAutoHideProperty] = new TemplateBinding(ScrollViewer.AllowAutoHideProperty),
                    [~ScrollViewer.HorizontalScrollBarVisibilityProperty] =
                        new TemplateBinding(ScrollViewer.HorizontalScrollBarVisibilityProperty),
                }),
            };
            list.ItemsSource = items;
            list.ItemTemplate = new FuncDataTemplate<Row>((_, _) => build());
            list.ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                ? new LazyStackPanel { CacheLength = 0.5 }
                : new VirtualizingStackPanel { CacheLength = 0.5 });
            ScrollViewer.SetAllowAutoHide(list, false);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                },
            });

            var window = new Window { Width = 300, Height = 300, Content = list };
            window.Show();
            Frame(3);
            var host = new Host(lazy, window, list, items);
            host.Check("shown");
            return host;
        }

        public void Dispose() => Window.Close();

        public double Top(Visual v) => v.TranslatePoint(default, Presenter)!.Value.Y;

        public Point Center(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), Window)!.Value;

        public void Click(Visual v)
        {
            var p = Center(v);
            Window.MouseMove(p);
            Window.MouseDown(p, MouseButton.Left);
            Window.MouseUp(p, MouseButton.Left);
        }

        public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);
            Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        }

        /// <summary>The visible realized rows that intersect the viewport, by index.</summary>
        public List<Slot> OnScreen()
        {
            var viewport = ScrollViewer.Viewport.Height;
            var result = new List<Slot>();
            foreach (var c in Panel.Children)
            {
                if (!c.IsVisible)
                    continue;
                var index = List.IndexFromContainer(c);
                if (index < 0)
                    continue;
                var top = Top(c);
                var height = c.Bounds.Height;
                if (top + height > 0.01 && top < viewport - 0.01)
                    result.Add(new Slot(index, c, top, height));
            }

            result.Sort((a, b) => a.Index.CompareTo(b.Index));
            return result;
        }

        public bool IsFullyVisible(int index)
        {
            var s = OnScreen().FirstOrDefault(x => x.Index == index);
            return s.Container is not null && s.Top >= -Tolerance &&
                   s.Top + s.Height <= ScrollViewer.Viewport.Height + Tolerance;
        }

        /// <summary>
        /// The real scroll position of the content: the sum of the model heights above the first visible row minus
        /// its top. Independent of the panel's estimate (for templates whose height is <see cref="Row.Height"/>).
        /// </summary>
        public double ContentPosition()
        {
            var first = OnScreen()[0];
            var sum = 0.0;
            for (var i = 0; i < first.Index; i++)
                sum += Items[i].Height;
            return sum - first.Top;
        }

        public void Check(string step)
        {
            var errors = Validate(step);
            AssertHelper.True(errors.Count == 0, string.Join("; ", errors));
        }

        /// <summary>The viewport covered, collection order, no gap or overlap, Offset within 0..Extent - Viewport.</summary>
        public List<string> Validate(string step)
        {
            var errors = new List<string>();
            var viewport = ScrollViewer.Viewport.Height;
            var slots = OnScreen();
            var name = Lazy ? "Lazy" : "VSP";
            if (slots.Count == 0)
            {
                errors.Add($"{name} {step}: no row on the screen");
                return errors;
            }

            for (var k = 0; k < slots.Count; k++)
            {
                var s = slots[k];
                if (!ReferenceEquals(s.Container.DataContext, Items[s.Index]))
                    errors.Add($"{name} {step}: row {s.Index} shows {s.Container.DataContext}");
                if (k == 0)
                    continue;
                var p = slots[k - 1];
                if (s.Index != p.Index + 1)
                    errors.Add($"{name} {step}: rows {p.Index} and {s.Index} not contiguous");
                else if (Math.Abs(s.Top - (p.Top + p.Height)) > Tolerance)
                    errors.Add($"{name} {step}: row {s.Index} top {s.Top:F2}, row {p.Index} bottom {p.Top + p.Height:F2}");
            }

            if (slots[0].Top > Tolerance)
                errors.Add($"{name} {step}: empty space above row {slots[0].Index} ({slots[0].Top:F2})");
            var last = slots[^1];
            if (last.Top + last.Height < viewport - Tolerance)
                errors.Add($"{name} {step}: empty space below row {last.Index} (bottom {last.Top + last.Height:F2}, viewport {viewport:F2})");

            var offset = ScrollViewer.Offset.Y;
            var max = Math.Max(0, ScrollViewer.Extent.Height - viewport);
            if (offset < -0.01 || offset > max + 0.01)
                errors.Add($"{name} {step}: Offset {offset:F2} outside 0..{max:F2} (Extent {ScrollViewer.Extent.Height:F2})");
            return errors;
        }
    }
}
