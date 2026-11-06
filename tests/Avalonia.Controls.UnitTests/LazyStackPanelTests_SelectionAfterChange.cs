using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Styling;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;
using Row = Avalonia.Controls.UnitTests.LazyStackPanelTests_BlackBoxRecycling.Row;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// A selection change between a collection change and the next layout: after the layout every realized
    /// container shows the selection of its index. 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_SelectionAfterChange : LazyStackPanelTestBase
    {
        public enum Change { Clear, DeselectRow3 }

        [Theory]
        [InlineData(false, Change.Clear, false)]
        [InlineData(true, Change.Clear, false)]
        [InlineData(false, Change.DeselectRow3, false)]
        [InlineData(true, Change.DeselectRow3, false)]
        [InlineData(false, Change.Clear, true)]
        [InlineData(true, Change.Clear, true)]
        [InlineData(false, Change.DeselectRow3, true)]
        [InlineData(true, Change.DeselectRow3, true)]
        public void IsSelected_Matches_The_Selection_After_A_Change_And_A_Selection_Change(bool lazy, Change change, bool insertAtTop)
        {
            var items = new ObservableCollection<Row>(Enumerable.Range(0, 100).Select(x => new Row(x)));
            var lb = CreateListBox();
            var (window, _) = Show(lb, items, lazy);
            lb.Selection.SelectRange(3, 5);
            RunLayout(lb);
            Assert.True(lb.ContainerFromIndex(4)!.Classes.Contains(":selected"));

            // In the same call: a collection change, then a selection change, then one layout.
            if (insertAtTop)
                items.Insert(0, new Row(100));
            else
                items.Add(new Row(100));

            if (change == Change.Clear)
                lb.Selection.Clear();
            else
                lb.SelectedItems!.Remove(items[insertAtTop ? 4 : 3]);

            RunLayout(lb);

            var wrong = lb.GetRealizedContainers()
                .Where(c => c.IsVisible)
                .Select(c => (Index: lb.IndexFromContainer(c), Selected: ((ListBoxItem)c).IsSelected))
                .Where(x => x.Index >= 0 && x.Selected != lb.Selection.IsSelected(x.Index))
                .ToList();
            Assert.Empty(wrong);
            window.Close();
        }

        private static ListBox CreateListBox() => new()
        {
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<Row>((r, _) => new Border { Height = 10 }),
            ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(ListBoxItem.TemplateProperty, new FuncControlTemplate<ListBoxItem>((_, _) =>
                        new ContentPresenter
                        {
                            Name = "PART_ContentPresenter",
                            [~ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty),
                            [~ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty),
                        })),
                },
            },
        };
    }
}
