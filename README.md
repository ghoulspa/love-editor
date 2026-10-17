# Love Editor

A small, fast code editor for making games with [LÖVE](https://love2d.org) on Windows. LÖVE comes bundled, so you can open a project and press Play without installing or configuring anything else.

## Features

- File tree with new file, new folder, rename, delete to the Recycle Bin, and drag-and-drop import
- Tabs for code files and image previews
- Lua syntax highlighting, auto-indent and multiple cursors (Ctrl+D adds the next match)
- Autocomplete with a docs panel, parameter hints and hover docs for Lua and the whole LÖVE API, with squiggles under mistakes, powered by the bundled [Lua Language Server](https://github.com/LuaLS/lua-language-server)
- Format Document (Shift+Alt+F) and Format Selection from the right-click menu
- Play (F5) runs your game with the bundled LÖVE and shows `print()` output live, with clickable error lines
- New Project creates a ready-to-run `main.lua` and `conf.lua`

## Requirements

Windows 10 or 11 with .NET Framework 4.8, which current versions of Windows already include.

Windows 7 SP1 and 8.1 work too once .NET Framework 4.8 is installed, with less: `print()` output doesn't show in the Output panel, and on Windows 7 there's no autocomplete, docs, squiggles or formatting.

## Building

Install the [.NET SDK](https://dotnet.microsoft.com/download) and run `dotnet build`. The build downloads LÖVE 11.5, the Lua Language Server and the LÖVE API definitions next to the editor, which ends up at `bin\Debug\net48\LoveEditor.exe`.

## License

MIT, see [LICENSE](LICENSE). LÖVE and the Lua Language Server have their own licenses, which are included in the `love` and `lua-language-server` folders of every build.
