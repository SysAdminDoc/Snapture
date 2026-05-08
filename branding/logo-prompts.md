# Snapture — Logo Generation Prompts

Five prompts for ChatGPT image generation. All require a **true transparent PNG (RGBA, alpha = 0 outside the subject)** — paste the standardized transparency block at the end of any prompt.

Concept: aperture / shutter shape that doubles as the letter S. Catppuccin-Mocha-aligned palette: deep base `#1E1E2E`, mauve accent `#CBA6F7`, secondary blue `#89B4FA`, soft text `#CDD6F4`. Distinctive, modern, geometric — no skeuomorphic camera lenses, no generic "screen with rectangle" clichés.

---

## 1. Minimal — geometric monogram

> Create a minimal vector-style icon for an app called "Snapture". A single bold geometric mark: an aperture/shutter ring that subtly forms the letter "S" through its negative space. Stroke only — no fill. Stroke color `#CBA6F7` (Catppuccin Mauve). Background fully transparent. Centered, 30% padding, no shadow, no glow, no gradient. Square 1024×1024.

## 2. App icon — rounded square tile

> Create a rounded-square app icon (squircle, ~22% corner radius) for "Snapture". Background gradient `#1E1E2E → #313244` (Catppuccin Base → Surface0), top-left to bottom-right. Centered: a stylized aperture mark in `#CBA6F7` (mauve), 6 blades, with one blade subtly extending to suggest the letter "S". Soft inner glow on the aperture edges in `#89B4FA` (blue) at 20% opacity. No outer drop shadow. 1024×1024.

## 3. Wordmark — horizontal lockup

> Create a horizontal wordmark for "Snapture". Aperture/shutter mark on the left, wordmark "Snapture" on the right. Mark color `#CBA6F7`. Wordmark in a modern geometric sans-serif (Inter / Manrope / DM Sans style), color `#CDD6F4`, weight 600. Tight tracking. Aperture mark height = wordmark cap height. Equal optical balance left/right. Background fully transparent. 1600×400.

## 4. Emblem — coat-of-arms style

> Create an emblem-style logo for "Snapture": a hexagonal frame in `#CBA6F7` with a 1px outer stroke, containing a centered aperture/shutter mark in `#89B4FA`. Inside the aperture, a small dot of `#A6E3A1` (Catppuccin green) representing the "captured pixel". Below the hexagon, the word "SNAPTURE" in small caps, letter-spaced 0.15em, color `#CDD6F4`. Background fully transparent. Square 1024×1024.

## 5. Abstract — pixel grid with focal mark

> Create an abstract icon for "Snapture": a 5×5 grid of small rounded squares in `#313244` (Catppuccin Surface0), with the center square replaced by a single bright `#CBA6F7` (mauve) mark forming a small aperture. The mark has a soft `#89B4FA` glow at 25% opacity. Conveys "selecting a region of the screen". No text. Background fully transparent. Square 1024×1024.

---

## Standardized transparency requirements (paste into every prompt)

Background/output requirements: The final image must be a true transparent PNG in RGBA format with a real alpha channel. Everything outside the main icon/logo must be fully transparent, alpha = 0. Do not render a checkerboard pattern. Do not render a white, gray, black, colored, or textured background. Do not simulate transparency. Only the main icon/logo should contain visible pixels. If the generated image includes a checkerboard or any visible background, remove it with image processing and export a corrected transparent PNG artifact.

After generating the image, verify that the delivered PNG is actually transparent by checking that the file is RGBA and that pixels outside the subject have alpha = 0. If the generated image contains a checkerboard pattern or any visible background, remove it using image processing and export a corrected transparent PNG file.

Final output: 1024×1024 PNG, RGBA, true transparent background, alpha channel enabled, no checkerboard, no solid background, no watermark, no text unless explicitly requested, only the main icon/logo visible.

---

## Integration checklist (after pick)

- [ ] Drop `logo.png` (1024×1024 RGBA) at repo root
- [ ] Drop `banner.png` (wide, ~2000×600) at `branding/banner.png`
- [ ] Convert `logo.png` → `assets/icon.ico` (multi-resolution: 16/24/32/48/64/128/256)
- [ ] Wire `<ApplicationIcon>` back into `src/Snapture.App/Snapture.App.csproj`
- [ ] Wire embedded `Resource Include` for tray icon fallback path
- [ ] README banner reference goes live (replace `onerror` placeholder)
- [ ] Add `favicon.ico` for any future GitHub Pages microsite
