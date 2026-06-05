# Midnight Ledger UI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the WealthIQ Blazor Server UI into the "Midnight Ledger" design — an emerald-on-navy, dark-first dashboard with a left navigation drawer, a dark/light theme toggle, tasteful motion, and a visual Steuerreport (animated tax figure + composition donut) — touching only `src/WealthIQ.Web`.

**Architecture:** A central `MudTheme` (light + dark palettes) drives all MudBlazor components. `MainLayout` is rewritten from a top `MudAppBar` to a persistent left `MudDrawer` with grouped navigation. A scoped `ThemePreferenceService` persists the dark/light choice in `localStorage` via JS interop, applied after first interactive render. Reusable presentational components (`StatCard`, `SectionCard`, `PageHeader`) plus a layered custom stylesheet (`wealthiq.css`) provide the visual vocabulary; each page is then restyled to use it. The Steuerreport gains a hero row using MudBlazor's built-in `MudChart` donut and a progressive-enhancement JS count-up.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.5, custom CSS, minimal vanilla-JS interop. No new NuGet packages. No backend (Domain/Application/Infrastructure/DB) changes.

**Verification model (read before starting):** This is UI-only visual work. The `tests/WealthIQ.Tests` project does **not** reference `WealthIQ.Web` (per CLAUDE.md dependency direction), and we are **not** adding that reference (it is a structural change requiring user approval). Therefore tasks are verified by: (1) `dotnet build WealthIQ.slnx` succeeds, (2) `dotnet format WealthIQ.slnx --verify-no-changes` is clean, (3) the existing `dotnet test WealthIQ.slnx` suite stays green, and (4) a manual browser pass in both themes. Do not weaken or modify existing tests. If any step appears to need a backend change, **stop and ask the user**.

**General rules for every task:**
- Preserve all existing `@code`, bindings, `@inject`, event handlers, and routes. Only presentation changes unless a step says otherwise.
- German UI labels; English identifiers/comments (per CLAUDE.md).
- No emoji anywhere. Icons come from `MudBlazor.Icons.Material` (line/outlined variants).
- Run `dotnet format WealthIQ.slnx` before each commit.
- Commit messages: English, Conventional Commits, ending with the Co-Authored-By trailer used in this repo.

---

## File Structure

**Create:**
- `src/WealthIQ.Web/Theme/WealthIqTheme.cs` — the central `MudTheme` (light + dark palettes, typography, shape).
- `src/WealthIQ.Web/Services/ThemePreferenceService.cs` — scoped service; reads/writes the theme choice in `localStorage`.
- `src/WealthIQ.Web/wwwroot/wealthiq.css` — layered custom styles (motion, tabular numerals, gradients, scrollbars, nav, cards).
- `src/WealthIQ.Web/wwwroot/wealthiq.js` — count-up enhancement + theme localStorage helpers.
- `src/WealthIQ.Web/Components/Shared/StatCard.razor` — KPI card.
- `src/WealthIQ.Web/Components/Shared/SectionCard.razor` — titled content container (replaces bare `MudPaper`).
- `src/WealthIQ.Web/Components/Shared/PageHeader.razor` — page title + optional actions slot.

**Modify:**
- `src/WealthIQ.Web/Components/App.razor` — swap Roboto→Inter, link `wealthiq.css`, reference `wealthiq.js`.
- `src/WealthIQ.Web/Components/Layout/MainLayout.razor` — drawer shell, theme provider wiring, nav.
- `src/WealthIQ.Web/Components/_Imports.razor` — add `@using` for new namespaces.
- `src/WealthIQ.Web/Program.cs` — register `ThemePreferenceService`.
- `src/WealthIQ.Web/Components/Pages/Steuerreport.razor` — hero + donut + StatCards + restyled tables.
- `src/WealthIQ.Web/Components/Pages/Import.razor`
- `src/WealthIQ.Web/Components/Pages/DataAdmin.razor` (presented as "Marktdaten")
- `src/WealthIQ.Web/Components/Pages/InstrumentsAdmin.razor`
- `src/WealthIQ.Web/Components/Pages/Diagnostics.razor`
- `src/WealthIQ.Web/Components/Pages/Audit.razor`
- `src/WealthIQ.Web/Components/Pages/Error.razor`
- `src/WealthIQ.Web/Components/Pages/NotFound.razor`

---

## Task 1: Central theme object

**Files:**
- Create: `src/WealthIQ.Web/Theme/WealthIqTheme.cs`

- [ ] **Step 1: Create the theme class**

```csharp
using MudBlazor;

namespace WealthIQ.Web.Theme;

/// <summary>
/// Central "Midnight Ledger" MudBlazor theme: emerald accent on a deep navy-slate base
/// (dark, primary) with a crisp paper-white light mode. Drives every MudBlazor component.
/// </summary>
public static class WealthIqTheme
{
    public static readonly MudTheme Instance = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#059669",
            Secondary = "#A78BFA",
            Info = "#2563EB",
            Success = "#059669",
            Warning = "#D97706",
            Error = "#DC2626",
            Background = "#F7F9FC",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1E2733",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1E2733",
            TextPrimary = "#1E2733",
            TextSecondary = "#5A6678",
            ActionDefault = "#5A6678",
            LinesDefault = "#E6EAF0",
            TableLines = "#E6EAF0",
            DrawerIcon = "#5A6678",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#10B981",
            Secondary = "#A78BFA",
            Info = "#60A5FA",
            Success = "#34D399",
            Warning = "#FBBF24",
            Error = "#F87171",
            Black = "#0A0E17",
            Background = "#0F1420",
            Surface = "#161D2C",
            AppbarBackground = "#0A0E17",
            AppbarText = "#E7ECF3",
            DrawerBackground = "#0A0E17",
            DrawerText = "#B8C2D4",
            TextPrimary = "#E7ECF3",
            TextSecondary = "#7D8AA3",
            ActionDefault = "#7D8AA3",
            LinesDefault = "#232D40",
            TableLines = "#232D40",
            DrawerIcon = "#7D8AA3",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "Segoe UI", "sans-serif" },
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "248px",
        },
    };
}
```

