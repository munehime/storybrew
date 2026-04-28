# Bundled ffmpeg (optional)

Drop the following binaries into this folder and they'll be picked up by the
build (debug, release, and the published zip):

```
ffmpeg.exe
ffprobe.exe
av*.dll        (avcodec, avformat, avutil, etc.)
sw*.dll        (swresample, swscale)
```

If you already have a working ffmpeg install (e.g. `C:\ffmpeg\bin\`),
copying its contents here is enough — no other config needed. Required for:

- Menu background video playback (`FfmpegVideoStream`)
- Project-editor video preview / scrubbing (`VideoPreview`)
- Storyboard video export

Without these binaries, image menu backgrounds and the rest of the editor
still work fine — you'll just see "ffmpeg not found" in the trace log when
trying to use video features.

## Why the binaries aren't checked in

ffmpeg is LGPL. Source-redistributing a binary copy here in git mixes
licensing in ways most fork maintainers don't want to think about. The
folder being committed (with this README and a `.gitignore` pinning
everything out) means the build infrastructure is in place — you just
choose locally whether to bundle.

Grab official Windows builds from <https://www.gyan.dev/ffmpeg/builds/> or
<https://www.un4seen.com/> for BASS-style.
