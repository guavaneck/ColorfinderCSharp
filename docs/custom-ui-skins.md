# Custom UI skins

ColorFinder can load a user stylesheet without rebuilding the application. Put the stylesheet at:

- Linux: `~/.config/colorfinder/ui/theme.css`
- macOS: `~/Library/Application Support/colorfinder/ui/theme.css`

Restart ColorFinder after adding or editing the file. The stylesheet is limited to 256 KB and loads after the built-in styles.

## Start from the example

The repository includes a minimal terminal skin in [`packaging/themes/terminal/theme.css`](../packaging/themes/terminal/theme.css). On Linux, install it with:

```bash
mkdir -p ~/.config/colorfinder/ui
cp packaging/themes/terminal/theme.css ~/.config/colorfinder/ui/theme.css
```

Copy the example before modifying it so repository updates do not overwrite your personal skin.

## Theme variables

Define these variables on `:root` to establish the main palette:

```css
:root {
  color-scheme: dark;
  --bg: #111318;
  --surface: #1a1d24;
  --surface2: #232730;
  --text: #eef1f6;
  --muted: #9299a8;
  --accent: #74a2ff;
  --border: #303541;
  --sidebar-width: 210px;
}
```

The first seven colors are also available as live color pickers under **Settings → Interface colors**. Picker values are stored in `colors.json` beside `theme.css` and intentionally override the corresponding skin variables. Use **Use Skin Defaults** to remove those overrides.

## Stable selectors

Use `data-ui` selectors for structural customization. Static elements with an ID automatically receive that ID as their `data-ui` value. Important semantic hooks include:

| Selector | Element |
| --- | --- |
| `[data-ui="app"]` | Entire application |
| `[data-ui="toolbar"]` | Top toolbar |
| `[data-ui="layout"]` | Main three-column layout |
| `[data-ui="sidebar"]` | Favorites and color sidebar |
| `[data-ui="browser"]` | File browser area |
| `[data-ui="info-panel"]` | Selection details panel |
| `[data-ui="entry"]` | File or folder entry |
| `[data-ui="entry"][data-kind="directory"]` | Folder entry |
| `[data-ui="entry"][data-kind="file"]` | File entry |
| `[data-ui="location"]` | Sidebar location |
| `[data-ui="breadcrumb"]` | Path component |
| `[data-ui="color-filter"]` | Tag filter |
| `[data-ui="tag-swatch"]` | Context-menu tag swatch |
| `[data-ui="tag-color-setting"]` | File-tag setting row |
| `[data-ui="ui-color-setting"]` | Interface-color setting row |
| `[data-ui="automatic-color-rule"]` | Automatic-rule row |
| `[data-ui="folder-picker-entry"]` | Destination folder row |

Existing classes and IDs remain available for precise styling. Prefer `data-ui` for structure and classes for component states such as `.selected`, `.active`, and `.drop-target`.

## Example customization

```css
:root {
  --bg: #0b1020;
  --surface: #121a30;
  --surface2: #1b2744;
  --text: #e8efff;
  --muted: #92a2c8;
  --accent: #65a3ff;
  --border: #30466f;
}

[data-ui="entry"] {
  border-radius: 4px;
}

[data-ui="entry"][data-kind="directory"] .name {
  font-weight: 700;
}
```

Keep readable contrast between `--text` and `--bg`, and retain visible focus and selection states when replacing borders or outlines.