> **MudBlazor version note:** This targets MudBlazor 9.5's typography API (`Typography.Default = new DefaultTypography { FontFamily = ... }`). If the build reports that `DefaultTypography` does not exist, the resolved MudBlazor version uses the older shape — replace with `Default = new Typography.Default { FontFamily = new[] { ... } }` per that version's API. Do not change the MudBlazor package version to work around this.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded (the type is not yet referenced; this just confirms the MudBlazor API usage compiles).

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Theme/WealthIqTheme.cs
git commit -m "feat(web): add Midnight Ledger MudTheme (light + dark palettes)"
```

---

## Task 2: Inter font, base stylesheet, JS enhancement file

**Files:**
- Create: `src/WealthIQ.Web/wwwroot/wealthiq.css`
- Create: `src/WealthIQ.Web/wwwroot/wealthiq.js`
- Modify: `src/WealthIQ.Web/Components/App.razor`

- [ ] **Step 1: Create `wealthiq.css`**

```css
/* Midnight Ledger — custom layer on top of MudBlazor.
   Scoped to .wiq-* helper classes so it does not fight the component library. */

:root {
    --wiq-ease: cubic-bezier(.22, .61, .36, 1);
}

/* Tabular numerals wherever we mark figures, so currency columns align. */
.wiq-num {
    font-variant-numeric: tabular-nums;
    letter-spacing: -0.01em;
}

.wiq-figure {
    font-variant-numeric: tabular-nums;
    font-weight: 800;
    letter-spacing: -0.02em;
    line-height: 1.05;
}

.wiq-pos { color: var(--mud-palette-success); }
.wiq-neg { color: var(--mud-palette-error); }

/* Card hover: subtle lift + accent ring. Applied to StatCard / SectionCard roots. */
.wiq-card {
    transition: transform .18s var(--wiq-ease), box-shadow .18s var(--wiq-ease), border-color .18s var(--wiq-ease);
    border: 1px solid var(--mud-palette-lines-default);
}
.wiq-card:hover {
    transform: translateY(-2px);
    border-color: var(--mud-palette-primary);
    box-shadow: 0 8px 24px rgba(0, 0, 0, .18);
}

/* Emerald-highlighted KPI (e.g. estimated tax). */
.wiq-card--accent {
    background: linear-gradient(135deg,
        color-mix(in srgb, var(--mud-palette-primary) 14%, var(--mud-palette-surface)),
        var(--mud-palette-surface));
    border-color: color-mix(in srgb, var(--mud-palette-primary) 45%, transparent);
}

/* Hero headline gradient text. */
.wiq-hero-figure {
    background: linear-gradient(135deg, var(--mud-palette-primary), var(--mud-palette-success));
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
}

/* Entrance animations. */
@keyframes wiq-rise {
    from { opacity: 0; transform: translateY(8px); }
    to   { opacity: 1; transform: translateY(0); }
}
.wiq-rise { animation: wiq-rise .45s var(--wiq-ease) both; }
.wiq-rise-2 { animation: wiq-rise .45s var(--wiq-ease) .08s both; }
.wiq-rise-3 { animation: wiq-rise .45s var(--wiq-ease) .16s both; }

/* Route transition: fade body content on navigation. */
.wiq-page { animation: wiq-rise .35s var(--wiq-ease) both; }

/* Nav section label inside the drawer. */
.wiq-nav-label {
    font-size: .68rem;
    text-transform: uppercase;
    letter-spacing: .09em;
    color: var(--mud-palette-text-secondary);
    padding: 14px 16px 6px;
}

/* Push the bottom nav group (Diagnose) to the foot of the drawer. */
.wiq-nav-spacer { flex: 1 1 auto; }

/* Slimmer, themed scrollbars. */
* {
    scrollbar-width: thin;
    scrollbar-color: var(--mud-palette-lines-default) transparent;
}
*::-webkit-scrollbar { width: 10px; height: 10px; }
*::-webkit-scrollbar-thumb {
    background: var(--mud-palette-lines-default);
    border-radius: 6px;
}

