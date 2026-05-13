---
name: component-forge
description: Iteratively create, improve, and audit Vue 3 components using spec-driven development with vanilla CSS/TypeScript
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, AskUserQuestion
user-invocable: true
---

# Component Forge

Spec-driven, self-validating skill for creating, improving, and auditing Vue 3 components in the fnl-platform project. Every component is built from scratch with vanilla CSS and TypeScript — no UI libraries.

## Folder Structure

There are **two layout strategies** for organizing themed components. The strategy is selected during Create mode and can be overridden via user context or parameters.

### Default: Theme-as-Directory (global theme switching)

Themes are **top-level directories** under `ui/`. Each theme contains its own complete set of components with the **same API** as the base. A single `ui/index.ts` controls which theme is active for the entire app.

```
resources/js/components/ui/
├── index.ts                  # Theme switcher — re-exports from active theme
├── basic/                    # Default/base theme
│   ├── button/
│   │   ├── index.ts
│   │   └── Button.vue
│   ├── card/
│   │   ├── index.ts
│   │   └── Card.vue
│   ├── dialog/
│   │   ├── index.ts
│   │   ├── Dialog.vue
│   │   ├── DialogContent.vue
│   │   └── ...
│   └── ...
├── steam-punk/               # Themed copy — same API, different visuals
│   ├── button/
│   │   ├── index.ts
│   │   └── Button.vue
│   ├── card/
│   │   ├── index.ts
│   │   └── Card.vue
│   └── ...
└── glassmorphism/            # Another theme
    ├── button/
    ├── card/
    └── ...
```

**Theme switcher** (`ui/index.ts`):
```typescript
// Switch the entire app's theme by changing this one line:
export * from './basic';

// To switch to steam-punk:
// export * from './steam-punk';
```

**Benefits:**
- Same component API across themes (`Button` is `Button` regardless of theme)
- One-line global theme switch
- Can also import specific themes explicitly: `import { Button } from '@/components/ui/steam-punk/button'`
- Each theme is self-contained and independently testable

### Alternative: Theme-as-Subfolder (per-component variants)

When you need a themed variant of a **single component** (not a full theme), nest the variant inside the component directory. Use this when the user explicitly requests it, or when the theme only applies to one component.

```
resources/js/components/ui/
├── button/
│   ├── index.ts              # Base button
│   ├── Button.vue
│   └── steam-punk/           # Variant of just this component
│       ├── index.ts
│       └── SteamPunkButton.vue
└── ...
```

### Choosing a Strategy

The skill determines which strategy to use based on:

1. **User explicitly specifies** — always honor the user's choice
2. **Multiple components in the same theme** — use Theme-as-Directory (default)
3. **Single component variant** — use Theme-as-Subfolder (alternative)
4. **Context from prior conversation** — if user has been building a full theme, continue with Theme-as-Directory

When ambiguous, ask:

```
AskUserQuestion:
  question: "How should the theme be organized?"
  header: "Layout"
  options:
    - label: "Full theme (Recommended)"
      description: "Top-level theme directory with all components — enables global theme switching"
    - label: "Single variant"
      description: "Subfolder under the component — for one-off themed variants"
```

### Naming Rules

| Element | Theme-as-Directory | Theme-as-Subfolder |
|---------|-------------------|-------------------|
| Theme dir | `ui/{theme}/` | `ui/{component}/{theme}/` |
| Vue file | `{Name}.vue` (same name as base) | `{Theme}{Name}.vue` |
| CSS prefix | Same as base (`.btn`) | `{prefix}--{theme}` (`.btn--steam-punk`) |
| Export name | Same as base (`Button`) | `{Theme}{Name}` (`SteamPunkButton`) |
| Component dir | `ui/{theme}/{component}/` | `ui/{component}/{theme}/` |

### CSS for Themes

**Theme-as-Directory:** Each theme gets its own CSS file or section. The themed component can use entirely different CSS classes or override base styles. Theme CSS is organized as:

```css
/* ===== [steam-punk] Button ===== */
.btn {
    /* Full redefinition for this theme — still uses CSS custom properties only */
}
```

Alternatively, themes can import and extend the base CSS by layering overrides.

**Theme-as-Subfolder:** Theme CSS is appended to `resources/css/components.css` as a subsection under the base component:

```css
/* ===== Button ===== */
.btn { /* ... base styles ... */ }

/* ----- Button: Steam Punk ----- */
.btn--steam-punk {
    /* Override or extend base with theme-specific tokens */
    border: 2px solid var(--border);
    background: linear-gradient(135deg, var(--secondary), var(--muted));
}

.btn--steam-punk-brass { /* variant */ }
.btn--steam-punk-copper { /* variant */ }
```

## Invocation

When the user runs `/component-forge`, immediately ask which mode to use:

```
AskUserQuestion:
  question: "What would you like to do?"
  header: "Mode"
  options:
    - label: "Create"
      description: "Build a new component from scratch using spec-driven development"
    - label: "Improve"
      description: "Enhance an existing component against the quality checklist"
    - label: "Audit"
      description: "Scan all UI components and generate a prioritized quality report"
```

If the user provided a component name or description alongside the invocation (e.g., `/component-forge Toast`), infer **Create** mode and skip the mode question — proceed directly to Step 1 of Create.

---

## Mode 1: Create

Build a new component from spec to implementation in a single iterative loop.

### Step 1 — Gather Intent

If not already provided, ask the user what component they need:

```
AskUserQuestion:
  question: "What component would you like to create?"
  header: "Component"
  (free text — no preset options)
```

Then determine if this involves theming:

```
AskUserQuestion:
  question: "Is this a base component or a themed variant?"
  header: "Type"
  options:
    - label: "Base component"
      description: "Standard UI primitive — stored in /ui/basic/{name}/ (or /ui/{name}/ if no themes exist yet)"
    - label: "Themed variant"
      description: "A themed take on a component — theme directory layout is determined next"
```

