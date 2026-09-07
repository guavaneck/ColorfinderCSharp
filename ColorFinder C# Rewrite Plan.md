# ColorFinder C# Rewrite Plan

## Project Goal

Rebuild ColorFinder in C# as a lightweight, cross-platform file manager inspired by macOS Finder, with **deep color-coding support as a core feature rather than a cosmetic add-on**.

The application will target:

- Linux
- macOS

The application will use a **framework-dependent .NET deployment**, meaning users will need the appropriate .NET runtime installed.

The UI will use the operating system's WebView implementation rather than bundling Chromium or another browser engine.

## Implementation Status

Last reviewed: 2026-09-07

- [x] The repository is separated into Core, Desktop, UI, and Core test projects.
- [x] The desktop host uses the operating system WebView through Photino; Chromium is not bundled.
- [x] Filesystem data and ColorFinder color metadata use separate models and services.
- [x] Color metadata is stored by the application in `~/.config/colorfinder/colors.json` on Linux.
- [x] The JSON bridge exposes intentional filesystem, navigation, and color actions.
- [x] C# owns filesystem access and navigation history; the UI owns transient selection and view mode.
- [x] Linux development uses `shell.nix` with .NET 8, GTK, and WebKitGTK.
- [x] The initial Core behavior has automated tests.
- [ ] Complete Version 0.1; breadcrumbs, opening files, and multi-selection remain.
- [ ] Verify and package the application on macOS.

Checked items below represent implemented and verified behavior, not merely planned behavior.

ColorFinder should eventually support:

- Finder-style file browsing
- Grid and list views
- Sidebar navigation
- File/folder previews
- File operations
- Assigning colors to files and folders
- Searching/filtering by color
- Automatic color assignment based on file extension
- Keyboard shortcuts for color assignment
- Context-menu color assignment
- Custom user-defined color rules
- Persistent color metadata across launches

---

# 1. Technology Stack

## Backend

**C# / .NET**

Use .NET for:

- File system operations
- Directory navigation
- File metadata
- Search
- File operations
- Color metadata
- Color rules
- Configuration
- Application state
- Asynchronous work
- Communication with the WebView

Prefer built-in .NET libraries whenever possible.

Useful namespaces will likely include:

```text
System.IO
System.Text.Json
System.Threading
System.Threading.Tasks
System.Collections.Generic
System.Diagnostics
System.Runtime.InteropServices
```

---

## UI

Use:

```text
HTML
CSS
JavaScript
```

Initially avoid frontend frameworks.

Vanilla JavaScript should be enough for early versions.

A framework such as Svelte can be considered later if the UI becomes difficult to maintain.

---

## WebView

Use a minimal native WebView wrapper.

Target:

```text
Linux
    WebKitGTK

macOS
    WKWebView
```

Do not bundle Chromium.

Architecture:

```text
             ColorFinder
                  |
        +---------+---------+
        |                   |
     C# Backend          Web UI
        |                   |
     .NET Runtime     HTML/CSS/JS
        |                   |
        +---------+---------+
                  |
              WebView
                  |
         +--------+--------+
         |                 |
      Linux              macOS
         |                 |
    WebKitGTK          WKWebView
```

---

# 2. Deployment Model

Use **framework-dependent deployment**.

Users will need:

```text
ColorFinder
.NET Runtime
System WebView libraries
```

Linux:

```text
.NET Runtime
GTK
WebKitGTK
```

macOS:

```text
.NET Runtime
WKWebView
```

The .NET SDK is only required to build ColorFinder.

---

# 3. Core Product Identity

ColorFinder should feel similar to Finder in terms of basic navigation and usability, but its defining feature should be:

> Files and folders can be visually categorized using persistent colors.

Color should not only affect appearance.

A file's assigned color should become searchable metadata.

For example:

```text
Documents/
├── taxes.pdf          [RED]
├── project.md         [BLUE]
├── vacation.jpg       [GREEN]
├── resume.pdf         [YELLOW]
└── school/            [PURPLE]
```

The user should eventually be able to search:

```text
Color: Red
```

and immediately see every red-tagged file.