/* Respect reduced-motion. */
@media (prefers-reduced-motion: reduce) {
    .wiq-card, .wiq-rise, .wiq-rise-2, .wiq-rise-3, .wiq-page { animation: none !important; transition: none !important; }
}
```

- [ ] **Step 2: Create `wealthiq.js`**

```js
// Midnight Ledger — progressive enhancement. No framework; safe if it no-ops.
window.wealthiq = {
    // Theme persistence (called from ThemePreferenceService via JS interop).
    getTheme: function () {
        try { return localStorage.getItem('wiq-theme'); } catch { return null; }
    },
    setTheme: function (value) {
        try { localStorage.setItem('wiq-theme', value); } catch { /* ignore */ }
    },

    // Animate every element matching `.wiq-countup[data-target]` from 0 to its target.
    // The element's static text is already the correct final value, so this is purely cosmetic.
    runCountUps: function () {
        var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        var els = document.querySelectorAll('.wiq-countup[data-target]');
        els.forEach(function (el) {
            if (el.dataset.wiqDone === '1') return;
            el.dataset.wiqDone = '1';
            var target = parseFloat(el.dataset.target);
            if (isNaN(target)) return;
            var suffix = el.dataset.suffix || '';
            var fmt = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            if (reduce) { el.textContent = fmt.format(target) + suffix; return; }
            var start = performance.now();
            var dur = 900;
            function frame(now) {
                var t = Math.min(1, (now - start) / dur);
                var eased = 1 - Math.pow(1 - t, 3);
                el.textContent = fmt.format(target * eased) + suffix;
                if (t < 1) requestAnimationFrame(frame);
            }
            requestAnimationFrame(frame);
        });
    }
};
```

- [ ] **Step 3: Update `App.razor`** — replace the Roboto font link (line 13) with Inter, and add the stylesheet + script.

Replace this line:
```html
<link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
```
with:
```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet" />
```

After the existing `<link ... href="@Assets["app.css"]" />` line, add:
```html
<link rel="stylesheet" href="@Assets["wealthiq.css"]" />
```

Before the closing `</body>` (after the MudBlazor script line), add:
```html
<script src="wealthiq.js"></script>
```

- [ ] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/wwwroot/wealthiq.css src/WealthIQ.Web/wwwroot/wealthiq.js src/WealthIQ.Web/Components/App.razor
git commit -m "feat(web): add Inter font, Midnight Ledger stylesheet and JS enhancement"
```

---

## Task 3: Theme preference service

**Files:**
- Create: `src/WealthIQ.Web/Services/ThemePreferenceService.cs`
- Modify: `src/WealthIQ.Web/Program.cs`
- Modify: `src/WealthIQ.Web/Components/_Imports.razor`

- [ ] **Step 1: Create the service**

```csharp
using Microsoft.JSInterop;

namespace WealthIQ.Web.Services;

/// <summary>
/// Persists the user's dark/light choice in browser localStorage via JS interop.
/// JS interop is only available after the first interactive render, so callers must
/// invoke <see cref="LoadIsDarkAsync"/> from OnAfterRenderAsync(firstRender), not during init.
/// Default is dark (the primary Midnight Ledger mode).
/// </summary>
public sealed class ThemePreferenceService
{
    private const string Dark = "dark";
    private const string Light = "light";
    private readonly IJSRuntime _js;

    public ThemePreferenceService(IJSRuntime js) => _js = js;

    /// <summary>Reads the stored preference; returns true (dark) when absent or unrecognized.</summary>
    public async Task<bool> LoadIsDarkAsync()
    {
        var stored = await _js.InvokeAsync<string?>("wealthiq.getTheme");
        return !string.Equals(stored, Light, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(bool isDark)
        => await _js.InvokeVoidAsync("wealthiq.setTheme", isDark ? Dark : Light);
}
```

- [ ] **Step 2: Register the service in `Program.cs`** — after the `AddMudServices()` line (line 60):

```csharp
builder.Services.AddScoped<WealthIQ.Web.Services.ThemePreferenceService>();
```

- [ ] **Step 3: Add usings to `_Imports.razor`** — append:

```razor
@using WealthIQ.Web.Services
@using WealthIQ.Web.Theme
@using WealthIQ.Web.Components.Shared
```

- [ ] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Services/ThemePreferenceService.cs src/WealthIQ.Web/Program.cs src/WealthIQ.Web/Components/_Imports.razor
git commit -m "feat(web): add theme preference service (localStorage persistence)"
```

---

## Task 4: Shared presentational components

**Files:**
- Create: `src/WealthIQ.Web/Components/Shared/StatCard.razor`
- Create: `src/WealthIQ.Web/Components/Shared/SectionCard.razor`
- Create: `src/WealthIQ.Web/Components/Shared/PageHeader.razor`

- [ ] **Step 1: Create `StatCard.razor`**

```razor
@* KPI card: caption + figure, with optional emerald accent and count-up. *@
<MudPaper Elevation="0" Class="@RootClass" Style="height:100%;padding:18px 18px 16px;">
    <MudText Typo="Typo.caption" Style="text-transform:uppercase;letter-spacing:.06em;color:var(--mud-palette-text-secondary);">
        @Caption
    </MudText>
    <div class="wiq-figure @FigureClass" style="font-size:1.6rem;margin-top:6px;">
        @if (CountUp)
        {
            <span class="wiq-countup" data-target="@Value.ToString(System.Globalization.CultureInfo.InvariantCulture)" data-suffix=" €">@Display</span>
        }
        else
        {
            @Display
        }
    </div>
    @if (!string.IsNullOrWhiteSpace(Hint))
    {
        <MudText Typo="Typo.caption" Style="color:var(--mud-palette-text-secondary);">@Hint</MudText>
    }
</MudPaper>

@code {
    [Parameter, EditorRequired] public string Caption { get; set; } = "";
    [Parameter] public decimal Value { get; set; }
    [Parameter] public string? Hint { get; set; }
    [Parameter] public bool Accent { get; set; }
    [Parameter] public bool CountUp { get; set; }

    private string Display => Value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " €";
    private string RootClass => "wiq-card" + (Accent ? " wiq-card--accent" : "");
    private string FigureClass => Accent ? "wiq-hero-figure" : "";
}
```

- [ ] **Step 2: Create `SectionCard.razor`**

```razor
@* Titled container that replaces bare MudPaper for content sections. *@
<MudPaper Elevation="0" Class="wiq-card" Style="padding:20px;">
    @if (!string.IsNullOrWhiteSpace(Title) || HeaderActions is not null)
    {
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px;gap:12px;">
            <div>
                <MudText Typo="Typo.h6" Style="font-weight:600;">@Title</MudText>
                @if (!string.IsNullOrWhiteSpace(Subtitle))
                {
                    <MudText Typo="Typo.caption" Style="color:var(--mud-palette-text-secondary);">@Subtitle</MudText>
                }
            </div>
            @if (HeaderActions is not null)
            {
                <div>@HeaderActions</div>
            }
        </div>
    }
    @ChildContent
