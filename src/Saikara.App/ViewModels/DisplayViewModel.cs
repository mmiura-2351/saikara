using System;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Saikara.App.Audio;
using Saikara.Core.Audio;
using Saikara.Core.Lyrics;
using Saikara.Core.Music;
using Saikara.Core.Scoring;

namespace Saikara.App.ViewModels;

/// <summary>
/// View-model for the display window: the two-line color-wipe lyric telop synced to playback (P2),
/// plus the real-time pitch bar that shows the singer's pitch against the reference melody (P3)
/// (REQUIREMENTS §5–§6).
/// </summary>
/// <remarks>
/// <para>
/// Sourcing. Lyrics come from the App audio engine via <see cref="ITelopSource"/> and the reference
/// melody via <see cref="IReferenceSource"/> (both built from the transformed song so they match the
/// audio clock and key); the live sung pitch comes from <see cref="IPitchMonitor"/>. The VM injects
/// the platform-agnostic <see cref="IAudioEngine"/> and casts it to the two App surfaces; if a cast
/// fails the corresponding feature simply stays idle and the window still renders.
/// </para>
/// <para>
/// Sync. A ~30 fps <see cref="DispatcherQueueTimer"/> samples <see cref="ITelopSource.Position"/> and
/// recomputes the telop and the active-reference-note properties. The sung-pitch properties are
/// instead pushed by <see cref="IPitchMonitor.PitchDetected"/> (already marshaled to the UI thread)
/// so the marker reacts at the hop rate rather than waiting for the next frame. All property writes
/// therefore happen on the UI thread.
/// </para>
/// <para>
/// Axis. Reference and sung pitches are both mapped to a shared, normalised vertical axis in
/// <c>[0, 1]</c> (0 = bottom = lowest pitch, 1 = top = highest pitch) spanning the reference melody's
/// MIDI range padded by <see cref="AxisPaddingSemitones"/>. The view turns the normalised values into
/// pixel positions. When there is no reference (instrumental / undetected melody) a default vocal
/// range is used so a sung marker still has somewhere to sit.
/// </para>
/// </remarks>
public partial class DisplayViewModel : ObservableObject, IDisposable
{
    /// <summary>Telop / pitch-bar refresh rate. 30 fps is smooth without burning the UI thread.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    /// <summary>Extra semitones of head- and foot-room added above/below the reference range on the axis.</summary>
    private const int AxisPaddingSemitones = 4;

    /// <summary>Fallback axis bounds (MIDI) when no reference melody is available: a comfortable vocal span (≈ C3–C5).</summary>
    private const int DefaultAxisMin = 48;
    private const int DefaultAxisMax = 72;

    private readonly IAudioEngine _audioEngine;
    private readonly ITelopSource? _telopSource;
    private readonly IReferenceSource? _referenceSource;
    private readonly IPitchMonitor? _pitchMonitor;
    private readonly IScoringEngine _scoringEngine;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer? _frameTimer;

    /// <summary>
    /// The transport state seen on the previous <see cref="IAudioEngine.StateChanged"/>. Used to fire
    /// scoring only on a Playing → Stopped transition (a song that actually ran and ended/was stopped),
    /// never on a Stop that happens before any playback (e.g. the engine settling to Stopped on Load).
    /// </summary>
    private PlaybackState _lastPlaybackState = PlaybackState.Stopped;

    private TelopPlayback _playback = new(Array.Empty<TelopLine>());
    private System.Collections.Generic.IReadOnlyList<ReferenceNote> _referenceNotes = Array.Empty<ReferenceNote>();
    private int _axisMin = DefaultAxisMin;
    private int _axisMax = DefaultAxisMax;
    private bool _disposed;

    /// <summary>
    /// Shown for the current line when the song has no lyrics (instrumental) or none have started.
    /// </summary>
    private const string InstrumentalPlaceholder = "♪";

