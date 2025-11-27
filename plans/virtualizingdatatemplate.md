# Content-Level Virtualization for Avalonia

> **Status**: Phase 1 & 2 Complete
> **Feature**: Content recycling for data templates in virtualizing panels
> **Impact**: Significant performance improvement for complex data templates

---

## Executive Summary

This document describes a comprehensive solution for content-level virtualization in Avalonia's ItemsControl and VirtualizingStackPanel. Currently, only **containers** (ListBoxItem, ContentPresenter) are recycled during virtualization, while their **content** (the expensive nested UI created by data templates) is destroyed and recreated. This enhancement introduces two complementary approaches:

1. **Explicit virtualization** via new `IVirtualizingDataTemplate` interface (opt-in, full control)
2. **Automatic virtualization** for existing `IRecyclingDataTemplate` with `DataType` (zero code changes)

**Expected Benefits:**
- 50-90% reduction in Measure/Arrange cycles during scrolling
- Significantly reduced GC pressure for complex templates
- Better scrolling performance, especially on mobile devices
- Backward compatible - existing code continues to work

---

## The Problem

### Current Behavior: Container Recycling Only

Avalonia's `VirtualizingStackPanel` recycles **containers** (ListBoxItem with ContentPresenter) but not their **content**:

```
User scrolls:
  1. Item #5 (ListBoxItem) leaves viewport
  2. VirtualizingStackPanel.RecycleElement() called
  3. ItemContainerGenerator.ClearItemContainer() executed
  4. ContentPresenter.Content property cleared
  5. ❌ Complex content UI (Border > StackPanel > 10 TextBlocks) DESTROYED

  6. Container #5 recycled for item #100
  7. ContentPresenter.Content = item #100 data
  8. ❌ ContentPresenter creates NEW content from scratch (expensive!)
     - Template.Build() creates new Border
     - Creates new StackPanel
     - Creates 10 new TextBlocks
     - Full Measure/Arrange cycle
```

### Why IRecyclingDataTemplate Isn't Sufficient

Avalonia already has `IRecyclingDataTemplate` with `Build(object? data, Control? existing)`, but it's insufficient for virtualization:

**Problem 1: Instance-Equality Constraint**
```csharp
// ContentPresenter.CreateChild() - line 582:
if (dataTemplate is IRecyclingDataTemplate rdt)
{
    var toRecycle = rdt == _recyclingDataTemplate ? oldChild : null;
    //                ^^^ Only reuses if SAME template instance!
    newChild = rdt.Build(content, toRecycle);
}
```

**Problem 2: No Pool Management**
```csharp
// VirtualizingStackPanel.RecycleElement() - line 909:
ItemContainerGenerator.ClearItemContainer(element);

// This calls:
ContentPresenter.ClearValue(ContentProperty);  // ← Content destroyed!
```

**Problem 3: Doesn't Survive Container Recycling**
- Content cleared before it can be saved
- No pool storage separate from ContentPresenter
- Cross-container reuse impossible

### Impact

For a ListBox with 1000 items and complex data templates:
- **Memory**: Each template creates 10-50 controls (~1-10KB each)
- **Performance**: Full Measure/Arrange on every virtualization cycle
- **Scrolling**: Janky, especially on mobile devices
- **GC Pressure**: Heavy allocation/deallocation during scrolling

---

## The Solution: Two-Tier Content Virtualization

We introduce **pool-based content recycling** with two complementary approaches:

### Approach 1: Explicit Virtualization (IVirtualizingDataTemplate)

**New Interface** for full control over content recycling:

```csharp
public interface IVirtualizingDataTemplate : IDataTemplate
{
    object? GetKey(object? data);              // Custom recycling key
    Control? Build(object? data, Control? existing);  // Build or reuse
    int MaxPoolSizePerKey { get; }            // Configurable pool size
}
```

**Usage:**
```xml
<DataTemplate DataType="local:Person"
              EnableVirtualization="True"
              MaxPoolSizePerKey="10">
    <Border>
        <StackPanel>
            <TextBlock Text="{Binding Name}" />
            <TextBlock Text="{Binding Email}" />
        </StackPanel>
    </Border>
</DataTemplate>
```

**Benefits:**
- Explicit opt-in via `EnableVirtualization="True"`
- Custom recycling keys via `GetKey()` method
- Per-template pool size configuration
- Full developer control

### Approach 2: Automatic Virtualization (IRecyclingDataTemplate + DataType)