</MudPaper>

@code {
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public RenderFragment? HeaderActions { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

- [ ] **Step 3: Create `PageHeader.razor`**

```razor
@* Page title (renders an <h1> so FocusOnNavigate's "h1" selector keeps working) plus optional actions. *@
<div class="wiq-rise" style="display:flex;align-items:flex-end;justify-content:space-between;gap:16px;margin-bottom:24px;flex-wrap:wrap;">
    <div>
        <h1 style="margin:0;font-size:1.6rem;font-weight:700;color:var(--mud-palette-text-primary);">@Title</h1>
        @if (!string.IsNullOrWhiteSpace(Subtitle))
        {
            <MudText Typo="Typo.body2" Style="color:var(--mud-palette-text-secondary);margin-top:4px;">@Subtitle</MudText>
        }
    </div>
    @if (Actions is not null)
    {
        <div style="display:flex;gap:10px;align-items:center;">@Actions</div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
}
```

> Note: `PageHeader` renders an `<h1>`, which preserves `FocusOnNavigate Selector="h1"` in `Routes.razor`. When a page adopts `PageHeader`, remove its old `<MudText Typo="Typo.h4">` title to avoid a duplicate heading.

- [ ] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Shared/
git commit -m "feat(web): add StatCard, SectionCard, PageHeader shared components"
```

---

## Task 5: Navigation drawer shell (MainLayout)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Replace the entire file contents**

```razor
@inherits LayoutComponentBase
@inject ThemePreferenceService ThemePreference

<MudThemeProvider Theme="WealthIqTheme.Instance" @bind-IsDarkMode="_isDark" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudDrawer Open="true" Variant="DrawerVariant.Persistent" Anchor="Anchor.Left"
               Elevation="0" ClipMode="DrawerClipMode.Never">
        <div style="display:flex;flex-direction:column;height:100%;">
            <!-- Brand -->
            <div style="display:flex;align-items:center;gap:10px;padding:18px 16px 8px;">
                <div style="width:34px;height:34px;border-radius:9px;background:linear-gradient(135deg,#34D399,#10B981);"></div>
                <div>
                    <MudText Typo="Typo.subtitle1" Style="font-weight:700;line-height:1;">WealthIQ</MudText>
                    <MudText Typo="Typo.caption" Style="color:var(--mud-palette-text-secondary);">Steuer &amp; Vermögen</MudText>
                </div>
            </div>

            <MudNavMenu>
                <div class="wiq-nav-label">Bericht</div>
                <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Outlined.ReceiptLong">Steuerreport</MudNavLink>

                <div class="wiq-nav-label">Daten erfassen</div>
                <MudNavLink Href="/import" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.UploadFile">Import</MudNavLink>

                <div class="wiq-nav-label">Stammdaten</div>
                <MudNavLink Href="/data-admin" Match="NavLinkMatch.All" Icon="@Icons.Material.Outlined.ShowChart">Marktdaten</MudNavLink>
                <MudNavLink Href="/data-admin/instruments" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.Inventory2">Instrumente</MudNavLink>
            </MudNavMenu>

            <div class="wiq-nav-spacer"></div>

            <MudNavMenu>
                <div class="wiq-nav-label">Diagnose</div>
                <MudNavLink Href="/diagnostics" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.BugReport">Diagnose</MudNavLink>
                <MudNavLink Href="/audit" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.History">Audit-Trail</MudNavLink>
            </MudNavMenu>

            <!-- Theme toggle -->
            <div style="border-top:1px solid var(--mud-palette-lines-default);padding:10px 12px;display:flex;align-items:center;justify-content:space-between;">
                <MudText Typo="Typo.caption" Style="color:var(--mud-palette-text-secondary);">
                    @(_isDark ? "Dunkel" : "Hell")
                </MudText>
                <MudIconButton Icon="@(_isDark ? Icons.Material.Outlined.LightMode : Icons.Material.Outlined.DarkMode)"
                               Color="Color.Primary" Size="Size.Small"
                               OnClick="ToggleThemeAsync" aria-label="Theme umschalten" />
            </div>
        </div>
    </MudDrawer>

    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.Large" Class="my-6">
            <div class="wiq-page" @key="_body">
                @Body
            </div>
        </MudContainer>
    </MudMainContent>
</MudLayout>

@code {
    private bool _isDark = true; // default to the primary dark mode until localStorage loads
    private readonly object _body = new();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _isDark = await ThemePreference.LoadIsDarkAsync();
            StateHasChanged();
        }
    }

    private async Task ToggleThemeAsync()
    {
        _isDark = !_isDark;
        await ThemePreference.SaveAsync(_isDark);
    }
}
```

> Notes:
> - `@key="_body"` is a stable object so the `.wiq-page` fade plays per layout mount; the route-level fade is acceptable as-is. If you find the fade does not replay on navigation, leave it — replaying on every keystroke would be worse. Do not key it on `@Body`.
> - Icon names assume MudBlazor 9.5 (`Icons.Material.Outlined.ReceiptLong`, `.UploadFile`, `.ShowChart`, `.Inventory2`, `.BugReport`, `.History`, `.LightMode`, `.DarkMode`). If any constant is missing in the resolved version, substitute the nearest existing outlined icon (e.g. `Icons.Material.Filled.*`).

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Manual verification**

Run: `dotnet run --project src/WealthIQ.Web` and open the app.
Confirm: left drawer with brand, three top sections, Diagnose pinned at the bottom, theme toggle flips dark/light and the choice survives a full page reload (F5). Active page shows the emerald highlight.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Layout/MainLayout.razor
git commit -m "feat(web): replace top appbar with grouped left navigation drawer + theme toggle"
```