    public DisplayViewModel(
        IAudioEngine audioEngine,
        IPitchMonitor pitchMonitor,
        IScoringEngine scoringEngine,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(audioEngine);
        ArgumentNullException.ThrowIfNull(pitchMonitor);
        ArgumentNullException.ThrowIfNull(scoringEngine);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _dispatcherQueue = dispatcherQueue;
        _audioEngine = audioEngine;
        _telopSource = audioEngine as ITelopSource;
        _referenceSource = audioEngine as IReferenceSource;
        _pitchMonitor = pitchMonitor;
        _scoringEngine = scoringEngine;

        if (_referenceSource is not null)
        {
            _referenceSource.ReferenceChanged += OnReferenceChanged;
            RebuildReference();
        }

        _pitchMonitor.PitchDetected += OnPitchDetected;

        // P4: score and show the result at song end. The engine raises StateChanged when playback
        // settles to Stopped (OnPositionTimerTick on end-of-song, or an explicit Stop); we score only
        // on a real Playing → Stopped transition (see OnPlaybackStateChanged), so a Stop before any
        // playback never triggers scoring. The handler marshals to the UI thread.
        _audioEngine.StateChanged += OnPlaybackStateChanged;
        _lastPlaybackState = _audioEngine.State;

        if (_telopSource is not null)
        {
            _telopSource.TelopChanged += OnTelopChanged;
            RebuildPlayback();
        }

        // A ~30 fps timer is the wipe's clock and the reference-note sampler. It runs whenever the
        // window is up; the sung marker is updated separately by the monitor's PitchDetected event.
        if (_telopSource is not null || _referenceSource is not null)
        {
            _frameTimer = _dispatcherQueue.CreateTimer();
            _frameTimer.Interval = FrameInterval;
            _frameTimer.Tick += OnFrameTick;
            _frameTimer.Start();
        }
    }

    // ---- Telop (P2) ----

    /// <summary>The text of the current (upper) telop line. Empty-state shows an instrumental marker.</summary>
    [ObservableProperty]
    private string _currentLineText = InstrumentalPlaceholder;

    /// <summary>The text of the next (lower) telop line, shown dimmer and un-wiped. Empty when none.</summary>
    [ObservableProperty]
    private string _nextLineText = string.Empty;

    /// <summary>
    /// How far the color-wipe has swept across the current line, in <c>[0, 1]</c>. The view maps
    /// this to the shared offset of the two coincident gradient stops on the current line's brush.
    /// </summary>
    [ObservableProperty]
    private double _wipeFraction;

    /// <summary>
    /// <see langword="true"/> when the current line is real lyric text (a wipe should run);
    /// <see langword="false"/> for the instrumental placeholder (no wipe, full bright color).
    /// </summary>
    [ObservableProperty]
    private bool _hasCurrentLyric;

    // ---- Pitch bar (P3) ----

    /// <summary>
    /// <see langword="true"/> when a reference note is active at the current playback position, so the
    /// target line should be shown. <see langword="false"/> between notes / before the melody starts.
    /// </summary>
    [ObservableProperty]
    private bool _hasReferenceNote;

    /// <summary>The active reference note's MIDI number (valid only when <see cref="HasReferenceNote"/>).</summary>
    [ObservableProperty]
    private int _referenceMidiNote;

    /// <summary>
    /// Normalised vertical position of the active reference note in <c>[0, 1]</c> (0 = bottom of the
    /// axis = lowest pitch, 1 = top = highest pitch). The view maps this to a pixel Y for the target
    /// line. Meaningful only when <see cref="HasReferenceNote"/>.
    /// </summary>
    [ObservableProperty]
    private double _referenceNormalized;

    /// <summary>
    /// <see langword="true"/> when the latest mic hop was voiced, so the sung marker should be shown
    /// (and bright); <see langword="false"/> for silence/noise, where the view hides or dims it.
    /// </summary>
    [ObservableProperty]
    private bool _isSungVoiced;

    /// <summary>The sung (possibly fractional) MIDI note of the latest voiced hop. Valid only when <see cref="IsSungVoiced"/>.</summary>
    [ObservableProperty]
    private double _sungMidiNote;

    /// <summary>
    /// Normalised vertical position of the sung pitch in <c>[0, 1]</c> on the same axis as
    /// <see cref="ReferenceNormalized"/>, clamped to the axis. The view maps this to the marker's
    /// pixel Y. Meaningful only when <see cref="IsSungVoiced"/>.
    /// </summary>
    [ObservableProperty]
    private double _sungNormalized;