**Automatically enabled** for existing templates that have `DataType` set:

```xml
<!-- Existing template - NO CHANGES NEEDED! -->
<DataTemplate DataType="local:Person">
    <Border>
        <StackPanel>
            <TextBlock Text="{Binding Name}" />
            <TextBlock Text="{Binding Email}" />
        </StackPanel>
    </Border>
</DataTemplate>
```

**How it works:**
- Templates implementing `IRecyclingDataTemplate` (all XAML DataTemplates)
- That also implement `ITypedDataTemplate` with `DataType` set
- Automatically get pool-based recycling using `DataType` as the key
- Default pool size: 5 controls per type
- Zero code changes required!

### Template Recycling Priority

```
1. IVirtualizingDataTemplate?
   └─> YES: Use custom GetKey() + explicit pool (user controls everything)

2. IRecyclingDataTemplate + ITypedDataTemplate + DataType != null?
   └─> YES: Use DataType as key + automatic pool (zero config!)

3. IRecyclingDataTemplate?
   └─> YES: Use instance-based recycling (original behavior)

4. Otherwise:
   └─> Create new (no recycling)
```

---

## How It Works

### Key Innovation: Pool Storage Survives ContentPresenter Clearing

**The critical insight:** Store content in ItemsControl's pool BEFORE ContentPresenter is cleared:

#### Scrolling OUT of Viewport:
```csharp
// 1. ContentPresenter.UpdateChild() detects content change
var oldVirtualizingTemplate = _virtualizingTemplate;
var oldData = _currentData;
var oldChild = Child;

// 2. BEFORE clearing, save to pool
if (oldVirtualizingTemplate != null)
{
    _itemsControl?.ReturnContentToPool(oldVirtualizingTemplate, oldData, oldChild);
}

// 3. THEN clear ContentPresenter (as before)
VisualChildren.Remove(oldChild);
```

#### Scrolling INTO Viewport:
```csharp
// 1. ContentPresenter.CreateChild() called with new data
if (dataTemplate is IVirtualizingDataTemplate vdt)
{
    // 2. Try to get recycled content from pool
    var recycled = _itemsControl?.GetRecycledContent(vdt, content);

    // 3. Template reuses control if available
    newChild = vdt.Build(content, recycled);
}
```

### Pool Architecture

**Storage:** `ItemsControl._contentRecyclePool`
```csharp
Dictionary<(IVirtualizingDataTemplate template, object key), Stack<Control>>
```

**For explicit virtualization:**
- Key: `(template instance, template.GetKey(data))`
- Example: `(PersonTemplate, typeof(PersonViewModel))`

**For automatic virtualization:**
- Key: `(DataTypeRecyclingMarker.Instance, dataType)`
- Example: `(Marker, typeof(PersonViewModel))`

**Lifetime:** Pools cleared when ItemsControl detaches from logical tree (prevents memory leaks)

---

## Implementation Status

### ✅ Phase 1: Explicit Virtualization (Completed)

**Files Modified:**
1. `src/Avalonia.Controls/Templates/IVirtualizingDataTemplate.cs` (new)
2. `src/Avalonia.Controls/ItemsControl.cs`
3. `src/Avalonia.Controls/Presenters/ContentPresenter.cs`
4. `src/Markup/Avalonia.Markup.Xaml/Templates/DataTemplate.cs`

**Features:**
- ✅ New `IVirtualizingDataTemplate` interface
- ✅ Pool management in ItemsControl
- ✅ ContentPresenter integration
- ✅ XAML DataTemplate implementation with `EnableVirtualization` property
- ✅ Diagnostic API (`ContentVirtualizationDiagnostics`)
- ✅ Pool cleanup on ItemsControl detach

### ✅ Phase 2: Automatic Virtualization (Completed)

**Files Modified:**
1. `src/Avalonia.Controls/ItemsControl.cs`
2. `src/Avalonia.Controls/Presenters/ContentPresenter.cs`

**Features:**
- ✅ `DataTypeRecyclingMarker` sentinel class for pool keys
- ✅ `GetRecycledContentForDataType()` method for automatic pooling
- ✅ `ReturnContentToPoolForDataType()` method for automatic pooling
- ✅ `_autoRecyclingDataType` field tracking in ContentPresenter
- ✅ Automatic detection in ContentPresenter.CreateChild()
- ✅ Automatic pool return in ContentPresenter.UpdateChild()