---

## Task 6: Steuerreport redesign (hero + donut + KPIs)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`

This is the centerpiece. Keep the entire `@code` block from the current file (the data loading, `Current`, `OnYearChanged`, `Eur`, `EntryTable`, `DrillToSource`) and add the additions below.

- [ ] **Step 1: Replace the markup (everything between the `<PageTitle>` line and the `@code` block)**

```razor
<PageTitle>WealthIQ — Steuerreport</PageTitle>

<PageHeader Title="Steuerreport" Subtitle="Deutsche Jahres-Steuerübersicht (Finanzamt-grade)">
    <Actions>
        @if (_reports.Count > 0)
        {
            <MudSelect T="int" Value="_selectedYear" ValueChanged="OnYearChanged" Label="Jahr"
                       Variant="Variant.Outlined" Dense="true" Style="min-width:130px;">
                @foreach (var report in _reports)
                {
                    <MudSelectItem T="int" Value="report.Year">@report.Year</MudSelectItem>
                }
            </MudSelect>
        }
    </Actions>
</PageHeader>

@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4">@_error</MudAlert>
}

@if (_loading)
{
    <div style="display:flex;justify-content:center;padding:64px;">
        <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
    </div>
}
else if (_reports.Count == 0)
{
    <SectionCard>
        <MudAlert Severity="Severity.Info" Variant="Variant.Text">
            Noch keine Daten. Importiere zuerst ein Broker-Statement auf der Import-Seite.
        </MudAlert>
    </SectionCard>
}
else if (Current is not null)
{
    @* Hero: headline estimated tax (count-up) + composition donut. *@
    <MudGrid Class="mb-4 wiq-rise">
        <MudItem xs="12" md="7">
            <MudPaper Elevation="0" Class="wiq-card wiq-card--accent" Style="height:100%;padding:24px;">
                <MudText Typo="Typo.overline" Style="color:var(--mud-palette-text-secondary);">Geschätzte Steuer @Current.Year</MudText>
                <div class="wiq-figure wiq-hero-figure" style="font-size:2.8rem;margin:6px 0;">
                    <span class="wiq-countup" data-target="@Current.Summary.EstimatedTax.ToString(System.Globalization.CultureInfo.InvariantCulture)" data-suffix=" €">@Eur(Current.Summary.EstimatedTax)</span>
                </div>
                <MudText Typo="Typo.body2" Style="color:var(--mud-palette-text-secondary);">
                    Steuerpflichtige Erträge abzüglich anrechenbarer Quellensteuer.
                </MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" md="5">
            <MudPaper Elevation="0" Class="wiq-card" Style="height:100%;padding:24px;display:flex;flex-direction:column;align-items:center;justify-content:center;">
                <MudText Typo="Typo.overline" Style="color:var(--mud-palette-text-secondary);align-self:flex-start;">Quellen der Steuer</MudText>
                @if (CompositionTotal > 0)
                {
                    <MudChart ChartType="ChartType.Donut" Width="200px" Height="200px"
                              InputData="@CompositionData" InputLabels="@CompositionLabels"
                              ChartOptions="@_chartOptions" />
                }
                else
                {
                    <MudText Typo="Typo.body2" Style="color:var(--mud-palette-text-secondary);padding:32px 0;">Keine steuerpflichtigen Erträge.</MudText>
                }
            </MudPaper>
        </MudItem>
    </MudGrid>

    @* KPI grid. *@
    <MudGrid Class="mb-4 wiq-rise-2">
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Verkäufe (steuerpflichtig)" Value="@Current.Summary.NetRealizedGainsTaxable" CountUp="true" /></MudItem>
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Dividenden (steuerpflichtig)" Value="@Current.Summary.DividendsTaxable" CountUp="true" /></MudItem>
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Zinsen (steuerpflichtig)" Value="@Current.Summary.InterestTaxable" CountUp="true" /></MudItem>
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Vorabpauschale (steuerpflichtig)" Value="@Current.Summary.VorabpauschaleTaxable" CountUp="true" /></MudItem>
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Anrechenbare Quellensteuer" Value="@Current.Summary.ForeignWithholdingTax" CountUp="true" /></MudItem>
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Geschätzte Steuer" Value="@Current.Summary.EstimatedTax" Accent="true" CountUp="true" /></MudItem>
    </MudGrid>

    @* Drill-down detail. *@
    <div class="wiq-rise-3">
        <MudExpansionPanels MultiExpansion="true" Elevation="0">
            <MudExpansionPanel Text="@($"Verkäufe (realisierter PnL) ({Current.Sells.Count})")">
                @EntryTable(Current.Sells)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Vorabpauschale ({Current.Vorabpauschale.Count})")">
                @EntryTable(Current.Vorabpauschale)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Dividenden ({Current.Dividends.Count})")">
                @EntryTable(Current.Dividends)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Zinsen ({Current.Interest.Count})")">
                @EntryTable(Current.Interest)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Quellensteuer ({Current.WithholdingTaxes.Count})")">
                @EntryTable(Current.WithholdingTaxes)
            </MudExpansionPanel>
        </MudExpansionPanels>
    </div>
}
```

- [ ] **Step 2: Add to the `@code` block** — inject `IJSRuntime` at the top of the file (add `@inject IJSRuntime JS` under the existing `@inject` lines) and add these members inside `@code`:

