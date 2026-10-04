---
name: Lush Trail Escapes
colors:
  surface: '#faf8ff'
  surface-dim: '#d2d9f4'
  surface-bright: '#faf8ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f2f3ff'
  surface-container: '#eaedff'
  surface-container-high: '#e2e7ff'
  surface-container-highest: '#dae2fd'
  on-surface: '#131b2e'
  on-surface-variant: '#404941'
  inverse-surface: '#283044'
  inverse-on-surface: '#eef0ff'
  outline: '#717970'
  outline-variant: '#c0c9be'
  surface-tint: '#2e6a41'
  primary: '#003b1b'
  on-primary: '#ffffff'
  primary-container: '#14532d'
  on-primary-container: '#87c695'
  inverse-primary: '#96d5a3'
  secondary: '#006c49'
  on-secondary: '#ffffff'
  secondary-container: '#6cf8bb'
  on-secondary-container: '#00714d'
  tertiary: '#28352e'
  on-tertiary: '#ffffff'
  tertiary-container: '#3f4b44'
  on-tertiary-container: '#adbbb1'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#b1f2be'
  primary-fixed-dim: '#96d5a3'
  on-primary-fixed: '#00210d'
  on-primary-fixed-variant: '#12512c'
  secondary-fixed: '#6ffbbe'
  secondary-fixed-dim: '#4edea3'
  on-secondary-fixed: '#002113'
  on-secondary-fixed-variant: '#005236'
  tertiary-fixed: '#d8e6dc'
  tertiary-fixed-dim: '#bccac0'
  on-tertiary-fixed: '#121e18'
  on-tertiary-fixed-variant: '#3d4a43'
  background: '#faf8ff'
  on-background: '#131b2e'
  surface-variant: '#dae2fd'
typography:
  display:
    fontFamily: Plus Jakarta Sans
    fontSize: 34px
    fontWeight: '800'
    lineHeight: 42px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 26px
    fontWeight: '700'
    lineHeight: 34px
    letterSpacing: -0.015em
  headline-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 22px
    fontWeight: '700'
    lineHeight: 28px
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Plus Jakarta Sans
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
  title-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 16px
    fontWeight: '600'
    lineHeight: 22px
  body-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Plus Jakarta Sans
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  label-lg:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 18px
    letterSpacing: 0.01em
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.02em
  label-sm:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '600'
    lineHeight: 14px
    letterSpacing: 0.03em
  price-display:
    fontFamily: Plus Jakarta Sans
    fontSize: 20px
    fontWeight: '800'
    lineHeight: 24px
    letterSpacing: -0.01em
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1rem
  margin: 1.25rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 1rem
  space-lg: 1.5rem
  space-xl: 2rem
---

## Brand & Style

The design system establishes a warm, invigorating, and effortlessly guided mobile experience for regional expeditions, eco-tours, and curated day trips. It speaks to weekend adventurers, cultural wanderers, and eco-conscious travelers seeking clarity, reliability, and local expertise right in their pocket.

The aesthetic blends **Modern Organic Tactility** with **Clean Editorial Utility**:
- **Welcoming & Trustworthy:** Interfaces rely on open layouts, generous tap targets, friendly geometry, and breathable whitespace that remove booking anxiety.
- **Rooted in Nature:** Deep jungle tones ground the interface with an air of enduring stability, while vibrant emerald and crisp mint accents spark excitement, discovery, and lush outdoor terrain.
- **Mobile-First Precision:** High-density trip attributes (departure dates, remaining capacity, transit modes, and package tiers) are chunked into structured, high-legibility cards with immediate glanceability under bright sunlight or on the go.

## Colors

The palette draws directly from dense native foliage, sunlit canopies, and crisp mountain streams, grounded by slate neutrals for maximum screen readability:

- **Primary (`#14532D` - Deep Jungle Green):** Represents foundational grounding and stability. Used for primary CTAs, active bottom navigation highlights, prominent display headings, and core touchpoints.
- **Secondary (`#10B981` - Vibrant Emerald):** The accent of discovery and positive confirmation. Applied to live availability indicators, success confirmations, interactive toggles, badge highlights, and progress rails.
- **Tertiary (`#E6F4EA` - Fresh Mint):** Soft, soothing tint used for tonal container fills, pill tags, badge backgrounds, and non-distracting active surface selections.
- **Neutral (`#0F172A` - Warm Slate Dark):** High-contrast base for primary text, micro-copy, and sharp iconography, ensuring AAA accessibility across bright daylight mobile scenarios.
- **Background & Canvas (`#F8FAFC` - Light Slate Mist):** A cool, airy canvas surface that allows foreground tour cards and floating action bars to pop without stark optic glare.