They should also be able to combine color with normal search criteria later:

```text
Color: Blue
Extension: .cs
Name contains: Config
```

This concept should influence the architecture from the beginning.

---

# 4. Repository Structure

```text
ColorFinder/
│
├── ColorFinder.sln
│
├── src/
│   │
│   ├── ColorFinder.Core/
│   │   ├── FileSystem/
│   │   ├── Navigation/
│   │   ├── Search/
│   │   ├── Colors/
│   │   ├── Rules/
│   │   ├── Models/
│   │   ├── Settings/
│   │   └── Services/
│   │
│   ├── ColorFinder.Desktop/
│   │   ├── Program.cs
│   │   ├── WebView/
│   │   ├── Interop/
│   │   └── Platform/
│   │
│   └── ColorFinder.UI/
│       ├── index.html
│       ├── css/
│       ├── js/
│       ├── icons/
│       └── assets/
│
├── tests/
│   └── ColorFinder.Core.Tests/
│
├── scripts/
│
├── README.md
└── LICENSE
```

Important separation:

```text
Core
    application logic

Desktop
    OS + WebView integration

UI
    presentation and interaction
```

---

# 5. Core Data Model

Start with a file representation such as:

```csharp
FileEntry
{
    Name
    FullPath
    Extension
    Type
    Size
    ModifiedDate
    IsDirectory
}
```

Do not immediately bake visual color properties directly into `FileEntry`.

Instead, keep filesystem information and ColorFinder metadata separate.

For example:

```csharp
FileEntry
```

and:

```csharp
ColorMetadata
{
    Path
    ColorId
    AssignmentType
}
```

Possible assignment types:

```text
Manual
Automatic
Inherited
None
```

This becomes important later when automatic extension rules exist.

---

# 6. Color Model

Create the basic color model very early, even before the full UI exists.

Example:

```csharp
ColorTag
{
    Id
    Name
    HexColor
}
```

Built-in initial colors might include:

```text
Red
Orange
Yellow
Green
Blue
Purple
Gray
```

Later, users should be allowed to define custom colors.

Do not assume that colors always have fixed meanings.

A user might decide:

```text
Red       Important
Blue      Programming
Green     Completed
Purple    School
Orange    Needs Review
```

ColorFinder should store the color itself, while optionally allowing labels/names to be customized.

---

# 7. Color Metadata Storage

This is one of the major architectural decisions.

Color assignment must survive application restarts.

Early prototypes can use a JSON database:

```text
~/.config/colorfinder/colors.json
```

Conceptually:

```json
{
    "/home/user/Documents/report.pdf": "red",
    "/home/user/Projects/ColorFinder": "blue"
}
```

This is acceptable for testing but probably should not be the permanent architecture.

Eventually investigate:

- Extended filesystem attributes
- Platform-specific metadata
- A lightweight ColorFinder metadata database
- Hybrid storage

Important considerations:

```text
What happens if a file is renamed?

What happens if it is moved?

What happens if it is copied?

What happens if ColorFinder is not responsible for the move?

What happens on removable drives?
```

These are later-stage problems, but the metadata layer should be designed so storage can be changed without rewriting the UI.

---

# 8. Early Color Prototype

Color coding should appear much earlier than the final implementation.

The first color prototype should support:

```text
Assign one predefined color to a file

Remove the color

Reload ColorFinder

Verify that the color remains
```

The UI can initially be extremely simple.

For example:

```text
file.txt       [Blue]
photo.png      [None]
school/        [Purple]
```

The purpose is to test metadata persistence rather than appearance.

---

# 9. File System Service

Create:

```text
IFileSystemService
```

with functionality such as:

```text
ListDirectory()
CreateDirectory()
Rename()
Move()
Copy()
Delete()
Exists()
GetMetadata()
```

Color metadata should not be directly implemented inside this service.

Instead:

```text
FileSystemService
ColorService
```

remain separate.

---

# 10. Color Service

Create:

```text
IColorService
```

Possible responsibilities:

```text
AssignColor(path, color)
RemoveColor(path)
GetColor(path)
GetFilesByColor(color)
MoveMetadata(oldPath, newPath)
CopyMetadata(source, destination)
```

Eventually:

```text
GetEffectiveColor(path)
```

may determine whether the displayed color came from:

```text
Manual assignment
Automatic rule
Folder inheritance
Default state
```

---

# 11. Navigation System

Create:

```text
NavigationService
│
├── CurrentPath
├── NavigateTo()
├── Back()
├── Forward()
└── Up()
```

The WebView should not own navigation history.

---

# 12. C# / JavaScript Bridge

Keep the API intentional.

Example API:

```text
filesystem.listDirectory
filesystem.rename
filesystem.copy
filesystem.move
filesystem.delete

navigation.open
navigation.back
navigation.forward
navigation.up

color.get
color.assign
color.remove
color.list

settings.get
settings.set

search.query
```

Later:

```text
rules.list
rules.create
rules.update
rules.delete
```

Example request:

```json
{
    "action": "color.assign",
    "path": "/home/user/test.txt",
    "color": "blue"
}
```

---

# 13. UI Foundation

Start with a Finder-inspired layout:

```text
+--------------------------------------------------------+
| <  >  ↑     /home/user/Documents          Search      |
+---------------+----------------------------------------+
|               |                                        |
| Favorites     |               Files                    |
|               |                                        |
| Home          |    folder                              |
| Desktop       |    image.png                           |
| Documents     |    report.pdf                          |
| Downloads     |    code.cs                             |
|               |                                        |
+---------------+----------------------------------------+
```

Support:

```text
Sidebar
Toolbar
Breadcrumbs
Grid view
List view
File selection
Search box
```

---

# 14. Early Visual Color Test

As soon as basic directory rendering works, display color information.

Do not wait until late development.

Example grid:

```text
┌───────────┐
│   📄      │
│ report    │
│ ███████   │
└───────────┘
     RED
```

Possible visual approaches:

```text
Colored underline
Colored filename
Colored icon background
Small tag
Colored selection border
Finder-style dot/tag
```

The UI should experiment with these early.

The final visual system does not need to be decided immediately.

---

# 15. Color Assignment UI Prototype

Early versions should allow color assignment through a temporary interface.

For example:

```text
Select file

Color:
[Red] [Orange] [Yellow] [Green] [Blue] [Purple] [Clear]
```

This can later become the right-click menu and keyboard shortcut system.

The important thing is to test:

```text
UI
 ↓
ColorService
 ↓
metadata
 ↓
reload directory
 ↓
correct color appears
```

---

# 16. File Selection

Selection should mainly live in the frontend.

Support:

```text
Single click
Ctrl/Cmd + click
Shift + click
Select all
Click empty space to deselect
```

Color assignment should work on multiple selected files.

Example:

```text
Selected:
photo1.png
photo2.png
photo3.png

Assign Blue

→ all three become Blue
```

This should eventually be one of ColorFinder's fastest workflows.

---

# 17. Finder-Style Context Menu

Later releases should provide:

```text
Open
Open With
Rename
Copy
Move
Duplicate
Delete
Get Info
--------------------------------
Color
    Red
    Orange
    Yellow
    Green
    Blue
    Purple
    Gray
    None
```

Assigning a color should require very little interaction.

The context menu is a core part of that experience.

---

# 18. Keyboard Color Shortcuts

Color assignment should eventually be available without opening menus.

Example:

```text
Ctrl/Cmd + 1    Red
Ctrl/Cmd + 2    Orange
Ctrl/Cmd + 3    Yellow
Ctrl/Cmd + 4    Green
Ctrl/Cmd + 5    Blue
Ctrl/Cmd + 6    Purple
Ctrl/Cmd + 0    Clear
```

Exact shortcuts should be configurable.

The user should be able to:

```text
Select files
Press shortcut
Continue working
```

with no modal dialog.

---

# 19. Automatic Color Rules

This is a later major ColorFinder feature.

Create a rule system such as:

```text
*.cs        → Blue
*.cpp       → Blue
*.pdf       → Red
*.png       → Green
*.jpg       → Green
*.zip       → Orange
```

