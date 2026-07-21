# Test Fonts (licensed — deliberately separate from ../TestOtfLoading/UNLICENSED-FONTS.md)

- **Roboto-Regular.ttf** — Apache License 2.0 (Google/Christian Robertson).
  Redistributable; committed here as the primary GPOS kerning oracle font.

- **Arial / Times New Roman / Consolas** — NOT committed. The oracle tests load
  them from `C:\Windows\Fonts\` when present and skip when absent (non-Windows
  or stripped installs).