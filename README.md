# MajdataEdit-Neo

## Known bugs

[ ] **Waveform drag speed**: dragging the waveform backward is faster than dragging it forward.
[v] **Break parsing**: `1bx-3[8:1]` is parsed incorrectly (expected "tap break", got "slide normal").
[v] **Note counts differ**: note counts differ depending on note type and between MajdataEdit vs MajdataEdit-Neo (suspected parser issue).
[v] **Play/Pause accumulation**: pressing play/pause twice while paused (without manually scrolling the waveform) causes note counts to accumulate.
[v] **Touch notes stuck**: touch notes can get stuck on MajdataView after unpausing in the above scenario, and can also stick as a "miss" during autoplay.
[ ] **SFX timing**: reports that SFX may feel delayed.
[ ] **Text highlight timing**: the text line highlight can be too early.
[ ] **Follow Cursor behavior**: with Follow Cursor enabled during playback, clicking in the text editor can break following.

## Planned features

[ ] **Time signature support for the yellow guide lines**: allow non-4/4 by supporting configurable "yellow line" markers, possibly via a comment-based time signature syntax.
[ ] **Editor auto-wrap or auto-formatting**: split very long code lines into new lines when the window is small.
[ ] **Start delay option**: optional delay (for example 10 seconds) after pressing play to make recording easier.

## Acknowledgements

This project is based on the original work by **LingFeng-bbben**.

**Original repository**: `https://github.com/LingFeng-bbben/MajdataEdit-Neo`
