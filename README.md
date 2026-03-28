# MajdataEdit-Neo

## Known bugs

- [x] **Waveform drag speed**: dragging the waveform backward is faster than dragging it forward.
- [x] **Break parsing**: `1bx-3[8:1]` is parsed incorrectly (expected "tap break", got "slide normal").
- [x] **Note counts differ**: note counts differ depending on note type and between MajdataEdit vs MajdataEdit-Neo (suspected parser issue).
- [x] **Play/Pause accumulation**: pressing play/pause twice while paused (without manually scrolling the waveform) causes note counts to accumulate.
- [x] **Touch notes stuck**: touch notes can get stuck on MajdataView after unpausing in the above scenario, and can also stick as a "miss" during autoplay.
- [x] **SFX timing**: reports that SFX may feel delayed.
- [x] **Text highlight timing**: the text line highlight can be too early.
- [x] **Follow Cursor behavior**: with Follow Cursor enabled during playback, clicking in the text editor can break following.
- [x] **Follow Cursor resets scroller**: with Follow Cursor enabled, placing a new note can reset the scroller back to the starting position.
- [x] **Copy resets scroller**: when you copy something, the scroller resets back to `0`.
- [ ] **Loop-mode blank viewer**: in loop mode, the viewer can appear blank after a few loops, then return to normal.
- [ ] **Loop marker beat snapping**: the loop marker does not snap to the beat.
- [ ] **Follow Cursor editor focus**: in Follow Cursor mode, you have to click on the text editor for the cursor to appear or update properly.

## Planned features

- [ ] **Time signature support for the yellow guide lines**: allow non-4/4 by supporting configurable "yellow line" markers, possibly via a comment-based time signature syntax.
- [x] **Editor auto-wrap or auto-formatting**: split very long code lines into new lines when the window is small.
- [ ] **Start delay option**: optional delay (for example 10 seconds) after pressing play to make recording easier.
- [x] **Discord RPC**: display playback state via Discord Rich Presence.
- [x] **Center display (Off/Combo)**: add a center display mode toggle between `Off` and `Combo`.
- [x] **Changeable play mode (Default/DJAuto)**: add a play mode selector between `Default` and `DJAuto`.
- [x] **Note speed change**: allow changing note speed via UI/setting.
- [x] **Editor settings**: add persistent editor settings using JSON file.
- [x] **Keybindings shortcut customization**: configurable shortcuts in `EditorSetting.json`, the **Edit** and **File** menus show each shortcut on the right via `InputGesture`, and the play/stop toolbar buttons still have tooltips.

## Acknowledgements

This project is based on the original work by **LingFeng-bbben**.

**Original repository**: [LingFeng-bbben/MajdataEdit-Neo](https://github.com/LingFeng-bbben/MajdataEdit-Neo)