If **themed variant**:
1. Ask the theme/concept name (e.g., `steam-punk`, `glassmorphism`, `retro-terminal`)
2. Ask which base component it extends (or offer to create the base first if it doesn't exist)
3. Determine layout strategy per the "Choosing a Strategy" rules above — ask if ambiguous

**Shortcut:** If the user's initial request already specifies a theme (e.g., `/component-forge steam-punk button`), skip these questions and parse directly:
- Component: `button`
- Theme: `steam-punk`
- Strategy: Theme-as-Directory (default)
- Path: `resources/js/components/ui/steam-punk/button/`

### Step 2 — Reconnaissance

Before designing anything, scan the codebase:

1. **Detect current structure** — `Glob` for `resources/js/components/ui/index.ts` to check if a theme switcher exists. If it does, read it to determine the active theme and available themes. Also `Glob` for `resources/js/components/ui/*/index.ts` and `resources/js/components/ui/*/*/index.ts` to map the full component tree.
2. **Check for duplication** — Scan component names across all themes. If a similar component exists, inform the user and ask whether to proceed, improve the existing one, or abort.
3. **Read the base component** (themed variant only) — If creating a themed variant, read the base/basic theme's `index.ts` and `.vue` files to understand the API surface being replicated.
4. **Read design tokens** — Read `resources/css/variables.css` to know the available tokens (colors, spacing, radii, shadows, transitions, z-indices).
5. **Read CSS patterns** — Read the first 200 lines of `resources/css/components.css` to internalize the section structure and naming conventions.
6. **Scan usage context** — `Grep` for the component name across `resources/js/pages/` and `resources/js/components/` to understand where it might be used.

### Step 3 — Generate Spec

Produce a specification covering:

| Section | Contents |
|---------|----------|
| **Name** | PascalCase component name, kebab-case CSS prefix |
| **Theme** | Theme/concept name if applicable (e.g., `steam-punk`), or `basic` for base theme |
| **Layout strategy** | Theme-as-Directory (default) or Theme-as-Subfolder (alternative) |
| **Base component** | Which base component this replicates/extends (themed variants only) |
| **Output path** | Full directory path where files will be created |
| **Type** | Atomic (single element) or Compound (parent + children) |
| **Variants** | Named visual variants (e.g., `default`, `destructive`, `outline`) |
| **Sizes** | Named size options (e.g., `sm`, `md`, `lg`) |
| **States** | Interactive states: default, hover, focus-visible, disabled, loading (if applicable) |
| **Accessibility** | ARIA role, labels, keyboard interactions, focus management |
| **Slots** | Default slot, named slots, scoped slot data |
| **Props** | Full Props interface with types and defaults |
| **Emits** | Events the component emits |
| **CSS tokens used** | Which design tokens from variables.css are referenced |

Present the spec to the user and ask for approval before proceeding:

```
AskUserQuestion:
  question: "Does this spec look good, or would you like changes?"
  header: "Spec review"
  options:
    - label: "Looks good, proceed"
      description: "Create the component files based on this spec"
    - label: "I have changes"
      description: "I'll describe what to adjust"
```

### Step 4 — Create Files

Follow the templates below based on component type and whether it's a base or themed variant.

**Path resolution (depends on strategy):**

| Strategy | Base component | Themed component |
|----------|---------------|-----------------|
| Theme-as-Directory (default) | `ui/basic/{name}/` | `ui/{theme}/{name}/` |
| Theme-as-Subfolder | `ui/{name}/` | `ui/{name}/{theme}/` |
| No themes yet | `ui/{name}/` | — |

#### For Themed Variants — Theme-as-Directory (Default)

The themed component has the **same file names and export names** as the base. This is what enables global theme switching via `ui/index.ts`.

**File 1: `resources/js/components/ui/{theme}/{name}/index.ts`**
```typescript
export { default as {Name} } from './{Name}.vue';

// Same types as base — themed components share the same API
export type {Name}Variant = 'default' | '...';
export type {Name}Size = 'default' | '...';

// Theme can define its own variant set if the concept demands it
// export type {Name}Variant = 'brass' | 'copper' | 'iron';

/**
 * Get CSS classes for {theme} themed {name}.
 */
export function get{Name}Classes(variant: {Name}Variant = 'default', size: {Name}Size = 'default'): string {
    const variantClasses: Record<{Name}Variant, string> = {
        default: '{prefix}-default',
        // ...
    };

    const sizeClasses: Record<{Name}Size, string> = {
        default: '{prefix}-md',
        // ...
    };

    return `{prefix} {prefix}--{theme} ${variantClasses[variant]} ${sizeClasses[size]}`;
}
```

**File 2: `resources/js/components/ui/{theme}/{name}/{Name}.vue`**

Same structure as the base component template (see "For Base Atomic Components" below). The only difference is the CSS classes returned by `getClasses` include the theme modifier.

**File 3: Update theme switcher** — If `ui/index.ts` doesn't re-export from this theme yet, add the re-export line:
```typescript
// ui/index.ts — re-export all components from active theme
export * from './basic';
// Available themes: basic, steam-punk, glassmorphism
```

**File 4: Append themed CSS section to `resources/css/components.css`**
```css
/* ===== [{theme}] {Name} ===== */
.{prefix}--{theme} {
    /* Theme-specific overrides — CSS custom properties only */
}

.{prefix}--{theme} .{prefix}-default { /* themed variant */ }
```

**When creating the first theme:** If the project has no themes yet (components live directly in `ui/{name}/`), the skill should:
1. Create `ui/basic/` and move/copy existing components into it
2. Create `ui/index.ts` pointing to `basic`
3. Create the new theme directory
4. **Ask the user before restructuring** — this is a significant refactor

#### For Themed Variants — Theme-as-Subfolder (Alternative)

The themed component has **unique names** to coexist with the base.

**File 1: `resources/js/components/ui/{name}/{theme}/index.ts`**
```typescript
export { default as {Theme}{Name} } from './{Theme}{Name}.vue';

// Re-export base types when the theme uses the same prop API
export type { {Name}Variant, {Name}Size } from '..';

// Or define theme-specific variants
export type {Theme}{Name}Variant = 'variant-a' | 'variant-b' | 'variant-c';

/**
 * Get CSS classes for {theme} themed {name}.
 */
export function get{Theme}{Name}Classes(variant: {Theme}{Name}Variant = 'variant-a'): string {
    const variantClasses: Record<{Theme}{Name}Variant, string> = {
        'variant-a': '{prefix}--{theme}-variant-a',
        'variant-b': '{prefix}--{theme}-variant-b',
        'variant-c': '{prefix}--{theme}-variant-c',
    };
    return `{prefix} {prefix}--{theme} ${variantClasses[variant]}`;
}
```

**File 2: `resources/js/components/ui/{name}/{theme}/{Theme}{Name}.vue`**
```vue
<script setup lang="ts">
import type { HTMLAttributes } from 'vue';
import type { {Theme}{Name}Variant } from '.';
import { get{Theme}{Name}Classes } from '.';
import { computed, useAttrs } from 'vue';

interface Props {
    variant?: {Theme}{Name}Variant;
    class?: HTMLAttributes['class'];
    disabled?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
    variant: 'variant-a',
    disabled: false,
});

const attrs = useAttrs();

const componentClass = computed(() => {
    const baseClasses = get{Theme}{Name}Classes(props.variant);
    return props.class ? `${baseClasses} ${props.class}` : baseClasses;
});
</script>

<template>
    <{default-element}
        data-slot="{name}"
        data-theme="{theme}"
        :class="componentClass"
        :disabled="disabled"
        v-bind="attrs"
    >
        <slot />
    </{default-element}>
</template>
```

**File 3: Append themed CSS subsection to `resources/css/components.css`**

Place it directly below the base component's CSS section using a subsection header:

```css
/* ----- {Name}: {Theme Title} ----- */
.{prefix}--{theme} {
    /* Theme overrides — still use CSS custom properties only */
}

.{prefix}--{theme}-variant-a { /* ... */ }
.{prefix}--{theme}-variant-b { /* ... */ }
```

#### For Base Atomic Components

Create **3 artifacts**:

**File 1: `resources/js/components/ui/{name}/index.ts`**
```typescript
export { default as {Name} } from './{Name}.vue';

export type {Name}Variant = 'default' | '...';
export type {Name}Size = 'default' | '...';

export interface {Name}Variants {
    variant?: {Name}Variant;
    size?: {Name}Size;
}

/**
 * Get CSS classes for {name} variant and size.
 */
export function get{Name}Classes(variant: {Name}Variant = 'default', size: {Name}Size = 'default'): string {
    const variantClasses: Record<{Name}Variant, string> = {
        default: '{prefix}-default',
        // ...
    };

    const sizeClasses: Record<{Name}Size, string> = {
        default: '{prefix}-md',
        // ...
    };

    return `{prefix} ${variantClasses[variant]} ${sizeClasses[size]}`;
}
```

**File 2: `resources/js/components/ui/{name}/{Name}.vue`**
```vue
<script setup lang="ts">
import type { HTMLAttributes } from 'vue';
import type { {Name}Variant, {Name}Size } from '.';
import { get{Name}Classes } from '.';
import { computed, useAttrs } from 'vue';

interface Props {
    variant?: {Name}Variant;
    size?: {Name}Size;
    class?: HTMLAttributes['class'];
    as?: string;
    disabled?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
    as: '{default-element}',
    variant: 'default',
    size: 'default',
    disabled: false,
});

const attrs = useAttrs();

const {name}Class = computed(() => {
    const baseClasses = get{Name}Classes(props.variant, props.size);
    return props.class ? `${baseClasses} ${props.class}` : baseClasses;
});
</script>

<template>
    <component
        :is="as"
        data-slot="{name}"
        :class="{name}Class"
        :disabled="as === 'button' ? disabled : undefined"
        v-bind="attrs"
    >
        <slot />
    </component>
</template>
```

**File 3: Append CSS section to `resources/css/components.css`**
```css
/* ===== {Name} ===== */
.{prefix} {
    /* Base styles — use only CSS custom properties from variables.css */
    display: inline-flex;
    align-items: center;
    border-radius: var(--radius-md);
    font-size: var(--text-sm);
    transition-property: color, background-color, border-color, box-shadow;
    transition-timing-function: var(--ease-default);
    transition-duration: var(--transition-normal);
}

.{prefix}:focus-visible {
    outline: 2px solid var(--ring);
    outline-offset: 2px;
}

.{prefix}:disabled {
    pointer-events: none;
    opacity: 0.5;
}

/* {Name} variants */
.{prefix}-default {
    background-color: var(--primary);
    color: var(--primary-foreground);
}

.{prefix}-default:hover {
    background-color: hsl(from var(--primary) h s calc(l * 0.9));
}

/* {Name} sizes */
.{prefix}-sm { /* ... */ }
.{prefix}-md { /* ... */ }
.{prefix}-lg { /* ... */ }
```

#### For Base Compound Components

Create **N+1 files** (parent + children):

**File 1: `resources/js/components/ui/{name}/index.ts`**
```typescript
export { default as {Name} } from './{Name}.vue';
export { default as {Name}Trigger } from './{Name}Trigger.vue';
export { default as {Name}Content } from './{Name}Content.vue';
// ... additional sub-components
```

**Parent `{Name}.vue`** — manages state via `provide`:
```vue
<script setup lang="ts">
import { computed, provide, ref } from 'vue';

interface Props {
    defaultOpen?: boolean;
    open?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
    defaultOpen: false,
});

const emit = defineEmits<{
    'update:open': [value: boolean];
}>();

const internalOpen = ref(props.defaultOpen);

const isOpen = computed(() => {
    return props.open !== undefined ? props.open : internalOpen.value;
});

function setOpen(value: boolean) {
    if (props.open === undefined) {
        internalOpen.value = value;
    }
    emit('update:open', value);
}

provide('{name}-open', isOpen);
provide('{name}-set-open', setOpen);
</script>

<template>
    <slot :open="isOpen" />
</template>
```

**Children** — `inject` shared state, own `data-slot`:
```vue
<script setup lang="ts">
import type { HTMLAttributes } from 'vue';
import { computed, inject, type ComputedRef } from 'vue';

const props = defineProps<{ class?: HTMLAttributes['class'] }>();

const isOpen = inject<ComputedRef<boolean>>('{name}-open');

const contentClass = computed(() => {
    return props.class ? `{name}-content ${props.class}` : '{name}-content';
});
</script>

<template>
    <div data-slot="{name}-content" :class="contentClass">
        <slot />
    </div>
</template>
```

### Step 4.5 — Write the Component Doc

Every component gets a co-located Markdown doc — `{Name}.md` lives next to `{Name}.vue`. The doc is public-facing reference for humans and AI agents who need to use the component without reading the source. Use the [Component Documentation Template](#component-documentation-template) at the end of this skill.

For compound components (parent + N children), a single `{Name}.md` covers the whole family with a sub-section per sub-component.

### Step 5 — Self-Validate

After creating all files, run the **33-item Quality Checklist** (defined below) against the new component. For each failed item, fix it immediately. Report the final score.

### Step 6 — Summary

Output a summary:
- Files created (with paths)
- Quality score (passed / 31 applicable items)
- Any items marked N/A and why
- Suggested next steps (e.g., "add to a page", "create a composable for data fetching")

---

## Mode 2: Improve

Enhance an existing component through targeted, iterative improvement.

### Step 1 — Select Component

If not specified, ask the user:

```
AskUserQuestion:
  question: "Which component would you like to improve?"
  header: "Component"
  (free text)
```

### Step 2 — Deep Read

1. Read ALL files in `resources/js/components/ui/{name}/`
2. Read the component's CSS section in `resources/css/components.css`
3. `Grep` for the component import across the entire `resources/js/` directory to understand usage patterns and frequency

### Step 3 — Score Against Quality Checklist

Run the **31-item Quality Checklist** and produce a scorecard:

```
Component: {Name}
Score: {passed}/{applicable} ({percentage}%)

| Dimension        | Score | Items Failed                    |
|------------------|-------|---------------------------------|
| Structure        | 3/4   | Missing withDefaults            |
| Types            | 4/4   | —                               |
| CSS Tokens       | 4/5   | Hardcoded color in hover state  |
| ...              | ...   | ...                             |
```

### Step 4 — Identify Highest-Impact Gap

From the failed items, select the one that:
1. Is used in the most pages (highest blast radius)
2. Affects accessibility (a11y failures are always high priority)
3. Is easiest to fix (quick wins first when tied)

Propose the specific fix with a before/after code preview.

### Step 5 — Implement

After user approval, make the change. Then re-run the checklist and show the before/after score comparison.

### Step 6 — Iterate

Ask the user:

```
AskUserQuestion:
  question: "Continue improving this component?"
  header: "Next"
  options:
    - label: "Fix next gap"
      description: "Address the next highest-impact quality gap"
    - label: "Done"
      description: "Stop improving — current score is acceptable"
```

If "Fix next gap", loop back to Step 4. This is the **infinite agentic loop** — keep iterating until the user says stop or the score hits 100%.

---

## Mode 3: Audit

Scan all UI components and produce a prioritized quality report.

### Step 1 — Enumerate Components

1. **Detect layout strategy** — Check if `resources/js/components/ui/index.ts` exists (Theme-as-Directory) or not (flat/Theme-as-Subfolder).
2. **Theme-as-Directory:** `Glob` for `resources/js/components/ui/*/` to list themes, then `resources/js/components/ui/*/*/index.ts` for all components per theme.
3. **Flat/Theme-as-Subfolder:** `Glob` for `resources/js/components/ui/*/index.ts` for base components, then `resources/js/components/ui/*/*/index.ts` for nested variants.
4. Build a tree map:

```
# Theme-as-Directory layout:
basic/ (active theme)
├── button/
├── card/
└── dialog/
steam-punk/ (inactive theme)
├── button/
└── card/

# Or flat layout:
button/ (base)
├── steam-punk/ (variant)
└── glassmorphism/ (variant)
card/ (base)
dialog/ (base)
```

### Step 2 — Lightweight Scoring

For each component, perform a **fast scan** (do NOT deep-read every file — use `Grep` and quick `Read` of index.ts only):

1. **Has index.ts with type exports?** — Check for `export type` in index.ts
2. **Has getClasses helper?** — Check for `export function get` in index.ts
3. **Uses data-slot?** — `Grep` for `data-slot` in the component's `.vue` files
4. **Has CSS section?** — `Grep` for `/* ===== {Name} ===== */` in components.css
5. **Uses CSS tokens only?** — Quick check for hardcoded colors (`#`, `rgb(`, `hsl(` without `var(`) in the CSS section
6. **Has accessibility attributes?** — `Grep` for `aria-` or `role=` in `.vue` files
7. **Has focus-visible?** — Check CSS section for `:focus-visible`
8. **Has disabled state?** — Check CSS section for `:disabled` or `.{prefix}-disabled`

Score each component out of 8 (lightweight score).

### Step 3 — Usage Frequency

`Grep` for each component's import across `resources/js/pages/` and `resources/js/components/` (excluding the component's own directory). Count occurrences. This determines **priority weight**.

### Step 4 — Identify Missing Components

Compare the existing inventory against common UI component needs:

**Expected components** (check which exist):
- Toast / Notification
- Radio / RadioGroup
- Progress / ProgressBar
- Accordion
- Popover
- Toggle / ToggleGroup
- Slider
- Command (command palette)
- Calendar / DatePicker
- AlertDialog (confirmation dialogs)
- ScrollArea
- AspectRatio

### Step 5 — Present Report

Output a prioritized table:

```
## Component Audit Report

### Base Components ({count})

| Component | Themes | Score | Usage | Priority | Top Gap |
|-----------|--------|-------|-------|----------|---------|
| Button    | 0      | 7/8   | 47    | HIGH     | Missing loading state |
| Input     | 0      | 6/8   | 32    | HIGH     | No aria-invalid |
| Dialog    | 0      | 8/8   | 12    | —        | Fully passing |
| ...       | ...    | ...   | ...   | ...      | ... |

### Themed Variants ({count})

| Component | Theme         | Score | Usage | Notes |
|-----------|---------------|-------|-------|-------|
| Button    | steam-punk    | 6/8   | 3     | Missing focus-visible |
| Card      | neumorphism   | 7/8   | 5     | — |
| ...       | ...           | ...   | ...   | ... |

### Missing Components

| Component    | Expected Usage | Recommended Priority |
|--------------|----------------|---------------------|
| Toast        | Global         | HIGH                |
| RadioGroup   | Forms          | HIGH                |
| AlertDialog  | Confirmations  | MEDIUM              |
| ...          | ...            | ...                 |

### Recommended Action Order
1. Improve Button (loading state) — used in 47 locations
2. Create Toast — needed globally for API feedback
3. ...
```

---

## Quality Checklist (33 Items)

This is the master checklist used by all three modes. Score each item as PASS, FAIL, or N/A.

### Structure (4 items)
1. `index.ts` exists and re-exports the default component
2. `Props` interface is explicitly defined (not inline `defineProps<{...}>()`)
3. Root element has `data-slot="{component-name}"` attribute
4. Props use `withDefaults(defineProps<Props>(), { ... })` for optional props with defaults

### Types (4 items)
5. Variant type is exported from `index.ts` (if component has variants)
6. Size type is exported from `index.ts` (if component has sizes)
7. `getClasses` helper function is exported (if component has variants/sizes)
8. No `any` type usage anywhere in the component files

### CSS Tokens (5 items)
9. All colors reference CSS custom properties (`var(--...)`)
10. All spacing values use `var(--spacing-*)` tokens
11. All border-radius values use `var(--radius-*)` tokens
12. All transitions use `var(--transition-*)` and `var(--ease-*)` tokens
13. All z-index values use `var(--z-*)` tokens (if applicable)

### Dark Mode (1 item)
14. No hardcoded color values — relies entirely on token system for automatic dark mode

### Accessibility (5 items)
15. Appropriate ARIA `role` attribute (if semantic HTML element doesn't convey role)
16. ARIA labels (`aria-label`, `aria-labelledby`, or visible label association)
17. Keyboard navigation works (Enter, Space, Escape, Arrow keys as appropriate)
18. `:focus-visible` styles defined in CSS
19. Screen reader text (`.sr-only`) for icon-only interactive elements

### States (2 items)
20. Disabled state handled (`:disabled` or `[aria-disabled]` styling + `pointer-events: none`)
21. Loading state handled (if component performs async actions — spinner slot or prop)

### Responsive (2 items)
22. No fixed pixel widths on the component root (uses `max-width`, `width: 100%`, or flex/grid)
23. Uses flexible layout model (flex or grid) — not absolute positioning for layout

### Transitions (2 items)
24. Hover state has smooth transition (not instant color/background change)
25. Open/close or show/hide has animation (if applicable — modals, dropdowns, accordions)

### Composability (4 items)
26. Default `<slot />` exists for content projection
27. Named slots for structured content areas (header, footer, icon — if applicable)
28. `as` prop for element polymorphism (if the component could render as different elements)
29. `v-bind="attrs"` with `useAttrs()` for attribute pass-through

### Documentation (4 items)
30. JSDoc comment on exported `getClasses` helper (if exists)
31. Type names are self-descriptive (`ButtonVariant`, not `Variant` or `BV`)
32. Co-located `{Name}.md` exists next to `{Name}.vue`
33. The doc covers (at minimum): Description, Props, Emits, Slots, Features, and Usage example — see [Component Documentation Template](#component-documentation-template)

---

## Component Documentation Template

Every component needs a co-located `{Name}.md` next to `{Name}.vue`. This is the public reference for any consumer (human or AI) who needs to use the component without reading the source.

**File path:**
- Atomic: `resources/js/components/ui/{name}/{Name}.md`
- Compound: same path, single doc covers the whole family
- Themed (Theme-as-Directory): `ui/{theme}/{name}/{Name}.md` (each theme gets its own doc)
- Themed (Theme-as-Subfolder): `ui/{name}/{theme}/{Theme}{Name}.md`
- Feature-scoped (e.g. `components/booking/schedule/`): same convention — co-located `.md` next to `.vue`.

### Required sections

```markdown
# {Name}

> One-sentence elevator pitch — what it is and when to use it.

## Description

2–4 paragraphs covering: what problem this component solves, design intent, and the most important behavioral notes a consumer needs upfront. Mention key invariants (e.g. "always renders a `<button>`", "manages its own focus trap").

## Props

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `propName` | `string \| null` | `null` | What it does. Note required vs optional. |

If a prop has constrained values (variants, sizes), list them inline or link to the exported type.

## Emits

| Event | Payload | When |
|-------|---------|------|
| `select` | `BookingListItem` | User clicks the card |

Omit the section entirely if there are no emits.

## Slots

| Slot | Scope | When |
|------|-------|------|
| default | — | Card body content |

Omit the section entirely if there are no slots.

## Exposed (defineExpose)

Only if the component exposes methods/refs via `defineExpose`. Document each exposed name, signature, and intended caller.

Omit the section if not used.

## Features

Bullet list of notable behaviors a consumer should know about. Examples:

- Animation contract (state machine, cancellation semantics)
- Accessibility (ARIA roles, keyboard interactions, screen-reader behavior)
- Reduced-motion handling
- Dark-mode handling (usually "automatic via tokens")
- Edge cases handled (empty states, overflow, error states)

This is the most useful section for AI agents — be specific, not vague.

## Usage

```vue
<script setup lang="ts">
import {Name} from '@/components/.../{Name}.vue';
</script>

<template>
    <{Name} :prop="value" @event="handler" />
</template>
```

Show 1–3 realistic snippets covering the most common use cases.

## Related

- `Sibling.vue` — short note on relation (e.g. "alternative variant for compact layouts")
- `useThing.ts` — composable that backs this component

## Notes / Gotchas

Optional. Include only if there are non-obvious foot-guns or constraints worth flagging.
```

### Style guidelines

- **Be concrete, not aspirational.** Document what the component DOES, not what it might do someday.
- **Examples must compile.** Snippets must use real prop names and import paths.
- **No marketing copy.** Skip "blazing-fast", "delightful UX". Stick to behavior.
- **Cross-link, don't duplicate.** Link to design tokens / sibling components rather than re-explaining them.
- **Update on change.** When the component's API changes, the doc changes in the same commit.

## Key Reference Files

Always consult these files during execution:

| File | Purpose |
|------|---------|
| `resources/js/components/ui/button/index.ts` | Gold standard atomic `index.ts` |
| `resources/js/components/ui/button/Button.vue` | Gold standard atomic `.vue` |
| `resources/js/components/ui/dialog/index.ts` | Gold standard compound `index.ts` |
| `resources/js/components/ui/dialog/Dialog.vue` | Gold standard compound parent (provide/inject) |
| `resources/js/components/ui/dialog/DialogContent.vue` | Gold standard compound child (inject, Teleport, focus trap) |
| `resources/css/components.css` | CSS section pattern and naming conventions |
| `resources/css/variables.css` | Full design token inventory |

## Rules

1. **Never use external UI libraries** — no Tailwind, Vuetify, Radix, Headless UI, or any CSS framework
2. **Every CSS value must reference a design token** from `variables.css` — no hardcoded colors, spacing, or radii
3. **Dark mode is automatic** — if tokens are used correctly, dark mode works via the `.dark` class on `:root`
4. **Always use `data-slot`** — every component root and significant sub-element gets a `data-slot` attribute
5. **Always use `useAttrs()`** — components must pass through unknown attributes
6. **Follow the existing naming convention** — directory is kebab-case, file is PascalCase, CSS prefix is kebab-case. In Theme-as-Directory mode, themed files keep the same names as base. In Theme-as-Subfolder mode, themed files are `{Theme}{Component}.vue`
7. **Append CSS to `components.css`** — do not create separate CSS files per component; all component styles go in the shared file. Base components use `/* ===== {Name} ===== */` headers; Theme-as-Directory uses `/* ===== [{theme}] {Name} ===== */`; Theme-as-Subfolder uses `/* ----- {Name}: {Theme} ----- */` subsection headers placed below the base section
8. **Theme-as-Directory is the default** — when creating themed components, default to top-level theme directories with the `ui/index.ts` switcher pattern. Only use Theme-as-Subfolder when the user explicitly requests it, or when the theme applies to a single component
9. **Same API across themes** — in Theme-as-Directory mode, every themed component must export the same names and prop interfaces as the base. `Button` is `Button` regardless of which theme is active. This enables the one-line global switch
10. **Strategy is overrideable** — user context, parameters, or explicit instructions always override the default strategy. If the user says "nest it under button/", use Theme-as-Subfolder. If the user says "make a full theme", use Theme-as-Directory
11. **Ask before restructuring** — if creating the first theme requires moving existing components into a `basic/` directory, always ask the user before proceeding
12. **Score honestly** — never mark a checklist item as PASS if the code doesn't fully satisfy it
13. **Fix before finishing** — in Create mode, all FAIL items must be addressed before completing (unless user explicitly opts out)
14. **Show the score** — always display the quality scorecard, even if it's perfect