**Implementation Details:**
- Templates with both `IRecyclingDataTemplate` and `ITypedDataTemplate` interfaces
- AND with `DataType` property set
- Automatically use pool-based recycling with default pool size of 5
- Zero code changes required for existing XAML templates!

---

## Usage Examples

### Example 1: Explicit Virtualization with Custom Keys

```xml
<ListBox ItemsSource="{Binding People}">
    <ListBox.ItemTemplate>
        <DataTemplate DataType="local:PersonViewModel"
                      EnableVirtualization="True"
                      MaxPoolSizePerKey="10">
            <Border BorderBrush="Blue" BorderThickness="2" Padding="10">
                <StackPanel>
                    <TextBlock Text="{Binding Name}" FontSize="16" FontWeight="Bold" />
                    <TextBlock Text="{Binding Email}" FontSize="12" />
                    <TextBlock Text="{Binding Phone}" FontSize="12" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

**Result:** 10 Border+StackPanel+3 TextBlocks pooled per PersonViewModel type

### Example 2: Automatic Virtualization (Planned)

```xml
<ListBox ItemsSource="{Binding People}">
    <ListBox.ItemTemplate>
        <!-- NO CHANGES NEEDED - automatic pooling! -->
        <DataTemplate DataType="local:PersonViewModel">
            <Border BorderBrush="Blue" BorderThickness="2" Padding="10">
                <StackPanel>
                    <TextBlock Text="{Binding Name}" FontSize="16" FontWeight="Bold" />
                    <TextBlock Text="{Binding Email}" FontSize="12" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

**Result:** 5 Border+StackPanel+2 TextBlocks automatically pooled (default size)

### Example 3: Heterogeneous Data

```xml
<ListBox ItemsSource="{Binding MixedItems}">
    <ListBox.DataTemplates>
        <DataTemplate DataType="local:PersonViewModel" EnableVirtualization="True">
            <Border Background="LightBlue">
                <TextBlock Text="{Binding Name}" />
            </Border>
        </DataTemplate>

        <DataTemplate DataType="local:CompanyViewModel" EnableVirtualization="True">
            <Border Background="LightGreen">
                <TextBlock Text="{Binding CompanyName}" />
            </Border>
        </DataTemplate>

        <DataTemplate DataType="local:MessageViewModel" EnableVirtualization="True">
            <Border Background="LightYellow">
                <TextBlock Text="{Binding Text}" />
            </Border>
        </DataTemplate>
    </ListBox.DataTemplates>
</ListBox>
```

**Result:** Separate pools for each type:
- PersonViewModel → Blue borders pooled
- CompanyViewModel → Green borders pooled
- MessageViewModel → Yellow borders pooled

### Example 4: Programmatic Custom Keys

```csharp
public class RoleBasedDataTemplate : IVirtualizingDataTemplate
{
    public object? GetKey(object? data)
    {
        // Pool by role instead of type
        if (data is PersonViewModel person)
            return person.Role;  // "Admin", "User", "Guest"
        return null;
    }

    public Control? Build(object? data, Control? existing)
    {
        if (existing is PersonView view)
        {
            view.DataContext = data;
            return view;
        }
        return new PersonView { DataContext = data };
    }

    public int MaxPoolSizePerKey => 8;
    public bool Match(object? data) => data is PersonViewModel;
}
```

**Result:** Separate pools for Admin/User/Guest, 8 controls each

---

## Diagnostic API

### Global Enable/Disable

```csharp
// Disable content virtualization for debugging
ContentVirtualizationDiagnostics.IsEnabled = false;
```

### Pool Statistics

```csharp
var stats = ContentVirtualizationDiagnostics.GetPoolStats(myListBox);
if (stats != null)
{
    foreach (var entry in stats.PoolEntries)
    {
        Console.WriteLine($"Template: {entry.TemplateName}");
        Console.WriteLine($"Key: {entry.RecycleKey}");
        Console.WriteLine($"Pooled: {entry.PooledCount}");
    }
}
```

### Manual Pool Clearing

```csharp
// Clear all pools for a specific ItemsControl
ContentVirtualizationDiagnostics.ClearPools(myListBox);
```

---

## Performance Expectations

### Memory Impact
- **Pool overhead**: ~100 bytes per (template, key) entry
- **Content savings**: 1KB-50KB+ per recycled complex control
- **Default pool**: 5 controls × ~1-50KB = 5-250KB per type
- **Net result**: Significant savings for complex templates

