using System.Collections.ObjectModel;
using Avalonia.Controls.Templates;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Collection changes against both panels: 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_ItemsChanged : LazyStackPanelTestBase
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Items_Added_To_An_Empty_List_Before_A_Layout_Show_From_The_Top(bool lazy)
        {
            var items = new ObservableCollection<string>();
            var ic = new ItemsControl { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };
            var (window, _) = Show(ic, items, lazy);

            for (var i = 0; i < 20; ++i)
                items.Add($"Item {i}");
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(0, info.FirstVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset);
            Assert.Equal(9, info.LastVisibleIndex);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Items_Added_After_All_Were_Removed_Show_From_The_Top(bool lazy)
        {
            var items = new ObservableCollection<string>(new[] { "a", "b", "c" });
            var ic = new ItemsControl { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };
            var (window, _) = Show(ic, items, lazy);

            items.Clear();
            for (var i = 0; i < 20; ++i)
                items.Add($"Item {i}");
            RunLayout(ic);

            Assert.Equal(0, ItemsScroll.Get(ic).FirstVisibleIndex);
            window.Close();
        }
    }
}
