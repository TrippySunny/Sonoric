<p align="center">
  <img src="assets/logo_256.png" width="96" alt="Sonoric">
</p>

<h1 align="center">Sonoric</h1>

<p align="center">
  <strong>Standalone and multifunctional music player.</strong><br>
  Cross-platform audio player with dynamic audio-reactive shaders, offline library sync, and custom mood tagging.
</p>

<p align="center">
  <img src="https://shields.io" alt="Platform">
  <img src="https://shields.io" alt="Environment">
  <img src="https://shields.io" alt="License">
</p>

---

Sonoric is a free, open-source music player built for complete independence from streaming services. It processes your local audio files, visualizes them in real time using GPU shaders, and keeps your library synchronized across all your devices.

It is designed for audiophiles, offline music collectors, and anyone who wants a gorgeous, highly customizable media player.

## Core Features

| Feature | What it does |
| --- | --- |
| **Complete Offline Mode** | Operates entirely without an internet connection, keeping your data private and available anywhere. |
| **True Cross-Platform** | Seamless native support across Windows, Linux, and mobile operating systems. |
| **Format Omnivore** | Accepts and smoothly plays almost any standard audio file format without extra codecs. |
| **Audio-Reactive Shaders** | Generates stunning, real-time visual effects that physically react to the rhythm, bass, and frequencies of your music. |
| **Custom Mood Tagging** | Allows you to classify and filter your library using your own personalized mood categories rather than generic genres. |
| **Library Synchronization** | Synchronizes music collections, custom tags, and configuration data between multiple desktop and mobile devices. |
| **Custom Playlist Exchange** | Features a dedicated, lightweight playlist file format optimized for instant importing and exporting. |

## Viewer & Customization

The audio visualizer is powered by customizable shader effects that adapt immediately to the ongoing audio track.

- **Dynamic Feedback:** Shader algorithms analyze frequency spectra and waveform spikes to alter colors, scale shapes, and move visual layouts.
- **Playlist Architecture:** Export whole collections in a single clicks using the fast, lightweight `.sonoric` native file container.
- **Mood System:** Filter your entire music archive with precise multi-tag combinations based on how you feel.

## Supported Formats

`MP3` · `FLAC` · `WAV` · `OGG` · `M4A` · `AAC` · `OPUS`

## Run it

### Linux
Automated scripts are included to handle dependencies and system building:
```bash
chmod +x install-linux.sh
./install-linux.sh
./run.sh
```

### Windows
Launch the main solution file `Sonoric.sln` inside Visual Studio or any compatible .NET IDE, then build and run the executable.

## License

[MIT](LICENSE)