Represent rules independently:

```csharp
ColorRule
{
    Id
    MatchType
    Pattern
    ColorId
    Enabled
    Priority
}
```

Possible match types later:

```text
Extension
Filename
Directory
MIME type
File size
Date
Regular expression
```

Start with extensions only.

---

# 20. Manual vs Automatic Colors

Manual assignment should probably override automatic rules.

Example:

```text
Rule:

*.pdf → Red

report.pdf

Automatic color:
Red
```

User manually assigns:

```text
report.pdf → Blue
```

Effective color becomes:

```text
Blue
```

Internally:

```text
Automatic: Red
Manual: Blue
Effective: Blue
```

If the manual assignment is removed:

```text
Effective: Red
```

This behavior should be designed into the model before automatic rules are implemented.

---

# 21. Color Search

Searching by color is a core feature.

Eventually support:

```text
Search:
Blue
```

or UI filters:

```text
Color
[ ] Red
[ ] Orange
[ ] Yellow
[x] Green
[ ] Blue
[ ] Purple
```

Color search should be able to operate:

```text
Current directory

Current directory recursively

Configured search locations

Entire home directory
```

Later searches should support combinations:

```text
Color = Blue

AND

Extension = .cs
```

or:

```text
Color = Red

AND

Modified < 7 days ago
```

---

# 22. Search Architecture

Do not treat search as simply filtering the currently displayed HTML elements.

Searching belongs in C#.

Create:

```text
SearchService
```

Supporting queries such as:

```csharp
SearchQuery
{
    Text
    Colors
    Extensions
    Recursive
}
```

Later:

```text
MinimumSize
MaximumSize
ModifiedAfter
ModifiedBefore
FileType
```

---

# 23. Color Index

Once there are many files, scanning the filesystem every time the user searches by color will not be ideal.

Later versions should maintain a lightweight metadata index.

Conceptually:

```text
Color Index

Path                         Color
----------------------------------
~/Documents/report.pdf       Red
~/Projects/test.cs           Blue
~/Photos/vacation.jpg        Green
```

This could eventually use SQLite if justified.

Do not introduce SQLite early.

Start simple.

Move to a database only when performance or metadata complexity demonstrates the need.

---

# 24. File Operations and Color Metadata

Color metadata must behave correctly when ColorFinder performs filesystem operations.

Examples:

### Rename

```text
report.pdf [Blue]

Rename →

final-report.pdf [Blue]
```

### Move

```text
Documents/report.pdf [Red]

Move →

Archive/report.pdf [Red]
```

### Duplicate

Likely behavior:

```text
report.pdf [Green]

Duplicate →

report copy.pdf [Green]
```

These behaviors should be tested.

---

# 25. Files Changed Outside ColorFinder

Eventually ColorFinder must deal with:

```text
External rename
External move
External deletion
```

This may require:

```text
FileSystemWatcher
```

and/or stronger metadata identification.

This should be considered a later technical milestone.

Do not overengineer it during the first releases.

---

# 26. File Previews

Add previews after navigation and file operations are stable.

Possible previews:

```text
Images
Text
Code
Markdown
Metadata
```

The WebView can render many formats directly.

---

# 27. Settings

Use:

```text
~/.config/colorfinder/settings.json
```

Possible settings:

```json
{
    "viewMode": "grid",
    "showHiddenFiles": false,
    "thumbnailSize": 96,
    "theme": "dark",
    "showColorLabels": true
}
```

Automatic color rules should probably live separately from general settings.

For example:

```text
settings.json
colors.json
rules.json
```

until a more structured metadata system is needed.

---

# 28. Theme vs File Colors

Keep these concepts separate.

ColorFinder has:

```text
Application theme colors
```

and:

```text
File classification colors
```

Changing the UI accent color should not alter file color tags.

Use CSS variables for application appearance:

```css
:root {
    --background: #151515;
    --surface: #202020;
    --accent: #709cff;
    --text: #ffffff;
}
```

File colors should come from ColorFinder metadata.

---

# 29. Platform Abstraction

Create:

```text
Platform/
│
├── IPlatformService.cs
│
├── LinuxPlatformService.cs
└── MacPlatformService.cs
```

Responsibilities may include:

```text
Open file
Open terminal here
Reveal file
Move to trash
System paths
File permissions
Native dialogs
```

---

# 30. Testing Strategy

Most tests should target `ColorFinder.Core`.

Tests should include normal file-manager functionality and ColorFinder-specific behavior.

Basic tests:

```text
Directory listing
Path handling
Navigation history
Sorting
Search
Settings serialization
```

Color tests:

```text
Assign color
Read assigned color
Remove color
Color survives reload
Multiple files can share a color
Files can have different colors
```

Rule tests:

```text
.cs matches Blue rule

.txt does not match .cs rule

Manual assignment overrides rule

Removing manual assignment restores automatic color

Disabled rule does not apply
```

Operation tests:

```text
Rename preserves color

Move preserves color

Delete removes stale metadata

Duplicate copies color
```

Search tests:

```text
Search Blue returns Blue files

Search Red excludes Blue files

Search Blue + .cs returns only Blue C# files
```

These tests should start appearing well before the full color UI is finished.

---

# 31. Performance Rules

Never block the UI thread with large filesystem operations.

Avoid reading unnecessary file contents.

Load thumbnails lazily.

Do not send giant directory datasets repeatedly between C# and JavaScript.

Keep dependencies small.

Use built-in browser APIs before adding frontend packages.

Use built-in .NET APIs before adding NuGet packages.

---

# 32. Development Milestones

## Milestone 1 — WebView Prototype

Build:

- [x] C# executable
- [x] Native system WebView
- [x] Local HTML
- [x] JavaScript → C# communication
- [x] C# → JavaScript communication

Then:

- [x] List the user's home directory
- [x] Render directory entries

---

# 33. Version 0.1 — Basic Finder

ColorFinder should support:

- [x] Launch on Linux
- [x] Open at the user's home directory
- [x] Browse folders
- [x] Navigate back
- [x] Navigate forward
- [x] Navigate up
- [ ] Breadcrumb navigation
- [x] Grid view
- [x] List view
- [ ] Open files with the operating system
- [x] Single-item selection
- [ ] Multi-item selection

### Early Color Experiment

Also include:

- [x] Assign one of several predefined colors
- [x] Clear an assigned color
- [x] Display color as a left-edge bar in grid and list views
- [x] Persist color metadata across application restarts

The interface can still be rough.

The point is validating the central ColorFinder concept immediately.

---

# 34. Version 0.2 — File Management

Add:

- [ ] Rename
- [ ] Create folder
- [ ] Copy
- [ ] Move
- [ ] Duplicate
- [ ] Delete
- [ ] Context menus
- [ ] Keyboard shortcuts

### Color Improvements

Add:

- [ ] Multi-selection color assignment
- [ ] Preserve color during rename
- [ ] Preserve color during move
- [ ] Copy color during duplication

Begin establishing the final color workflow.

---

# 35. Version 0.3 — ColorFinder Foundation

This should be the point where ColorFinder starts becoming meaningfully different from a normal file manager.

Add:

```text
Full color picker/tag UI
Color context-menu options
Keyboard color shortcuts
Color-based filtering
Color-based search
Custom color names
Color settings
```

At this stage:

```text
Select files
Right-click
Choose Blue
```

should be polished and fast.

---

# 36. Version 0.4 — Automatic Colors

Introduce automatic color rules.

Initial rule type:

```text
File extension
```

Example configuration:

```text
.cs      Blue
.cpp     Blue
.pdf     Red
.png     Green
.jpg     Green
.zip     Orange
```

Add:

```text
Rule creation
Rule editing
Rule deletion
Rule enable/disable
Rule priority
Manual override
```

Test rule precedence carefully.

---

# 37. Version 0.5 — Advanced Search

Expand search.

Support:

```text
Filename
Color
Extension
Directory
Recursive search
```

Allow combinations:

```text
Blue + .cs

Red + PDF

Green + Images
```

Begin evaluating whether the simple metadata storage system is still sufficient.