## Typography

The type system pairs the geometric, affable curves of **Plus Jakarta Sans** for titles, headings, and immersive editorial narratives with the neutral, crisp metrics of **Inter** for dense metadata, badges, and interface controls.

- **Plus Jakarta Sans:** Injects human warmth and modernity into hero titles, destination intros, and pricing summaries. The heavier weights (`700` and `800`) provide authoritative yet inviting wayfinding.
- **Inter:** Chosen for seat counters, departure timestamps, transportation badge labels, and tab navigations where monoline consistency and tight horizontal compactness prevent unwanted line wraps on smaller mobile displays (e.g., iPhone SE, 360dp Android devices).
- **Price Emphases:** Currency symbols are paired at slightly lower weight or superscript positioning relative to the integer price token to anchor customer focus instantly on total cost.

## Layout & Spacing

The layout model is optimized strictly around handheld ergonomic zones:

- **Mobile Viewport Grid:** Based on an adaptive fluid single-column layout flanked by fixed screen margins (`1.25rem` / `20px` default, shrinking to `1rem` on narrow screens `<375px`). Internal composite grids (e.g. 2-up itinerary cards or filter toggles) use a `1rem` (`16px`) gutter.
- **Thumb Zone Discipline:** Primary conversion actions (e.g., "Reserve Spot", "Continue to Checkout") anchor within a sticky bottom sheet/bar located `space-md` above the hardware safe-area-inset.
- **Component Padding Scale:**
  - `space-xs` (4px): Micro-gaps between badge icons and companion label strings.
  - `space-sm` (8px): Inner padding for compact pill chips and list item row separation.
  - `space-md` (16px): Standard internal padding for cards, input containers, and sheet headers.
  - `space-lg` (24px): Vertical separation between related card sections and group segments.
  - `space-xl` (32px): Generous breathing room between macro blocks (e.g., Hero image end to content body).

## Elevation & Depth

Visual depth is achieved through **ambient diffused shadows** tinted with forest undertones rather than generic grayscale drops, creating a soft, floating effect:

- **Layer 0 (Base Canvas):** Flat surface filled with `#F8FAFC`. Zero elevation.
- **Layer 1 (Card & Content Blocks):** Pure white `#FFFFFF` surface resting on `box-shadow: 0px 4px 16px -2px rgba(15, 23, 42, 0.05), 0px 2px 6px -1px rgba(20, 83, 45, 0.04)`. Imparts clean detachment without harsh outline borders.
- **Layer 2 (Floating Micro-Controls & Bookmarks):** Action buttons (such as the floating heart/bookmark icon overlaid on media banners) leverage `box-shadow: 0px 6px 20px -4px rgba(15, 23, 42, 0.12)` with a slight white 80% opacity backing blur (`backdrop-filter: blur(8px)`).
- **Layer 3 (Modals, Filters, & Bottom Sheets):** Deep upward ambient projection: `box-shadow: 0px -8px 30px rgba(15, 23, 42, 0.10)` combined with a 40% opacity `#0F172A` backdrop scrim.

## Shapes

The interface embraces organic, inviting geometry. Outer tour cards, modal sheets, and image carousels implement large, friendly corners (`1rem` to `1.5rem` / `16px` to `24px`), reflecting the casual comfort of recreational travel:

- **Primary Cards & Containers:** Standardized at `rounded-lg` (`16px`) to `rounded-xl` (`24px`).
- **Interactive Buttons & Form Fields:** Built with `rounded-lg` (`12px` to `16px`) for confident, tactile touch points.
- **Badges, Tags, & Avatar Clips:** Utilize full continuous pill rounding (`9999px`) to visually differentiate status and category items from structural cards.

## Components

### Tour & Experience Cards
- **Architecture:** Contained within a pure white `#FFFFFF` surface with `rounded-xl` (`20px`) corners. 
- **Media Header:** Fixed 16:10 or 4:3 aspect ratio photograph with corner radius clipped to top edges. Contains an overlaid floating bookmark/favorite button in the top-right corner.
- **Badges & Tags:** Situated directly below or overlaid on the bottom-left of the media banner. Employs `#E6F4EA` pill backgrounds with `#14532D` typography for transport types (e.g., `4x4 Safari`, `Catamaran`, `Electric E-Bike`) alongside an SVG icon.
- **Meta Row:** Depicts departure date/duration via an understated calendar icon and `label-md` neutral text.
- **Seat Availability Chip:** Displays immediate status (e.g., "Only 3 seats left" with `#10B981` dot indicator, switching to amber `#F59E0B` when `< 2`).
- **Pricing Anchor:** Anchored at the bottom-right using `price-display` in `#14532D` with an inline `/ person` unit in `body-sm`.