```csharp
private readonly ChartOptions _chartOptions = new()
{
    ChartPalette = new[] { "#34D399", "#60A5FA", "#A78BFA", "#FBBF24", "#F472B6" },
    DisableLegend = false,
};

// Composition of the tax base: only positive contributors (a donut can't render negatives).
// Withholding tax is a credit, not a source, so it is intentionally excluded here.
private double[] CompositionData => Current is null
    ? Array.Empty<double>()
    : new[]
    {
        (double)Math.Max(0, Current.Summary.NetRealizedGainsTaxable),
        (double)Math.Max(0, Current.Summary.DividendsTaxable),
        (double)Math.Max(0, Current.Summary.VorabpauschaleTaxable),
        (double)Math.Max(0, Current.Summary.InterestTaxable),
    };

private static readonly string[] CompositionLabels = { "Verkäufe", "Dividenden", "Vorabpauschale", "Zinsen" };

private double CompositionTotal => CompositionData.Sum();

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    // Re-run after every render so count-ups fire once data and year-switches have painted.
    await JS.InvokeVoidAsync("wealthiq.runCountUps");
}
```

> The `wiq-countup` JS marks elements done with `data-wiq-done`, so calling `runCountUps` on every render is idempotent — only freshly-rendered figures animate.

- [ ] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

> **MudChart API note (MudBlazor 9.5):** `ChartType.Donut`, `InputData` (`double[]`), `InputLabels` (`string[]`), and `ChartOptions.ChartPalette` are the expected members. If `DisableLegend` is not present in the resolved version, remove that initializer line. Do not add a charting package.

- [ ] **Step 4: Manual verification**

Run the app. Confirm: hero shows the estimated-tax figure counting up on load; donut renders the four positive categories in emerald/blue/violet/amber; switching the year re-animates the figures; the five drill-down panels still expand and the "Anzeigen" buttons still navigate to `/audit?isin=...`.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor
git commit -m "feat(web): redesign Steuerreport with hero figure, composition donut, KPI cards"
```

---

## Task 7: Import page restyle

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Import.razor`

- [ ] **Step 1: Read the current file** to learn its exact markup and `@code` members (account-number field, `InputFile`, import button, progress text, per-file result alerts + diagnostics table). Preserve every binding and handler.

- [ ] **Step 2: Apply this restyle recipe** (presentation only):
  - Replace the top `<MudText Typo="Typo.h4">Import</MudText>` with:
    ```razor
    <PageHeader Title="Import" Subtitle="IBKR FlexQuery-Statements (XML) einlesen" />
    ```
  - Wrap the upload form (account field + `InputFile` + import button) in `<SectionCard Title="Statement hochladen"> ... </SectionCard>` instead of the bare `MudPaper`.
  - Give the `InputFile` a dropzone feel by wrapping it in a bordered container:
    ```razor
    <div style="border:1.5px dashed var(--mud-palette-lines-default);border-radius:12px;padding:28px;text-align:center;">
        @* existing InputFile / MudButton stay here *@
    </div>
    ```
  - Keep the import `MudButton` but set `Variant="Variant.Filled" Color="Color.Primary"` and add `StartIcon="@Icons.Material.Outlined.UploadFile"`.
  - Wrap each per-file result block in a `<SectionCard>` and keep the existing `MudAlert` (its `Severity` binding unchanged) and the diagnostics `MudTable` (add `Elevation="0"` and `Hover="true"` if not already present).
  - Wrap the whole page body content so the entrance animation applies: add `Class="wiq-rise"` to the first content container, or wrap in `<div class="wiq-rise"> ... </div>`.

- [ ] **Step 3: Build, format, manual check**

Run: `dotnet build WealthIQ.slnx` then `dotnet format WealthIQ.slnx`
Run the app, go to `/import`, confirm upload still works against a sample from `data/test/statements/` and result/diagnostics rendering is intact in both themes.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Import.razor
git commit -m "feat(web): restyle Import page (dropzone card, section cards)"
```

---

## Task 8: Marktdaten (DataAdmin) restyle

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/DataAdmin.razor`

- [ ] **Step 1: Read the current file.** It has an `<MudText Typo="Typo.h4">` title, a `MudProgressLinear`, a dismissible `MudAlert`, a `MudExpansionPanels` (MultiExpansion) with five sections (Ledger, Historical Prices, FX Rates, Basiszins, Instruments link), and a result `MudPaper`+`MudTable` at the bottom. Preserve all `@code`, dialog calls, multiselect bindings, date pickers, and button handlers.

- [ ] **Step 2: Apply this restyle recipe** (presentation only — the page keeps the route `/data-admin`; only the visible title becomes "Marktdaten"):
  - Replace the title with:
    ```razor
    <PageHeader Title="Marktdaten" Subtitle="Kurse, Wechselkurse und Basiszins verwalten" />
    ```
  - Keep `MudProgressLinear` for the busy state but set `Color="Color.Primary"` and `Rounded="true"`.
  - Convert each of the five `MudExpansionPanel` sections so their inner content sits in the existing panel (leave `MudExpansionPanels` as the container) — but set `<MudExpansionPanels MultiExpansion="true" Elevation="0">` and add a leading icon to each panel via `Icon="@Icons.Material.Outlined.X"` (Ledger → `Icons.Material.Outlined.Receipt`, Historical Prices → `Icons.Material.Outlined.ShowChart`, FX Rates → `Icons.Material.Outlined.CurrencyExchange`, Basiszins → `Icons.Material.Outlined.Percent`, Instruments → `Icons.Material.Outlined.Inventory2`).
  - The "Instruments" section's link to `/data-admin/instruments`: render it as `<MudButton Variant="Variant.Outlined" Color="Color.Primary" Href="/data-admin/instruments" StartIcon="@Icons.Material.Outlined.OpenInNew">Instrumente verwalten</MudButton>`.
  - Wrap the bottom result block in `<SectionCard Title="Ergebnis">` keeping the existing `MudTable` (set `Elevation="0"`).
  - Add `class="wiq-rise"` to the top content wrapper.
  - Leave all confirm-dialog logic untouched; the `MudDialogProvider` in MainLayout already themes them.