---

# 38. Version 0.6 — Metadata Index

If performance requires it, introduce a dedicated index.

Potentially:

```text
SQLite
```

Store:

```text
Files
Colors
Rules
Metadata
Search information
```

Do not do this unless measurements show it is needed.

---

# 39. Version 0.7 — Finder-Like Polish

Focus heavily on usability.

Add:

```text
Better sidebar
Favorites
Recent locations
Mounted drives
Improved breadcrumbs
Quick Look-style preview
Drag and drop
Better context menus
File info panel
Toolbar customization
```

Color coding should remain integrated throughout the UI.

---

# 40. Version 0.8 — Advanced Color System

Expand beyond extension rules.

Possible automatic rules:

```text
Filename pattern
Directory
MIME type
File age
File size
Regex
```

Examples:

```text
*.cs                     Blue

Downloads/*.zip          Orange

Files older than 1 year  Gray

Name contains "FINAL"    Green

Files > 1 GB             Red
```

Allow rule precedence and manual overrides.

---

# 41. Version 0.9 — Platform Integration

Linux:

```text
Wayland testing
X11 testing
Trash integration
Open-with support
Desktop file
Application icons
Common distro testing
```

macOS:

```text
.app bundle
Trash integration
Open-with
Finder reveal
macOS keyboard behavior
Application menus
```

---

# 42. Version 1.0 — Full ColorFinder Experience

ColorFinder 1.0 should feel like:

```text
Finder-style file management
            +
powerful visual organization
```

Core capabilities:

```text
✓ Fast navigation

✓ Grid and list views

✓ File management

✓ Preview support

✓ Persistent colors

✓ Color search

✓ Color filtering

✓ Multi-file color assignment

✓ Right-click color assignment

✓ Keyboard color shortcuts

✓ Automatic extension rules

✓ Manual override of automatic rules

✓ Custom colors

✓ Custom rule configuration

✓ Linux support

✓ macOS support
```

The goal is that color becomes another meaningful property of a file.

Users should think in terms like:

```text
"Show me all my blue files."

"Make all .cs files blue."

"Mark these five folders red."

"Show me green PDFs."

"Make everything in this project purple."
```

That is what distinguishes ColorFinder from simply being a Finder clone.

---

# 43. Long-Term Architecture

```text
                         ColorFinder
                              │
                    ColorFinder.Desktop
                              │
               ┌──────────────┴──────────────┐
               │                             │
          WebView Host                Platform Services
               │                             │
        HTML / CSS / JS                 Linux / macOS
               │
               │ JSON / RPC
               │
               ▼
                      ColorFinder.Core
                              │
      ┌──────────┬────────────┼────────────┬──────────┐
      │          │            │            │          │
 FileSystem  Navigation    Search       Colors      Rules
      │                                    │          │
      │                                    └────┬─────┘
      │                                         │
      └─────────────── Metadata / Index ─────────┘
                              │
                              ▼
                         .NET APIs
```

The major design rule remains:

**The UI presents information. C# owns the application.**

A second rule should now be added:

**Color is application metadata, not just CSS.**

That distinction is extremely important.

---

# 44. Recommended First Development Session

The first session should accomplish:

1. [x] Create `ColorFinder.sln`.
2. [x] Create `ColorFinder.Core`.
3. [x] Create `ColorFinder.Desktop`.
4. [x] Start a native system WebView.
5. [x] Load local HTML.
6. [x] Establish JavaScript ↔ C# communication.
7. [x] List the user's home directory.
8. [x] Render the directory in HTML.
9. [x] Create the initial `ColorTag` model.
10. [x] Assign a test color to a file or folder.
11. [x] Save the assignment in app-owned metadata.
12. [x] Restart/recreate ColorFinder services.
13. [x] Confirm the color persists and renders after reload.

That final test is important.

Even though automatic rules, color searching, custom color sets, and polished assignment workflows belong to later releases, the fundamental idea of:

```text
File
   +
Persistent Color Metadata
```

should be proven almost immediately.

That prevents the project from becoming a generic file manager first and trying to bolt its defining feature on afterward.