### Buttons
- **Primary Action:** Solid `#14532D` fill, pure white text, `label-lg`, `rounded-lg` (`14px`), minimum touch height of `52px` to guarantee effortless one-handed thumb interaction. Subtle scale micro-interaction (`scale(0.98)`) on press.
- **Secondary / Ghost:** `#E6F4EA` fill with `#14532D` typography for secondary path actions (e.g., "View Itinerary Details").
- **Icon Actions (Bookmark / Share):** 44x44px circular or `rounded-lg` (`12px`) button with `#FFFFFF` translucent background (`rgba(255, 255, 255, 0.9)`), handling tap toggles with spring physics and an active filled state in `#10B981`.

### Chips & Filter Pills
- **Unselected:** Subtle 1px border in `#E2E8F0` with transparent or `#FFFFFF` fill, `#0F172A` text.
- **Selected:** Solid `#14532D` fill with `#FFFFFF` text, or tinted `#E6F4EA` fill with `#14532D` text and a subtle 1px `#10B981` border.
- **Dimensions:** Height fixed at `36px` with horizontal internal padding of `space-md` (`16px`).

### Input Fields & Selectors
- **Container:** Height `52px`, `rounded-lg` (`12px`), background `#FFFFFF`, border `1.5px` solid `#E2E8F0`. Focus state shifts border to `#10B981` with an ambient `0 0 0 3px rgba(16, 185, 129, 0.15)` ring.
- **Date & Guest Pickers:** Include leading icons in `#14532D` with formatted display dates (e.g., `Thu, Oct 24`) in `title-md`.

### List Rows (Itinerary Timelines)
- Connected vertical timeline rail colored in `#E6F4EA` with `#10B981` station nodes. Each node details time, location milestone, and transit badge with `space-sm` vertical spacing.

- Layer 3 (Modals, Filters, & Bottom Sheets): Deep upward ambient projection.

## .NET MAUI TECHNICAL MAP (Critical for OpenCode)

### 1. Font Aliases (MauiProgram.cs)
When writing XAML, always use these exact `FontFamily` names:
- Plus Jakarta Sans -> `PlusJakartaSans` (Registered as PlusJakartaSans-Regular/Bold.ttf)
- Inter -> `Inter` (Registered as Inter-Regular/Medium.ttf)

### 2. Unit Conversions
- Convert `1rem` to MAUI device-independent units as `16`.
- `space-xs (0.25rem)` -> `4`
- `space-sm (0.5rem)`  -> `8`
- `space-md (1rem)`   -> `16`
- `space-lg (1.5rem)`  -> `24`
- `space-xl (2rem)`   -> `32`
- `margin (1.25rem)`  -> `20` (Use as Padding="20,0" or Margin="20")

### 3. Corner Radius (Rounded) Mapping
- `sm` -> `Border.StrokeShape="RoundRectangle 4"`
- `DEFAULT` -> `Border.StrokeShape="RoundRectangle 8"`
- `md` -> `Border.StrokeShape="RoundRectangle 12"`
- `lg` -> `Border.StrokeShape="RoundRectangle 16"`
- `xl` -> `Border.StrokeShape="RoundRectangle 24"`
- `full` -> Use a high value like `100` inside the RoundRectangle.

### 4. Shadows in MAUI
Since CSS box-shadow doesn't exist, translate the elevations using the `<Shadow>` element inside a `<Border>`:
- **Layer 1:** `<Shadow Brush="#0F172A" Offset="0,4" Radius="16" Opacity="0.05" />`
- **Layer 2:** `<Shadow Brush="#0F172A" Offset="0,6" Radius="20" Opacity="0.12" />`

### 5. Color Resource Key Mapping
Ensure you use the exact names from the YAML header as StaticResource keys:
- Primary Color (`#003b1b`) -> `ResourceKey=Primary`
- Secondary Color (`#006c49`) -> `ResourceKey=Secondary`
- Tertiary Color (`#28352e`) -> `ResourceKey=Tertiary`
- Surface Background (`#faf8ff`) -> `ResourceKey=Surface`
