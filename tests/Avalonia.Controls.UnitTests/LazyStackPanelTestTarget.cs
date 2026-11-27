using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.UnitTests;
using Avalonia.VisualTree;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// An items control in a window, with a <see cref="LazyStackPanel"/> or a VSP.
    /// </summary>
    internal static class LazyStackPanelTestTarget
    {
        public static (Window Window, ScrollViewer ScrollViewer) Show(
            ItemsControl ic, IEnumerable items, bool lazy, double cacheLength = 0, double size = 100, params IStyle[] styles)
        {
            ic.Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                    [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                }.RegisterInNameScope(ns),
            });
            ic.ItemsSource = items;
            ic.ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                ? new LazyStackPanel { CacheLength = cacheLength }
                : new VirtualizingStackPanel { CacheLength = cacheLength });

            var window = new Window { Width = size, Height = size, Content = ic };
            window.Styles.AddRange(styles);
            window.Show();
            RunLayout(window);
            // No ScrollViewer yet for a hidden items control (no template).
            return (window, ic.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()!);
        }

        /// <summary>
        /// A relative scroll: a command to an <see cref="IScrollCommandTarget"/>, else an Offset write.
        /// </summary>
        public static void ScrollBy(ScrollViewer sv, double delta)
        {
            if (sv.FindDescendantOfType<ItemsPresenter>()?.Panel is IScrollCommandTarget target)
                target.ScrollBy(new Avalonia.Vector(0, delta), Avalonia.Controls.Primitives.ScrollSource.Programmatic);
            else
                sv.Offset = new Avalonia.Vector(0, sv.Offset.Y + delta);
        }

        public static void RunLayout(Control c)
        {
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
        }
    }

    /// <summary>
    /// Starts a styled, focusable test application for each test.
    /// </summary>
    public abstract class LazyStackPanelTestBase : ScopedTestBase
    {
        private readonly System.IDisposable _app;

        protected LazyStackPanelTestBase() => _app = UnitTestApplication.Start(TestServices.FocusableWindow);

        public override void Dispose()
        {
            _app.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Replaces all items with one <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// </summary>
    internal class ResettingCollection<T> : ObservableCollection<T>
    {
        public ResettingCollection(IEnumerable<T> items) : base(items) { }

        public void Reset(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