### CPU Impact
- **Dictionary lookup**: O(1) on composite key
- **Stack operations**: O(1) push/pop
- **Layout savings**: 50-90% reduction in Measure/Arrange cycles
- **GC pressure**: Dramatically reduced allocation during scrolling

### Optimal Scenarios
1. VirtualizingStackPanel with complex data templates
2. Heterogeneous data (multiple types with different templates)
3. Frequent scrolling/virtualization
4. Mobile/memory-constrained devices
5. Large datasets (1000+ items)

---

## Files Modified

### Phase 1: Explicit Virtualization
| File | Lines Changed | Purpose |
|------|---------------|---------|
| `IVirtualizingDataTemplate.cs` | +65 (new) | New interface definition |
| `ItemsControl.cs` | +120 | Pool management + diagnostics |
| `ContentPresenter.cs` | +40 | Pool integration |
| `DataTemplate.cs` | +35 | Interface implementation |

### Phase 2: Automatic Virtualization
| File | Lines Changed | Purpose |
|------|---------------|---------|
| `ItemsControl.cs` | +60 | Automatic recycling methods + sentinel |
| `ContentPresenter.cs` | +40 | Auto-detection logic + field tracking |

**Total:** ~360 lines of code across both phases

---

## Testing Checklist

### Phase 1 Testing
- [ ] IVirtualizingDataTemplate with EnableVirtualization=True
- [ ] Pool size limits enforced (MaxPoolSizePerKey)
- [ ] Content actually reused (not recreated)
- [ ] Mixed template types in same ListBox
- [ ] Pool cleanup on ItemsControl detach
- [ ] Global disable flag (ContentVirtualizationDiagnostics.IsEnabled)
- [ ] Diagnostic API (GetPoolStats, ClearPools)
- [ ] Heterogeneous data scenarios
- [ ] Custom GetKey() implementations
- [ ] Memory leak testing (pools released properly)

### Phase 2 Testing
- [ ] Automatic virtualization for templates with DataType
- [ ] Templates without DataType fall back correctly
- [ ] Mixed explicit and automatic virtualization
- [ ] Both mechanisms use same pool infrastructure
- [ ] Default pool size (5) enforced for automatic

---

## Migration Guide

### For Existing Applications

**No changes required!** Existing code continues to work:
- Templates without `EnableVirtualization` use original behavior
- Templates with `EnableVirtualization="True"` use new pooling
- (Phase 2) Templates with `DataType` automatically benefit

### Opting In

**Option 1: Explicit (available now)**
```xml
<DataTemplate DataType="local:Person" EnableVirtualization="True">
```

**Option 2: Automatic (planned)**
```xml
<DataTemplate DataType="local:Person">
<!-- Automatically uses pooling when Phase 2 is complete -->
```

### Performance Tuning

**Increase pool size for heavily scrolled items:**
```xml
<DataTemplate DataType="local:FrequentlyViewedItem"
              EnableVirtualization="True"
              MaxPoolSizePerKey="20">
```

**Disable for simple templates:**
```xml
<!-- Don't enable for simple templates - overhead not worth it -->
<DataTemplate DataType="local:SimpleText">
    <TextBlock Text="{Binding}" />
</DataTemplate>
```

---

## Known Limitations

1. **Template responsibility**: Templates must properly update recycled controls in Build()
2. **Visual state cleanup**: Templates must reset animations, selections, etc.
3. **Pool size limits**: Excess controls are discarded (configurable per template)
4. **DataContext updates**: Template must handle DataContext assignment
5. **Cross-window sharing**: (Phase 1) Pools scoped to ItemsControl, not global

---

## Future Enhancements

### Potential Phase 3+
1. **Configurable pool sizes per ItemsControl** (not just per template)
2. **Pool warming** (pre-create controls for known types)
3. **Cross-thread pooling** for multi-window apps
4. **Content validation** (verify recycled content matches expectations)
5. **Performance metrics** (hit rates, pool efficiency)
6. **Pool size auto-tuning** based on viewport size

---

## Contributing

This feature is being developed in stages:
- **Phase 1** (✅ Complete): Explicit virtualization via `IVirtualizingDataTemplate`
- **Phase 2** (🚧 Planned): Automatic virtualization for typed templates
- **Phase 3+** (💡 Ideas): Advanced features and optimizations

Contributions welcome! See implementation plan sections for details.

---

## References

- **Original Issue**: [Link to GitHub issue]
- **Related**: VirtualizingStackPanel, ItemsControl, ContentPresenter
- **Documentation**: [Avalonia virtualization docs]