    /// <summary>Whether the microphone is unavailable, so the view can show a "no mic" hint.</summary>
    public bool IsMicAvailable => _pitchMonitor?.IsAvailable ?? false;

    // ---- Scoring result (P4) ----

    /// <summary>
    /// <see langword="true"/> while the end-of-song result overlay is shown. Set by
    /// <see cref="ShowResult"/> at song end and cleared by <see cref="CloseResultCommand"/>. The view
    /// binds the overlay's visibility to this.
    /// </summary>
    [ObservableProperty]
    private bool _isResultVisible;

    /// <summary>The overall score, 0-100, shown large. Rounded to a whole number for display.</summary>
    [ObservableProperty]
    private int _overallScore;

    /// <summary>The coarse letter grade (S/A/B/C/D/E) shown prominently next to the overall score.</summary>
    [ObservableProperty]
    private string _grade = string.Empty;

    /// <summary>Pitch-accuracy (音程) sub-score, 0-100, formatted for display.</summary>
    [ObservableProperty]
    private string _pitchAccuracyText = string.Empty;

    /// <summary>Stability (安定性) sub-score, 0-100, formatted for display.</summary>
    [ObservableProperty]
    private string _stabilityText = string.Empty;

    /// <summary>Expression (抑揚) sub-score, 0-100, formatted for display.</summary>
    [ObservableProperty]
    private string _expressionText = string.Empty;

    /// <summary>Long-tone (ロングトーン) sub-score, 0-100, formatted for display.</summary>
    [ObservableProperty]
    private string _longToneText = string.Empty;

    /// <summary>Pitch-accuracy sub-score as a <c>[0, 100]</c> value bound to a ProgressBar.</summary>
    [ObservableProperty]
    private double _pitchAccuracyValue;

    /// <summary>Stability sub-score as a <c>[0, 100]</c> value bound to a ProgressBar.</summary>
    [ObservableProperty]
    private double _stabilityValue;

    /// <summary>Expression sub-score as a <c>[0, 100]</c> value bound to a ProgressBar.</summary>
    [ObservableProperty]
    private double _expressionValue;

    /// <summary>Long-tone sub-score as a <c>[0, 100]</c> value bound to a ProgressBar.</summary>
    [ObservableProperty]
    private double _longToneValue;

    /// <summary>Detected vibrato (ビブラート) count, formatted for display.</summary>
    [ObservableProperty]
    private string _vibratoCountText = string.Empty;

    /// <summary>Detected shakuri (しゃくり) count, formatted for display.</summary>
    [ObservableProperty]
    private string _shakuriCountText = string.Empty;

    /// <summary>Detected kobushi (こぶし) count, formatted for display.</summary>
    [ObservableProperty]
    private string _kobushiCountText = string.Empty;

    /// <summary>Hides the result overlay. Bound to the overlay's Close button.</summary>
    [RelayCommand]
    private void CloseResult() => IsResultVisible = false;