- [ ] **Step 3: Build, format, manual check** — confirm each section still refreshes/clears/seeds correctly and dialogs appear themed. Do **not** trigger destructive clears against real data; use the busy/disabled states and dialogs to verify wiring, or run against a disposable `data/app` DB.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/DataAdmin.razor
git commit -m "feat(web): restyle Data-Admin as Marktdaten (section icons, result card)"
```

---

## Task 9: Instrumente restyle

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/InstrumentsAdmin.razor`

- [ ] **Step 1: Read the current file.** It has a header with a back button, `MudProgressLinear`, dismissible `MudAlert`, the instruments `MudTable` (Striped+Hover, columns ISIN/Name/Typ/Teilfreistellung/Vorabpauschale/#Listings/Aktionen with Bearbeiten/Löschen), a conditional inline edit panel (ISIN read-only when editing, Name, Type, Teilfreistellungsquote numeric, SubjectToVorabpauschale checkbox, nested listings sub-editor, Add listing, Save/Cancel), and an upload section (two `InputFile`, Merge/Replace `MudRadioGroup`, upload button, result alert). Preserve every binding and handler.

- [ ] **Step 2: Apply this restyle recipe** (presentation only):
  - Replace the header block with:
    ```razor
    <PageHeader Title="Instrumente" Subtitle="Instrumentenprofile und Listings pflegen">
        <Actions>
            <MudButton Variant="Variant.Text" Href="/data-admin" StartIcon="@Icons.Material.Outlined.ArrowBack">Zurück zu Marktdaten</MudButton>
        </Actions>
    </PageHeader>
    ```
  - Wrap the instruments table in `<SectionCard Title="Instrumente">` and set the `MudTable` to `Elevation="0" Hover="true" Striped="true"` with `FixedHeader="true"` and a `Height` (e.g. `Style="max-height:520px;"`) so the header sticks.
  - Style the Bearbeiten/Löschen actions as icon buttons: `<MudIconButton Icon="@Icons.Material.Outlined.Edit" .../>` and `<MudIconButton Icon="@Icons.Material.Outlined.Delete" Color="Color.Error" .../>` keeping the existing `OnClick` handlers.
  - Put the conditional inline edit panel inside `<SectionCard Title="Instrument bearbeiten">` (shown when the edit state is active). Keep the nested listings sub-editor; wrap each listing row in a bordered `div` (`style="border:1px solid var(--mud-palette-lines-default);border-radius:10px;padding:12px;margin-bottom:10px;"`).
  - Wrap the upload section in `<SectionCard Title="Import (JSON)">` keeping the two `InputFile`, the `MudRadioGroup`, the button (`Variant="Variant.Filled"`), and result alert.
  - Add `class="wiq-rise"` to the top content wrapper.

- [ ] **Step 3: Build, format, manual check** — confirm edit/add/delete-guard/upload all still function and the sticky-header table scrolls within the card in both themes.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/InstrumentsAdmin.razor
git commit -m "feat(web): restyle Instrumente (sticky table, card editor, icon actions)"
```

---

## Task 10: Diagnose and Audit-Trail restyle

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Diagnostics.razor`
- Modify: `src/WealthIQ.Web/Components/Pages/Audit.razor`

- [ ] **Step 1: Read both files.** `Diagnostics.razor`: h4 title, error alert, a `MudSelect` severity filter (All/Info/Warning/Error/Fatal), a `MudTable` (Severity colored chip, Code, Meldung, Sektion, Referenz, Feld). `Audit.razor`: h4 title, error alert, ISIN `MudTextField` filter, a provenance `MudTable` and an import-batches `MudTable`. Preserve all bindings/handlers and the `?isin=` query-string handling on Audit (the Steuerreport drill-down depends on it).

- [ ] **Step 2: Restyle Diagnose:**
  - Title → `<PageHeader Title="Diagnose" Subtitle="Import-Diagnosen filtern und prüfen" />`.
  - Wrap the severity filter + table in `<SectionCard Title="Diagnosen">` with the filter in the `HeaderActions` slot.
  - Set the `MudTable` to `Elevation="0" Hover="true" Dense="true"`. Keep the severity chip but ensure its color maps to the themed palette (Info→`Color.Info`, Warning→`Color.Warning`, Error/Fatal→`Color.Error`).
  - Add `class="wiq-rise"` to the content wrapper.

- [ ] **Step 3: Restyle Audit:**
  - Title → `<PageHeader Title="Audit-Trail" Subtitle="Quell-Provenance und Import-Batches" />`.
  - Put the ISIN filter in the `PageHeader` `Actions` slot (keep its binding and the URL `?isin=` initialization).
  - Wrap the provenance table in `<SectionCard Title="Quell-Einträge (Provenance)">` and the batches table in `<SectionCard Title="Import-Batches">`, both tables `Elevation="0" Hover="true" Dense="true"`.
  - Add `class="wiq-rise"` to the content wrapper.

- [ ] **Step 4: Build, format, manual check** — from Steuerreport click an "Anzeigen" button and confirm it lands on `/audit` with the ISIN filter pre-applied; confirm the Diagnose severity filter still filters.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Diagnostics.razor src/WealthIQ.Web/Components/Pages/Audit.razor
git commit -m "feat(web): restyle Diagnose and Audit-Trail pages"
```

---

## Task 11: Error and NotFound restyle

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Error.razor`
- Modify: `src/WealthIQ.Web/Components/Pages/NotFound.razor`

- [ ] **Step 1: Read both files.** `Error.razor` shows a filled error `MudAlert`, a request-id line, and a home button (keep the `RequestId`/`ShowRequestId` `@code`). `NotFound.razor` is plain minimal HTML.

- [ ] **Step 2: Restyle** — centered, on-brand, minimal:
  - `Error.razor`: wrap content in a centered `SectionCard`:
    ```razor
    <div style="display:flex;justify-content:center;padding:48px 0;">
        <SectionCard Title="Es ist ein Fehler aufgetreten">
            @* keep existing alert / request-id block *@
            <MudButton Variant="Variant.Filled" Color="Color.Primary" Href="/" StartIcon="@Icons.Material.Outlined.Home" Class="mt-4">Zur Startseite</MudButton>
        </SectionCard>
    </div>
    ```
  - `NotFound.razor`: replace the plain HTML with the same centered `SectionCard` pattern, title "Seite nicht gefunden", a short body text, and a `MudButton ... Href="/"`. Keep any existing `PageTitle`. Note `NotFound.razor` may render outside the themed layout depending on routing — keep it self-contained and avoid relying on layout-only styles.

- [ ] **Step 3: Build, format, manual check** — visit `/not-found` and force an error path if feasible; confirm both render centered and on-brand.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Error.razor src/WealthIQ.Web/Components/Pages/NotFound.razor
git commit -m "feat(web): restyle Error and NotFound pages"
```

---

## Task 12: Final verification and cleanup

**Files:**
- Possibly modify: `src/WealthIQ.Web/wwwroot/app.css` (only if the error-boundary yellow clashes badly with the dark theme)
- Modify: `CLAUDE.md` (document the new UI layer)

- [ ] **Step 1: Full build (Release, as CI runs it)**

Run: `dotnet build WealthIQ.slnx --configuration Release`
Expected: Build succeeded, 0 errors. Treat new warnings as defects to fix.

- [ ] **Step 2: Format check**

Run: `dotnet format WealthIQ.slnx --verify-no-changes`
Expected: no changes. If it reports changes, run `dotnet format WealthIQ.slnx` and re-check.

- [ ] **Step 3: Full test suite (must stay green)**

Run: `dotnet test WealthIQ.slnx --configuration Release`
Expected: all tests pass. These are backend tests; they must be unaffected. If any fails, you changed something you should not have — investigate before proceeding.

- [ ] **Step 4: Manual full pass** — for EVERY page (`/`, `/import`, `/data-admin`, `/data-admin/instruments`, `/diagnostics`, `/audit`, `/not-found`):
  - Toggle dark and light; verify legibility and that no element is unstyled/invisible.
  - Verify the active nav item highlights correctly.
  - Narrow the window; verify the drawer/content remain usable.
  - Set OS "reduce motion" and confirm animations are suppressed.
  - Reload; confirm the theme choice persists.

- [ ] **Step 5: Error-boundary check (optional fix)** — if the Blazor error-boundary bar (defined in `app.css`, yellow `#b32121`) looks broken in dark mode, lightly adjust only that rule. Keep the change minimal.

- [ ] **Step 6: Update `CLAUDE.md`** — under the repository-layout/Web description, add a sentence noting the UI layer: the Midnight Ledger theme lives in `Theme/WealthIqTheme.cs`, shared presentational components in `Components/Shared/`, theme persistence in `Services/ThemePreferenceService.cs` (+ `wwwroot/wealthiq.css` / `wwwroot/wealthiq.js`), and that the nav drawer groups pages as Bericht / Daten erfassen / Stammdaten / Diagnose.

- [ ] **Step 7: Final commit**

```bash
git add CLAUDE.md src/WealthIQ.Web/wwwroot/app.css
git commit -m "docs(web): document Midnight Ledger UI layer; finalize redesign"
```

---

## Self-review notes (for the implementer)

- **Spec coverage:** palette (Task 1), Inter + tabular-nums + motion (Task 2), theme persistence (Task 3), shared components (Task 4), left drawer nav with workflow grouping + Diagnose pinned bottom + theme toggle (Task 5), Steuerreport hero + donut + KPI cards + drill-down (Task 6), all secondary pages (Tasks 7–11), verification + docs (Task 12). The IA changes (Data-Admin shown as "Marktdaten", Instrumente promoted to top-level) are realized in Task 5's nav and Task 8's title; routes are unchanged.
- **No backend changes:** every modified/created file is under `src/WealthIQ.Web` except the `CLAUDE.md` doc update. If any task seems to require touching Domain/Application/Infrastructure or the DB, STOP and ask the user.
- **Type/name consistency:** `ThemePreferenceService.LoadIsDarkAsync()`/`SaveAsync(bool)`, JS `wealthiq.getTheme`/`setTheme`/`runCountUps`, CSS classes `wiq-card`/`wiq-card--accent`/`wiq-figure`/`wiq-hero-figure`/`wiq-countup`/`wiq-rise[-2/-3]`/`wiq-nav-label`/`wiq-nav-spacer`/`wiq-num`, and components `StatCard`/`SectionCard`/`PageHeader` are used consistently across tasks.
- **Known version-sensitive spots (hedged inline):** MudBlazor typography API (Task 1), icon constant names (Task 5), `MudChart`/`ChartOptions` members (Task 6). Each task says how to adapt without changing the package version.
```
