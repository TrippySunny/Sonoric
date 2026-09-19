using LibVLCSharp.Shared;

namespace Sonoric.Services;

public sealed class PlaybackService : IDisposable
{
    private static readonly object NativeGate = new();
    private static bool _nativeReady;

    private readonly LibVLC? _libVlc;
    private readonly MediaPlayer? _player;
    private Media? _media;
    private bool _disposed;

    public PlaybackService()
    {
        try
        {
            EnsureNative();
            _libVlc = new LibVLC("--no-video", "--no-osd");
            _player = new MediaPlayer(_libVlc);
            _player.TimeChanged += OnTimeChanged;
            _player.LengthChanged += OnLengthChanged;
            _player.Playing += OnPlaying;
            _player.Paused += OnPaused;
            _player.Stopped += OnStopped;
            _player.EndReached += OnEndReached;
            IsAvailable = true;
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            InitError = exception.Message;
        }
    }

    public bool IsAvailable { get; }
    public string? InitError { get; }
    public bool IsPlaying { get; private set; }
    public bool HasMedia => _player?.Media is not null;
    public long PositionMs { get; private set; }
    public long DurationMs { get; private set; }

    public int Volume
    {
        get => _player?.Volume ?? 0;
        set
        {
            if (_player is null)
            {
                return;
            }

            _player.Volume = Math.Clamp(value, 0, 100);
        }
    }

    public event Action? StateChanged;
    public event Action? PositionChanged;
    public event Action? TrackEnded;

    public void Play(string path)
    {
        if (_libVlc is null || _player is null)
        {
            return;
        }

        _media?.Dispose();
        _media = new Media(_libVlc, path, FromType.FromPath);
        _player.Play(_media);
        IsPlaying = true;
        StateChanged?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        if (_player is null || _player.Media is null)
        {
            return;
        }

        _player.SetPause(paused);
    }

    public void Seek(long positionMs)
    {
        if (_player is null || !_player.IsSeekable)
        {
            return;
        }

        _player.Time = Math.Max(0, positionMs);
        PositionMs = _player.Time;
        PositionChanged?.Invoke();
    }

    public void Stop()
    {
        _player?.Stop();
        IsPlaying = false;
        PositionMs = 0;
        StateChanged?.Invoke();
        PositionChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_player is not null)
        {
            _player.TimeChanged -= OnTimeChanged;
            _player.LengthChanged -= OnLengthChanged;
            _player.Playing -= OnPlaying;
            _player.Paused -= OnPaused;
            _player.Stopped -= OnStopped;
            _player.EndReached -= OnEndReached;
            _player.Stop();
            _player.Dispose();
        }

        _media?.Dispose();
        _libVlc?.Dispose();
    }

    private static void EnsureNative()
    {
        lock (NativeGate)
        {
            if (_nativeReady)
            {
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                LinuxVlcNative.Initialize();
            }
            else
            {
                Core.Initialize();
            }
            _nativeReady = true;
        }
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        PositionMs = e.Time;
        PositionChanged?.Invoke();
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        DurationMs = e.Length;
        PositionChanged?.Invoke();
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        IsPlaying = true;
        StateChanged?.Invoke();
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        IsPlaying = false;
        StateChanged?.Invoke();
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        IsPlaying = false;
        StateChanged?.Invoke();
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PositionMs = DurationMs;
        StateChanged?.Invoke();
        TrackEnded?.Invoke();
    }
}