    /// <summary>
    /// Handles a transport state change. Scoring runs only on a real <see cref="PlaybackState.Playing"/>
    /// → <see cref="PlaybackState.Stopped"/> transition (a song that actually ran and then ended or was
    /// stopped), so a Stop that happens before any playback (e.g. on Load) is ignored. Marshals to the
    /// UI thread because <see cref="IAudioEngine.StateChanged"/> may arrive on a background thread.
    /// </summary>
    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        PlaybackState state = _audioEngine.State;

        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyPlaybackState(state);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => ApplyPlaybackState(state));
        }
    }

    private void ApplyPlaybackState(PlaybackState state)
    {
        if (_disposed)
        {
            return;
        }

        bool endedFromPlaying = state == PlaybackState.Stopped && _lastPlaybackState == PlaybackState.Playing;
        _lastPlaybackState = state;

        if (endedFromPlaying)
        {
            TryScoreAndShow();
        }
    }

    /// <summary>
    /// Scores the just-finished performance and, when there is something to show, reveals the result
    /// overlay. Skips silently (no overlay) when there is no reference melody (instrumental) or no
    /// voiced mic samples (no microphone / no singing), so an instrumental playthrough or a run with
    /// the mic off never pops a meaningless all-zero score.
    /// </summary>
    private void TryScoreAndShow()
    {
        if (_pitchMonitor is null || _referenceSource is null)
        {
            return;
        }

        var samples = _pitchMonitor.CollectedSamples;
        var reference = _referenceSource.CurrentReferenceNotes;

        // No reference (instrumental / undetected melody) or no voiced singing → nothing meaningful to
        // score. The scoring engine would return ScoreResult.Empty (all zeros); skip the overlay.
        if (reference.Count == 0 || !samples.Any(s => s.IsVoiced))
        {
            return;
        }

        ScoreResult result = _scoringEngine.Score(samples, reference);
        ShowResult(result);
    }

    /// <summary>
    /// Pushes a <see cref="ScoreResult"/> into the bound result properties and shows the overlay.
    /// Numbers are formatted with the invariant culture so the display is stable regardless of the
    /// machine locale. Runs on the UI thread (callers marshal first).
    /// </summary>
    private void ShowResult(ScoreResult result)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;

        OverallScore = (int)Math.Round(result.Overall, MidpointRounding.AwayFromZero);
        Grade = result.Grade;

        PitchAccuracyValue = result.PitchAccuracy;
        StabilityValue = result.Stability;
        ExpressionValue = result.Expression;
        LongToneValue = result.LongTone;

        PitchAccuracyText = result.PitchAccuracy.ToString("0", culture);
        StabilityText = result.Stability.ToString("0", culture);
        ExpressionText = result.Expression.ToString("0", culture);
        LongToneText = result.LongTone.ToString("0", culture);

        VibratoCountText = result.VibratoCount.ToString(culture);
        ShakuriCountText = result.ShakuriCount.ToString(culture);
        KobushiCountText = result.KobushiCount.ToString(culture);

        IsResultVisible = true;
    }

    // ---- Reference handling ----

    private void OnReferenceChanged(object? sender, EventArgs e)
    {
        // IReferenceSource raises this on the UI thread, but marshal defensively.
        if (_dispatcherQueue.HasThreadAccess)
        {
            RebuildReference();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(RebuildReference);
        }
    }

    /// <summary>
    /// Caches the new reference-note set and recomputes the shared pitch axis from its MIDI range
    /// (padded), falling back to a default vocal span when the melody is empty. Then refreshes the
    /// active-note properties so a freshly loaded song shows its target line immediately.
    /// </summary>
    private void RebuildReference()
    {
        _referenceNotes = _referenceSource!.CurrentReferenceNotes;
        ComputeAxis();
        UpdateReferenceFromPosition();
    }

    private void ComputeAxis()
    {
        if (_referenceNotes.Count == 0)
        {
            _axisMin = DefaultAxisMin;
            _axisMax = DefaultAxisMax;
            return;
        }

        int min = int.MaxValue;
        int max = int.MinValue;
        foreach (ReferenceNote note in _referenceNotes)
        {
            if (note.MidiNote < min) min = note.MidiNote;
            if (note.MidiNote > max) max = note.MidiNote;
        }

        _axisMin = Math.Max(MusicMath.MinMidiNote, min - AxisPaddingSemitones);
        _axisMax = Math.Min(MusicMath.MaxMidiNote, max + AxisPaddingSemitones);

        // Guard against a degenerate single-note range collapsing the axis to a point.
        if (_axisMax - _axisMin < 2 * AxisPaddingSemitones)
        {
            _axisMax = Math.Min(MusicMath.MaxMidiNote, _axisMin + 2 * AxisPaddingSemitones);
        }
    }

    /// <summary>Maps a (possibly fractional) MIDI note to a clamped <c>[0, 1]</c> vertical position on the axis.</summary>
    private double Normalize(double midiNote)
    {
        double span = _axisMax - _axisMin;
        if (span <= 0.0)
        {
            return 0.5;
        }

        double t = (midiNote - _axisMin) / span;
        return Math.Clamp(t, 0.0, 1.0);
    }

    /// <summary>
    /// Finds the reference note active at <paramref name="position"/> (the first whose
    /// <c>[Start, End)</c> window contains it) and updates the reference-line properties. Linear scan;
    /// the melody is short enough (hundreds of notes) that this per-frame cost is negligible.
    /// </summary>
    private void UpdateReferenceFromPosition()
    {
        if (_referenceSource is null)
        {
            return;
        }

        // The reference source and the telop source are the same engine instance, so the playback
        // clock comes from ITelopSource.Position; fall back to zero only if the engine is somehow not
        // an ITelopSource (then the reference stays parked at the song start).
        TimeSpan position = _telopSource?.Position ?? TimeSpan.Zero;

        ReferenceNote? active = null;
        foreach (ReferenceNote note in _referenceNotes)
        {
            if (position >= note.Start && position < note.End)
            {
                active = note;
                break;
            }

            // Notes are time-ordered; once a note starts after the position none later can contain it.
            if (note.Start > position)
            {
                break;
            }
        }

        if (active is { } n)
        {
            HasReferenceNote = true;
            ReferenceMidiNote = n.MidiNote;
            ReferenceNormalized = Normalize(n.MidiNote);
        }
        else
        {
            HasReferenceNote = false;
        }
    }

    // ---- Live sung pitch ----

    private void OnPitchDetected(object? sender, LivePitch live)
    {
        // PitchDetected is raised on the UI thread by PitchMonitor, but marshal defensively.
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyLivePitch(live);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => ApplyLivePitch(live));
        }
    }

    private void ApplyLivePitch(LivePitch live)
    {
        if (live.MidiNote is { } midi)
        {
            IsSungVoiced = true;
            SungMidiNote = midi;
            SungNormalized = Normalize(midi);
        }
        else
        {
            IsSungVoiced = false;
        }
    }

    // ---- Frame loop ----

    private void OnFrameTick(DispatcherQueueTimer sender, object args)
    {
        UpdateFromPosition();
        UpdateReferenceFromPosition();
    }

    // ---- Telop position sync (unchanged from P2) ----

    /// <summary>
    /// Rebuilds the <see cref="TelopPlayback"/> from the source's current lines and refreshes the
    /// view immediately so a freshly loaded song shows its first line before playback starts.
    /// </summary>
    private void RebuildPlayback()
    {
        _playback = new TelopPlayback(_telopSource!.CurrentTelopLines);
        UpdateFromPosition();
    }

    private void OnTelopChanged(object? sender, EventArgs e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            RebuildPlayback();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(RebuildPlayback);
        }
    }

    /// <summary>
    /// Samples the playback position and recomputes the bound telop properties. Runs on the UI
    /// thread (frame timer / dispatcher), so the property sets are already marshaled.
    /// </summary>
    private void UpdateFromPosition()
    {
        if (_telopSource is null)
        {
            return;
        }

        TimeSpan position = _telopSource.Position;
        var lines = _playback.Lines;

        if (lines.Count == 0)
        {
            SetTelop(InstrumentalPlaceholder, string.Empty, 0.0, hasLyric: false);
            return;
        }

        int active = _playback.ActiveLineIndex(position);
        if (active < 0)
        {
            SetTelop(
                lines[0].Text,
                lines.Count > 1 ? lines[1].Text : string.Empty,
                fraction: 0.0,
                hasLyric: true);
            return;
        }

        string current = lines[active].Text;
        string next = active + 1 < lines.Count ? lines[active + 1].Text : string.Empty;
        SetTelop(current, next, _playback.WipeFraction(position), hasLyric: true);
    }

    private void SetTelop(string current, string next, double fraction, bool hasLyric)
    {
        CurrentLineText = current;
        NextLineText = next;
        WipeFraction = fraction;
        HasCurrentLyric = hasLyric;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_frameTimer is not null)
        {
            _frameTimer.Stop();
            _frameTimer.Tick -= OnFrameTick;
        }

        if (_telopSource is not null)
        {
            _telopSource.TelopChanged -= OnTelopChanged;
        }

        if (_referenceSource is not null)
        {
            _referenceSource.ReferenceChanged -= OnReferenceChanged;
        }

        if (_pitchMonitor is not null)
        {
            _pitchMonitor.PitchDetected -= OnPitchDetected;
        }

        _audioEngine.StateChanged -= OnPlaybackStateChanged;
    }
}
