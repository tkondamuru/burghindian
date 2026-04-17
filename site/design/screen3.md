# Design System Documentation: The Civic Ledger

## 1. Overview & Creative North Star
The design system is built upon the Creative North Star of **"The Civic Ledger."** 

Rather than a generic community portal, this system treats information as a premium editorial asset. It balances the warmth of the Indian diaspora (Saffron/Teal) with the grit and structural integrity of Pittsburgh’s architectural heritage. By moving away from standard card-and-border layouts, we utilize **Intentional Asymmetry** and **Tonal Depth** to create an experience that feels like a high-end digital broadsheet. We prioritize high information density without sacrificing visual breathing room, ensuring the portal is an authoritative source for the community.

---

## 2. Color & Surface Philosophy
The palette avoids "flat" monotony by using a sophisticated spectrum of tones derived from the primary Saffron and secondary Teal.

### The "No-Line" Rule
To achieve a premium editorial feel, **1px solid borders are prohibited for sectioning.** Boundaries must be defined solely through:
*   **Background Color Shifts:** Use `surface-container-low` sections sitting on a `surface` background.
*   **Tonal Transitions:** A `surface-container-highest` sidebar against a `surface-bright` main content area.

### Surface Hierarchy & Nesting
Treat the UI as a series of physical layers—like stacked sheets of fine archival paper.
*   **Base:** `surface` (#F8F9FA)
*   **Structural Sections:** `surface-container-low` (#F3F4F5)
*   **Information Nodes (Cards):** `surface-container-lowest` (#FFFFFF)
*   **Overlays/Contextual Menus:** `surface-container-high` (#E7E8E9)

### The "Glass & Gradient" Rule
While the design is "flat," we inject "soul" through subtle color depth. 
*   **Primary CTAs:** Should utilize a subtle linear gradient from `primary` (#9F3D00) to `primary_container` (#C74E00) at a 135-degree angle. 
*   **Floating Navigation:** Use glassmorphism for top-level navigation bars (e.g., `surface` at 85% opacity with a 12px backdrop-blur) to ensure the layout feels integrated and expensive.

---

## 3. Typography: The Editorial Voice
This design system uses a dual-font strategy to establish a clear hierarchy between "Storytelling" and "Utility."

*   **Display & Headlines (Manrope):** Use Manrope for all `display-` and `headline-` tokens. Its geometric yet warm character provides an authoritative, modern-broadsheet feel.
    *   *Usage:* Use `display-md` for Hero headers; `headline-sm` for section titles.
*   **Body & UI (Inter):** Use Inter for all `body-`, `title-`, and `label-` tokens. Inter’s high x-height ensures maximum readability for high-density text portals.
    *   *Usage:* `body-md` is the standard for article summaries; `label-md` for metadata (dates, categories).

**Creative Direction:** Avoid "centered" text blocks. Use left-aligned, ragged-right typography to lean into the editorial aesthetic.

---

## 4. Elevation & Depth
In this system, depth is a function of light and tone, not structure.

*   **Tonal Layering:** Instead of shadows, stack containers. Place a `surface-container-lowest` (White) card on a `surface-container-low` background to create a "soft lift."
*   **Ambient Shadows:** If a card must float (e.g., a featured community post), use a shadow that mimics natural light. 
    *   *Shadow Token:* `0px 12px 32px rgba(141, 113, 101, 0.08)`. Notice the shadow is tinted with `outline` (#8D7165) rather than pure black to keep the UI warm.
*   **The "Ghost Border" Fallback:** If a divider is mandatory for accessibility, use the `outline_variant` token at **15% opacity**. Never use 100% opaque lines.

---

## 5. Component Guidelines

### Buttons: The Tactile Command
*   **Primary:** Gradient of `primary` to `primary_container`. Text: `on_primary`. Radius: `md` (0.375rem).
*   **Secondary:** Solid `secondary_container`. Text: `on_secondary_container`. No border.
*   **Tertiary:** Ghost style. No background. `primary` text. Focus state uses a `primary_fixed_dim` subtle background shift.

### Cards: Information Modules
*   **Style:** Zero border. Radius: `lg` (0.5rem).
*   **Structure:** Use vertical whitespace (1.5rem to 2rem) instead of divider lines to separate header, body, and footer sections within the card. 
*   **Interaction:** On hover, shift the background from `surface-container-lowest` to `surface-bright`.

### Inputs: The Clean Slate
*   **Field:** Solid `surface_container_low` background. 
*   **Indicator:** A 2px bottom-only border in `outline_variant`, turning `primary` on focus. This avoids the "boxed-in" look and feels more like a signature line on a document.

### Chips: The Metadata Filter
*   **Filter Chips:** Radius `full`. Background: `secondary_fixed_dim`. Text: `on_secondary_fixed`.
*   **Action Chips:** Radius `sm`. Background: `tertiary_fixed`. Text: `on_tertiary_fixed`.

---

## 6. Do’s and Don’ts

### Do:
*   **Embrace Whitespace:** Use the `xl` (0.75rem) and `lg` (0.5rem) roundedness scale to keep the interface feeling friendly yet professional.
*   **Type Contrast:** Pair a `display-sm` headline with a `body-sm` metadata label immediately above it for an "Editorial Header" look.
*   **Color as Signpost:** Use `secondary` (Teal) exclusively for "Community" or "Action" items, and `primary` (Saffron) for "News" or "Urgent" items.

### Don’t:
*   **No Card-Clutter:** Do not put a card inside another card. Use a background color shift instead.
*   **No Pure Black:** Never use `#000000` for text. Use `on_surface` (#191C1D) to maintain the premium, ink-on-paper quality.
*   **No Heavy Borders:** Avoid the "Bootstrap" look. If the design feels messy, add more padding, don't add more lines.
*   **No Motion:** Respect the user's request for fast-loading performance. Rely on layout and typography to guide the eye, not animations.

---

## 7. Spacing Scale
Maintain a strict 8px/4px grid system to ensure information density remains readable.
*   **Compact (Information Tables):** 4px - 8px
*   **Editorial (Articles/Cards):** 16px - 24px
*   **Sectional (Structural Gaps):** 48px - 64px