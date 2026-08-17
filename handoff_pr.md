# Handoff — what's still open on #20259

Branch `feature/20259_virtualizingdatatemplate_master` → upstream draft PR
[AvaloniaUI/Avalonia#20993](https://github.com/AvaloniaUI/Avalonia/pull/20993) (base `main`).

Design knowledge lives in `virtualization.md`. This file is only the open work.

## 1. Status

**Code defects: none known.** All five defects the earlier version of this file listed are closed —
see §2 for what each fix was, so a reviewer's question can be answered without re-deriving it. A
sixth, found by closing the last coverage gap (warmup running on a detached panel), is written up
in §3.

**Heuristic removal is complete and verified.** Grep across `src/` returns zero hits for
`_frozenExtentU`, `_extentOscillation*`, `CompensateForExtentChange`, `_consecutiveMeasureCount`,
`_measurePostponed`, `_suppressValidateStartU`, `lockSizes`, `WarmupSampleSize`,
`_lastEstimateFirstIndex/LastIndex`, `_lastMeasuredExtentU`, `_viewportAnchorU`, `IsTracingEnabled`,
`_contentRecyclePool`, `DataTypeRecyclingMarker`, `_recycledContentToUse`. The constants inventory in
`virtualization.md` §11 has no open row.

**Tests: `Avalonia.Controls.UnitTests` is fully green — 3840 cases, 3839 passed, 0 failures** (the one
skip is a pre-existing `CalendarDatePicker` skip), and `Avalonia.Markup.Xaml.UnitTests` — 591 cases,
590 passed, 0 failures, again one pre-existing skip. Both re-run on 2026-08-15 after the §5 fixes were
committed. No known-failing baseline any more: the
three failures the earlier version of this file called pre-existing
(`ItemsControlTests.ItemContainerTheme_Can_Be_Changed_Virtualizing`,
`ListBoxTests.Handles_Resetting_Items_With_Existing_Selection_And_AutoScrollToSelectedItem`,
`ListBoxVirtualizationIssueTests.GhostItemTest_FocusManagement`) are fixed.

| Test file | Tests | Cases |
|---|---|---|
| `VirtualizingStackPanelTests.cs` | 153 (from 73 stock, +80) | 310 |
| `ContainerVirtualizationTests.cs` (new) | 21 | 21 |
| `Presenters/ContentPresenterTests_BatchUpdate.cs` (new) | 8 | 8 |

Production diff against `master`: `VirtualizingStackPanel.cs` +1309/-127, `Utils/RealizedStackElements.cs`
+150/-11, `ItemsControl.cs` +150/-10, `ContentPresenter.cs` +50/-3,
`Templates/FuncDataTemplate.cs` +48/-2, `Markup.Xaml/Templates/DataTemplate.cs` +38/-1,
`Templates/IVirtualizingDataTemplate.cs` +34, `VirtualizingPanel.cs` +14/-1. Benchmarks add
`tests/Avalonia.Benchmarks/Controls/` (7 files) and one hook in its `Program.cs`.

What remains is **not** defects and not writing: the PR description and the docs are done (§4, §5).
What is left is the upstream questions in §5, all of which are asked and waiting on an answer.

## 2. Defects — all closed, and how

Kept because these are the five things a reviewer is most likely to ask about.

### (a) Every plain XAML `DataTemplate` capped the container pool at 5 — *fixed*

`PushToRecyclePool` no longer inspects `ItemsControl.ItemTemplate` directly. It asks
`ItemsControl.GetMaxPoolSizePerKey(recycleKey)`, which caps **only** keys an
`IVirtualizingDataTemplate` handed out and resolves the template through the same
`GetEffectiveItemTemplate()` path that `NeedsContainer<T>` keys on — so keying and capping can no
longer disagree for a `DisplayMemberBinding` template or a `DataTemplates` collection. Containers
under `DefaultRecycleKey` are pooled uncapped, as in stock.

Covered by `Plain_DataTemplate_Does_Not_Cap_Recycle_Pool`,
`MaxPoolSizePerKey_Is_Respected_For_DataTemplate_With_EnableVirtualization`,
`DisplayMemberBinding_Template_Keys_And_Caps_Consistently`.

### (b) Nothing was opt-in — *fixed, and the story is now one story*

Virtualization is opted into **per template**: implement `IVirtualizingDataTemplate` and return a
non-null key from `GetKey`, which for a XAML `DataTemplate` means `EnableVirtualization="True"`
(default `false`). Every site now keys on *that* rather than on the template type implementing an
interface — `NeedsContainer<T>` and both skip-clear branches of `ClearContainerForItemOverride` test
`GetKey(item) != null`. `ContainerVirtualization.IsEnabled` stays `true` by default but is
now only a **kill switch** (documented as such): setting it `false` forces every `ItemsControl` back
to stock recycling. A `<DataTemplate DataType="local:Foo">` that did not opt in therefore behaves
exactly as in stock, `DefaultRecycleKey` and all.

Covered by `IsEnabled_False_Forces_Default_Recycle_Key_And_Clears_Content`,
`Typed_DataTemplate_Without_Opt_In_Clears_Content_On_Recycle`,
`Opted_In_Template_Keeps_Child_Attached_Across_Recycling`.

### (c) Dead public API and a dead sample — *fixed*

`GetPoolStats`, `ClearPools`, `ContentPoolStats` and `PoolEntry` are deleted;
`ContainerVirtualization` is now just the kill switch. The `ListBoxPage` 1-second
`DispatcherTimer` and its mutation of the process-global `IsEnabled` from a page constructor are gone.

### (d) Unflagged cost of the root-cause fix — *fixed, and now disclosed*

- `EstimateElementSizeU` no longer sums the record: `_measuredSizesSum` is maintained incrementally at
  the single upsert site, with Neumaier compensation so it stays within a rounding of a fresh sum
  however many updates it has seen. A measure pass is O(realized window), as in stock. The comment
  that claimed "no separate sweep" is now true.
- Insert/remove **remap in place**; an append (the infinite-scroll case) moves nothing and allocates
  nothing.
- The record is still **unbounded** — one entry per item ever measured, released only on `Move`, a
  non-preserving `Reset`, a re-attach, or `StartU` going unstable. That is deliberate (trimming to the
  realized window is exactly the window-dependence the record exists to remove) and is now written
  down in `virtualization.md` §2 "What the record costs" and §9, with tests that pin it:
  `Measured_Size_Record_Holds_One_Entry_Per_Item_Ever_Measured`,
  `Measured_Size_Record_Is_Not_Trimmed_When_Items_Leave_The_Viewport`,
  `Measured_Size_Record_Is_Dropped_On_Reset`, `Measured_Size_Record_Is_Dropped_On_Move`,
  `Measured_Size_Record_Survives_Detach_But_Not_A_Re_Attach`. **This paragraph has to reach the PR
  body** — it is the first thing a reviewer will profile.

### (e) `_templateCache` was never invalidated — *fixed, then deleted*

First hooked to `DataTemplates.CollectionChanged`. Then measured, and deleted: on the
`DataTemplates`-collection path — the only path it served — the cache was **inert**, because
`PrepareItemContainer` runs before `AddInternalChild` (stock ordering), so the first lookup for an item
type happens on an unparented container, finds nothing and memoizes `null` for good. Making it work
instead of deleting it would have been actively harmful: it would set `ContentTemplate` from a
collection template while `NeedsContainer` still returned `DefaultRecycleKey`, so an
`EnableVirtualization="True"` template in a `DataTemplates` collection would buy the skip-clear while
its containers sat in the single shared pool — a container handed a different item type with the
previous item's `Child` still attached. `ItemsControl` now resolves `ContentTemplate` from
`ItemTemplate` / `DisplayMemberBinding` only, exactly as stock, and the `ContentPresenter` resolves
collection templates itself. Full rationale in `virtualization.md` §12.

Covered by `DataTemplates_Collection_Template_Is_Never_Copied_Onto_The_Container`,
`Template_Swapped_In_DataTemplates_At_Runtime_Is_Picked_Up`,
`ItemTemplate_Is_Still_Applied_To_The_Container`,
`DataTemplates_Collection_Does_Not_Cause_Repeated_Measures` (the last one pins that deleting the cache
did not reintroduce the layout cycle it was justified by).

## 3. Missing test coverage

The `ItemsControl` / `ContentPresenter` / `DataTemplate` half of the PR is no longer untested: the 23
tests in `ContainerVirtualizationTests.cs` + `ContentPresenterTests_BatchUpdate.cs` cover the kill
switch, pool capping for plain and opted-in templates, `GetKey` / `Build(data, existing)` /
`EnableVirtualization`, the typed-template skip-clear, `NeedsContainer<T>` ordering,
`SetIfUnsetOrDifferent`, template resolution, nested/recursive virtualization, and batch update.

**Horizontal orientation is now covered.** The five areas that had none — the size record, the
adversarial shapes, warmup, the collection-edit matrix and pre-anchor compensation — run as
theories over `Orientation`, 36 methods and +66 cases (236 → 302 in the file at the time). No production change
was needed: the panel already works in (u, v) space. The tests express that through the
`OffsetU` / `ExtentU` / `BoundsStartU` / `BoundsSizeU` / `CreateItemsU` / `ItemTemplateU` / `CanvasU`
accessors in `virtualization.md` §10, so a case reads the axis it is actually scrolling rather than
a hard-coded Y.

Verified red→green with two axis-only defects, each of which failed **only** horizontal cases and
no vertical one: forcing `GetElementSizeU` to read `DesiredSize.Height` (27 red, 18 of them new) and
forcing `CalculateMeasureViewport` to read `viewport.Y`/`.Bottom` (50 red, 41 of them new). The
adversarial shapes survived both, because a wrong axis used *consistently* stays self-consistent —
so `Adversarial_CrossRegion_Extent_Is_Window_Independent` now also asserts the extent converged on
the true 34000px total, not merely that it stopped moving. That assertion is what makes the
horizontal case fail under the viewport probe; without it a never-scrolling window reports the same
head-only estimate at every offset and the spread check passes on nothing.

**Warmup vs. the visual tree is now covered — and it found a leak.** Warmup posts to the dispatcher
from `OnAttachedToVisualTree` and runs a frame later; nothing drove that posted job (every existing
warmup test called `PerformWarmup()` directly), so nothing covered a detach landing in between. It
does not throw — but it does not skip either: with the panel out of the visual tree but still
attached to its `ItemsControl`, `Items` and the generator are live and warmup built its full pool
(5 containers in the test), parked in `Children` and `_recyclePool` for the rest of the panel's life
having bought nothing. `PerformWarmup` now returns early when `!IsAttachedToVisualTree`, leaving
`_isWarmupComplete` false so a re-attach posts the work again. `VirtualizingStackPanel.cs` +11.

Three theories over `Orientation` (6 cases): `Warmup_Is_Posted_By_Attach_And_Runs_On_The_Dispatcher`
(control — the other two assert nothing happens, which only means something if something otherwise
does), `Warmup_Detached_Before_Its_Dispatcher_Tick_Neither_Throws_Nor_Builds_Containers`,
`Warmup_Skipped_While_Detached_Runs_When_The_Panel_Is_Attached_Again`. Verified red→green twice:
disabling the post fails the two post-dependent tests and *not* the detach one, and making the guard
cancel (`_isWarmupComplete = true`) instead of postpone fails only the re-attach test.

**The benchmark gap is closed.** Prepare/measure counts used to be asserted only as *bounds*, never
measured against stock. `tests/Avalonia.Benchmarks/Controls/` now measures them, and the results are
in §5 — including one that does not say what the PR currently claims.

Per `virtualization.md` §10, verify each new test **red→green**: break the specific code it covers,
confirm *that* test fails and no other does.

**Process note, worth not repeating.** `ContentPresenterTests_BatchUpdate.cs` shipped with three tests
that pinned defects (`_deferUpdateChild` as a bool, unbalanced `End` rebuilding, `:empty` never
refreshed) which the *same commit's* `ContentPresenter` already fixed — a depth counter, a no-op
unbalanced `End`, and `UpdatePseudoClasses` in `EndBatchUpdate`. They were red on `master` until
rewritten to the shipped contract (`Batches_Nest_So_Only_The_Outermost_End_Publishes`,
`EndBatchUpdate_Without_A_Matching_Begin_Does_Nothing`,
`A_Batch_Refreshes_The_Empty_PseudoClass_When_It_Ends`). Characterization tests written from a
description of the code, rather than run against it, document the wrong thing and read as intentional.

## 4. The PR description — done

**Rewritten and live on the PR** (2026-08-15). Nothing here is open work; the section is kept as the
record of *what the body now says*, so a later edit does not have to re-derive it, and so a scope
split knows which paragraphs travel with which half.

The heuristic advertising is gone (layout cycle breaker, sub-pixel tolerance, extent oscillation and
frozen extent, boundary clamping, stale anchor guard, estimate caching, dampened compensation), as
are the wrong figures and the wrong file list. Specifically corrected against the real diff:
`IVirtualizingDataTemplate : IRecyclingDataTemplate` (not `: IDataTemplate`), no `WarmupSampleSize`,
the sample is `ListBoxComplexLayoutPage.{xaml,xaml.cs}` + `FieldTemplateSelector.cs` +
`MarkdownToInlinesConverter.cs` + `ListBoxComplexLayoutPageViewModel.cs`, `ItemsControl.cs` is
**+150/-10** with no template cache, `ContentPresenter.cs` +50/-3, `RealizedStackElements.cs` is
`NullifyElement` plus `ValidateStartU`'s new signature, and `CacheLength` is not in the diff at all.
Test figures are the measured ones: 153 methods / 310 cases in `VirtualizingStackPanelTests`, 21 in
`ContainerVirtualizationTests`, 8 in `ContentPresenterTests_BatchUpdate`.

Added since: a `[!IMPORTANT]` callout directly under the three-item summary, stating that an opted-in
template's controls are only hidden and never leave the visual tree, so `Loaded` / `Unloaded` and
`AttachedToVisualTree` / `DetachedFromVisualTree` fire once and never per item. That is the one way
this feature breaks a working list, and it is now the first thing a reviewer meets rather than a
Breaking-changes footnote.

### What the body leads with, and why

1. **Root cause, not dampers.** Persistent per-item size record replaces the realized-window average;
   extent becomes reproducible across revisits and a provable no-op for uniform items. Lead with the
   constants-inventory table (`virtualization.md` §11) — it is the single strongest asset in this
   change: *every fork-added tuning constant is gone.* State the record's memory cost in the same
   breath (§2 (d)); it is the obvious follow-up question and the answer is good — 43.7 bytes per item,
   4.2 MiB at 100k, and no per-pass cost at all, all measured in §5a.
2. **Tier A correctness fix.** Reset-preservation only when *every* realized element still validates;
   fixes wrong-index render on a mid-list edit coalesced into a `Reset`. Cite
   `Reset_With_MidList_Insert_Realizes_Shifted_Items_At_Correct_Index` and the 28-case
   `Collection_Edit_Keeps_Every_Container_On_Its_Own_Item` matrix.
3. **Constant-free anchor compensation** in `ValidateStartU` (`StartU -= preDelta`,
   `LayoutHelper.LayoutEpsilon` for float noise, one `GetElementSizeU` accessor for record-and-recheck).
4. **Container-level virtualization, opt-in per template** — `IVirtualizingDataTemplate`,
   `EnableVirtualization="True"`, type-aware recycle keys, child stays attached.
   `ContainerVirtualization.IsEnabled` is a kill switch, not the opt-in. **This is the
   change's actual performance payload and should lead the PR alongside item 1** — over heterogeneous
   complex rows it is ~3.4× faster on scroll, ~2.4× on jumps, allocates 83% less and has ~20× lower
   run-to-run variance, with *identical* container prepare counts (§5a). Item 1 is the correctness and
   constants argument; this is the speed argument. The current PR body leads with neither.
5. **`RetainMatchingContainers`** + `NullifyElement` — keyed on item identity, so correctness-safe.
   The prepare-count benchmarks it wanted now exist (§5a): ~10% fewer prepares on backwards scrolling
   and paging, nothing on jumps or forward scrolling. Real, but small enough that it belongs in its
   own PR rather than in this one's headline.
6. **Opt-in warmup**, default off, pool grows off encountered keys, no head sampling, and skipped
   (not cancelled) when its dispatcher tick lands on a panel that has left the visual tree.
7. **The zero-viewport guard**, with the camera trace from `virtualization.md` §7 as justification —
   it reads as a defensive nicety otherwise, and a reviewer will ask to delete it.
8. **Accepted trades**, verbatim from `virtualization.md` §9: never-settling templates iterate to the
   `LayoutManager` cap; view lifecycle events don't fire per item (only for templates that opted in);
   `Panel.Children` retains invisible pooled containers; the size record's memory.

## 5. Still open before upstream will take this

### Closed since the last revision

- **PR description — done** (§4), and the three fixes below are no longer only in the working tree:
  they are commit `2c7e2b3564` and pushed, so the diff, the description and the docs agree on the
  API names. Both suites were re-run against that commit before it went up.
- **Docs PR — done.** [AvaloniaUI/avalonia-docs#1113](https://github.com/AvaloniaUI/avalonia-docs/pull/1113),
  draft against `main`, from `C:\Code_gh\avalonia-docs_gd` on a branch of the same name as this one.
  See "Docs PR" below for the constraints it ships under.
- **Scope split — asked, not decided.** Put to MrJul on the PR
  ([comment](https://github.com/AvaloniaUI/Avalonia/pull/20993#issuecomment-5300761189)) as a
  concrete proposal rather than an open question. Now waiting on upstream, not on us.
- **Benchmarks — done.** Figures and method in §5a. The first attempt measured the wrong thing; §5a
  says how, because it is the mistake most available to whoever runs these next.
- **`FuncDataTemplate` opt-in — done** (`587b46162a`). Was on the "optional" list; the benchmark is
  what promoted it. Container virtualization was reachable only from XAML, so every `ItemTemplate`
  written in C# — the majority — was locked out of the change's headline feature. The opt-in is a
  `RecycleKeySelector`, not a bool, because the key must identify the *shape the build function
  produced*: a template branching on a property builds several subtrees for one CLR type, which
  `DataType`-based keying cannot express. It also flushed out the warmup-depth bug below.

### Fixed before review — three things a reviewer would have found first

All three sat uncommitted in the working tree until 2026-08-15, when the PR description was rewritten
to describe them. They are commit `2c7e2b3564`. If a future session finds this section describing
something the diff does not show, check `git status` before believing either.

**`DataTemplate.MinPoolSizePerKey` was `{ get; } = 2`, so XAML could not set it — *fixed*.** It is
documented as the warmup-depth knob and was unreachable from the markup that is supposed to configure
it, while `MaxPoolSizePerKey` one line above was `{ get; set; }`. Since
`FuncDataTemplate.MinPoolSizePerKey` is settable, the two opt-in APIs disagreed about the same
concept. The setter is in, covered by `MinPoolSizePerKey_Set_On_A_DataTemplate_Reaches_Warmup`, which
asserts the value reaches `DiscoverTemplateKeys` — and asserts `7`, not `DefaultWarmupPoolSizePerKey`
(3), so it fails if the template is never asked. Verified red→green with an inert setter
(`{ get => 2; set { } }`).

**`AdjustElementSize` was a `protected internal virtual` test seam — *fixed*.** The `protected` half
made it public API by accident, and nothing in production ever wants to change a measured size. It is
now `internal Func<int, double, double>? ElementSizeAdjustmentForTesting` on
`VirtualizingStackPanel`, applied in `GetElementSizeU` exactly where the method was called. The three
test panels that overrode it (`VirtualizingStackPanelWithInstability`,
`...WithSubPixelNoise`, `...AsyncGrow`) now assign the delegate in their constructors; behaviour is
unchanged and the suite is green. `TryGetMeasuredSizeForTesting` and `RecyclePoolForTesting` were
already `internal` and stay as they are.

**`ContentVirtualizationDiagnostics.IsEnabled` → `ContainerVirtualization.IsEnabled` — *renamed*.** A
behaviour kill switch does not belong in a class called `Diagnostics`, whatever upstream decides
about the rest of the naming; the class is now named for the feature it switches. Purely a rename —
no call site changed shape.

Related and already fixed, but worth knowing because the shape recurs: warmup used to read
`MinPoolSizePerKey` off `EffectiveVirtualizingItemTemplate`, a *type* test with no key check. That is
the same mistake as the bug where a plain XAML template capped the pool at 5 — **implementing
`IVirtualizingDataTemplate` is not the opt-in, handing out a key is** — and it only became visible
when `FuncDataTemplate` started implementing the interface inertly. Both call sites now go through
`ItemsControl.GetMaxPoolSizePerKey` / `GetMinPoolSizePerKey`, which guard on the key. Any *third*
place that reads a per-key figure off the template must do the same.

### Needs a decision from us

- **Draft → ready for review.** Nothing structural is blocking it. The one argument for staying in
  draft is that the scope-split answer may restructure the branches anyway, in which case flipping to
  ready first buys nothing.

The scope split used to sit here. It is now on the PR as a proposal: container virtualization is the
**speed** case (~3.4× and −83% allocation, all of it), items 1–3 are the **correctness and constants**
case and measure performance-neutral, and `RetainMatchingContainers` defers on its own evidence. Two
PRs plus a deferral, with an explicit offer to restructure however upstream prefers.

### Needs upstream's opinion, not more work from us

Everything below is **asked on the PR**, across two comments: the original
([5297416987](https://github.com/AvaloniaUI/Avalonia/pull/20993#issuecomment-5297416987)), which
carries the Reset-preservation and lifecycle questions and the separate-panel fallback, and a second
one addressed to MrJul
([5300761189](https://github.com/AvaloniaUI/Avalonia/pull/20993#issuecomment-5300761189)), which puts
the scope split as a concrete proposal and re-raises lifecycle events with the mechanism spelled out.
Waiting on an answer, not on us. If a nudge is needed, the split is the one that unblocks the most.

- **Is Reset-preservation wanted at all?** Stock treats `Reset` as a full rebuild. This whole concept
  is non-upstream; the scroll-anchor system may be the right owner instead. The comment spells out
  the coupling a reader would otherwise miss: **if preservation goes, the Tier A correctness fix goes
  with it**, because the wrong-index render only exists on the preserving path. Do not defend one
  without the other.
- **View lifecycle events.** Synthesise `Loaded`/`Unloaded` for recycled containers, add
  virtualization-aware equivalents, or document the trade? All three are implementable; each sets a
  framework-wide precedent for what those events mean under virtualization, which is why it is their
  call. Currently paid only by templates that opted in, and documented as a trade.
- **API review / naming** — `EnableVirtualization`, `RecycleKeySelector`, `MaxPoolSizePerKey` /
  `MinPoolSizePerKey`, `EnableWarmup`, and `ContainerVirtualization` as a home for a process-global
  switch at all. (`ContentVirtualizationDiagnostics` is gone — see above.)
- **Fallback if they don't want it in `VirtualizingStackPanel`.** If upstream would rather not carry
  container virtualization in the stock panel, the same implementation can ship as a separate panel
  (`ContentVirtualizingStackPanel`) that opts in by type instead of by flag. That is an acceptable
  outcome for us, and it is on the PR comment so they can pick it.

### Docs PR — open, and what it is waiting on

[AvaloniaUI/avalonia-docs#1113](https://github.com/AvaloniaUI/avalonia-docs/pull/1113), draft.
`docs/app-development/container-virtualization.md` plus a pointer from `performance.md`'s UI
virtualization section and a `sidebars.ts` entry. It covers both opt-in forms, the rule that the key
identifies the *subtree shape* rather than the data type, the `DataTemplates`-collection caveat, the
`IVirtualizingDataTemplate` selector for mixed rows, warmup, the size record's memory cost, the kill
switch, and a `:::danger` block for the lifecycle caveat. It says outright that the feature does
nothing measurable for cheap templates (§5a), because that is the question a reader actually has.

Three constraints on it, all stated in its own body:

- **It must not merge before #20993**, and the `:::info` line naming that PR has to become a release
  version when the API ships.
- **It is written against the current API names.** If the API review renames
  `EnableVirtualization` / `RecycleKeySelector` / `MaxPoolSizePerKey` / `MinPoolSizePerKey` /
  `EnableWarmup` / `ContainerVirtualization`, the page needs a pass.
- **The site was not built locally** (no `node_modules` in that clone), so link and MDX validation
  rests on the docs repo's CI. Worth checking the run before asking for review.

The clone is `C:\Code_gh\avalonia-docs_gd`, branch `feature/20259_virtualizingdatatemplate_master`,
`origin` = `gentledepp/avalonia-docs`, `upstream` = `AvaloniaUI/avalonia-docs` (the upstream remote
was added in that session; branch off `upstream/main`, not the fork's `main`). That repo ships its own
style rules in `.claude/skills/docs-style-lint`, and the page was written against them: `doc-type`
frontmatter, sentence-case headings, **no em or en dashes**, a language tag on every fence, `## See
also` at the end. Any edit has to keep those.

### Decided against — do not re-open without a new reason

- **Mobile numbers: we will not take them.** Every figure in §5a is desktop x64. The consequence is
  binding on how this is written up: the PR states the mobile case **directionally only** — the work
  avoided is subtree construction, binding setup and text layout, all of which cost proportionally
  more on a phone — and extrapolates no figure. Keep it that way. A reviewer asking "how much on
  Android?" gets "we have not measured it", not an estimate.
- **`VirtualizingPanel` base-class support: out of scope.** Was on the original checklist as
  optional; it stays unimplemented. `VirtualizingStackPanel` is the panel the change is about.

## 5a. Benchmark results

`tests/Avalonia.Benchmarks/Controls/`. Counts are deterministic, so they bypass BenchmarkDotNet
(`dotnet Avalonia.Benchmarks.dll --virtualization-report`); timings go through BDN
(`--filter "Avalonia.Benchmarks.Controls.*" --job medium --buildTimeout 900` — the default 120s build
timeout is not enough to rebuild the Avalonia chain into BDN's generated project, and the failure
reads like a compile error rather than a timeout).

**Two different comparisons, and they answer different questions. Do not mix them up.**

1. **Off vs. on, both on this branch** (`ComplexScrollBenchmark`, `Mode=Plain` vs `Virtualized`). This
   is the one that measures §4's container-level virtualization. It has to be an in-branch A/B because
   stock has no equivalent to compare against — and it is a *fair* one, because a template that does
   not opt in is put back on stock's exact path: `DefaultRecycleKey`, content cleared on recycle,
   subtree rebuilt. The `Plain` arm is measured at the merge-base too (below), which is what turns
   that from a claim into a check.
2. **Branch vs. merge-base worktree**, same stock-API harness on both. This measures the **panel**
   changes — size record, `RetainMatchingContainers`, Reset handling. Per §13 of `virtualization.md`
   the baseline is a **worktree, not a `git stash`**.

Everything in the directory is stock API except `ComplexVirtualizingTemplate.cs`; leave that one file
behind when copying into the baseline worktree and nothing else needs editing, because it registers
itself through a `[ModuleInitializer]` and `ComplexItems.AvailableModes` then reports only the arm
that exists.

**The trap this benchmark fell into first, since it is the obvious one to repeat.** The original
harness used a one-`Canvas` item template. Over a template that cheap there is nothing for container
virtualization to save — rebuilding the child is a single allocation — so the feature measured as
worthless and `RetainMatchingContainers` measured as noise. Both conclusions were artefacts of the
template. The rows below are four kinds × 10–20 visuals with bound text, which is what a form or feed
row actually is. **A virtualization benchmark's item template is not a detail of the harness; it is
the independent variable.**

### Container-level virtualization is the change's actual payload — 3.4× on scroll, −83% allocation

`ComplexScrollBenchmark`, 5,000 heterogeneous rows, MediumRun, i7-10750H. Two independent runs, both
shown, because the `Plain` arm's variance is the point of the third row:

| Scenario | Off (`Plain`) | On (`Virtualized`) | Δ |
|---|---:|---:|---:|
| ScrollDownAndBack | 24.08 / 19.80 ms | **6.48 / 6.35 ms** | ~3.4× faster |
| JumpToOffsets | 24.63 / 24.75 ms | **9.92 / 10.77 ms** | ~2.4× faster |
| Allocated, scroll | 7.85 MB | **1.36 MB** | −83% |
| Allocated, jumps | 11.68 MB | **2.00 MB** | −83% |
| StdDev, scroll | ±6.79 / ±3.75 ms | **±0.24 / ±0.19 ms** | ~20× steadier |

That last row is worth as much as the mean to a UI: jank is variance, not average cost.

The deterministic counts say *why*, and they are the cleanest evidence in the whole change — prepares
are **identical** in both arms, so nothing is being skipped; only the child subtree survives:

| Scenario | Prepares | Child builds | Visuals created |
|---|---|---|---|
| Wheel down+up, 80 steps | 80 → 80 | 160 → **5** | 1,562 → **69** |
| Page down+up, 20 steps | 51 → 51 | 102 → **0** | 986 → **0** |
| Jumps, 20 | 117 → 117 | 236 → **1** | 2,362 → **9** |

Once the per-kind pools have filled, paging rebuilds **nothing at all**. This is §4's
"container + child = one reusable unit" doing exactly what it says.

**Desktop x64 is a lower bound here.** The avoided work is subtree construction, binding setup and
text layout, all of which cost proportionally more on a phone — which is where this fork's lists run.
No mobile numbers have been taken; do not extrapolate a figure, but do say the direction.

### The panel changes alone are a wash on heavy templates, and a win on trivial ones

Same complex `Plain` template, branch vs. merge-base — the arm that isolates the panel:

| Scenario | Stock | Branch `Plain` |
|---|---:|---:|
| ScrollDownAndBack | 18.51 / 20.93 ms | 24.08 / 19.80 ms |
| JumpToOffsets | 22.69 / 23.72 ms | 24.63 / 24.75 ms |

**The ordering flips between the two runs, so these are indistinguishable.** Both arms allocate
~8–12 MB per op and are GC-dominated. Read this as the confirmation it is: a template that does not
opt in really does behave as stock, which is the §4 opt-in claim, measured rather than asserted.

Over the *trivial* template (`VirtualizedScrollBenchmark`, one `Canvas` per row) the branch is
genuinely faster — 16–34% and 12–23% fewer bytes across the 1k/100k × uniform/variable matrix — with
equal or lower container counts. That is real, but it is the regime where panel bookkeeping *is* the
whole cost. Quote it as "the panel rework does not regress, and helps when templates are cheap", not
as the change's performance story. The performance story is the table above it.

### The record's memory cost — §2 (d)'s figures are now measured, and they were right

Managed bytes a live panel retains after walking the whole collection, with the item collection
itself excluded from the delta:

| Items | Stock | Branch | The record |
|---|---:|---:|---:|
| 1,000 | 17,952 B | 71,520 B | 53,568 B (53.6 B/item) |
| 100,000 | 145,856 B | 4,520,728 B | **4,374,872 B (43.7 B/item)** |

§2's estimate was "~40 bytes each, single-digit MB at that size". Measured: 43.7 B/item and 4.2 MiB
at 100k. **Put this table in the PR body** — it is the answer to the first question a reviewer will
ask, and the answer holds up.

### The record does not cost per-pass time — §2's other claim also holds

`SizeRecordBenchmark`: the same 40-pass wheel-scroll burst at the head of the collection, run once on
a panel that has only ever seen that head and once on a panel that has already walked the entire
collection.

| Items | Record populated | Stock | Branch |
|---|---|---:|---:|
| 10,000 | no | 1.504 ms | 1.222 ms |
| 10,000 | yes | 1.470 ms | 1.293 ms |
| 100,000 | no | 1.490 ms | 1.144 ms |
| 100,000 | yes | 1.474 ms | 1.197 ms |

The populated/empty gap on the branch is 0.071 ms at 10k entries and 0.053 ms at 100k — inside the
run's own standard deviation (0.050–0.159 ms), and **smaller** at the larger record. A per-pass sweep
would have grown by 10×. It does not. Note the branch is also *faster than stock* here, and allocates
699 KB against stock's 910 KB.

### `RetainMatchingContainers` — a real but modest saving, and only backwards

Container prepares, branch vs. merge-base, from `--virtualization-report`:

| Scenario | Stock | Branch |
|---|---:|---:|
| Wheel **down**, page **down** (all shapes) | — | identical |
| Wheel **up**, uniform / variable | 132 / 71 | 120 / 64 (−9%, −10%) |
| Page **up**, uniform / variable | 83 / 43 | 75 / 38 (−10%, −12%) |
| Jumps (20), 1k uniform / 100k uniform | 299 / 320 | 299 / 320 — identical |
| Jumps (20), 100k variable | 182 | 201 (+10%) |

Over **complex** rows, where each avoided prepare is an avoided subtree rebuild rather than one
`Canvas`, the same ~7–10% shows up as work that actually exists:

| Scenario (complex, `Plain`) | Stock | Branch |
|---|---|---|
| Wheel down+up | 86 prepares / 172 builds / 1,674 visuals | 80 / 160 / **1,562** |
| Page down+up | 55 / 110 / 1,061 | 51 / 102 / **986** |
| Jumps (20) | 118 / 238 / 2,337 | 117 / 236 / 2,362 |

**An earlier version of this section recommended cutting the feature. That recommendation was drawn
from a one-`Canvas` template and is withdrawn.** What can be said from these numbers:

1. It saves ~10% of prepares — and with real templates, ~7% of all subtree construction — on
   **backwards** scrolling and paging. Forward scrolling is untouched; jumps are untouched.
2. The saving is below the noise floor of the timing runs, so it cannot be *separately* priced in ms
   here. It is visible only in counts.
3. A backwards scroll is **not** the wholesale re-prepare the design note implies: if stock recycled
   and re-prepared the whole realized window on every backwards step, wheel-up would cost 40 × 15 =
   600 prepares. It costs 132. The disjunct branch fires on a handful of steps, not all of them.
4. The +10% on 100k-variable jumps is most likely the size record changing the extent estimate, and so
   which items a jump lands on — the counts cannot separate that from retention.

For the scope split: this is a genuine but small optimization carrying real machinery
(`NullifyElement`, `_retainedForReuse`, `RecycleUnusedRetainedContainers`, `ScrollAnchorProvider`
bookkeeping) and the one path that can defeat the §2 staleness self-heal. That is a **cost/benefit
argument for a separate PR**, not the "delete it" the earlier draft claimed. Deciding it properly
wants a build with retention removed, measured over complex rows on a weak device — the case it was
written for, and the one no bench here reproduces.

## 6. Repo hygiene

Done: the nine AI-generated markdown files (`VARIABLE_HEIGHT_TEST.md`, `handoff_heuristics.md`,
`handoff_takepicture.md`, `heuristics_removal_plan.md`, `virtualizingstackpanel_perf.md`,
`virtualizingstackpanel_test_todo.md`, `plans/smoothscrolling.md`, `plans/virt_impro.md`,
`plans/virtualizingdatatemplate.md`, `plans/virtualizingdatatemplate_warmup.md`) are deleted; their
durable content is in `virtualization.md`. The two orphaned
`plans/virtualizingdatatemplate_memory_{enabled,disabled}.png` are deleted too — nothing referenced
them, and a memory before/after for the PR wants re-measuring against the current code anyway (§5).

**These two files are now fork-only** (`56c751649b`). They were 1,059 lines of the PR diff — working
notes, open questions and review framing sitting next to the code, which a reviewer has to scroll
past to reach the change. Both are still on disk and still in git history on branch
**`fork/20259-design-notes`**; they are listed in `.git/info/exclude`, so they no longer show as
untracked on the PR branch either.

Consequence to remember: **editing them does not touch the PR branch.** Commit doc changes onto
`fork/20259-design-notes` (a temporary `git worktree` is the least disruptive way — switching the
main tree would roll `src/` back to that branch's commit).

Still to do:

- If any of this should reach upstream, the candidate is `virtualization.md`'s design rationale as
  XML docs or a `docs/` note, rewritten for a reader who has not been following the branch. The
  fork-internal framing ("what we tried and rejected", open questions) does not travel.
- Unrelated to this PR but sitting in the working tree: `Directory.Build.props`,
  `nukebuild/BuildParameters.cs`, `external/Avalonia.Controls.DataGrid/`, `external/Numerge/`.
