using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utilities {
    public static Color ColorWithAlpha(Color tweakColor, float newAlpha) {
        tweakColor.a = newAlpha;
        return tweakColor;
    }
}

public class Fretboard : MonoBehaviour {
    public const uint MinBPM = 120;
    public const uint MaxBPM = 220;

    public GameObject TargetBar;
    public GameObject[] NoteLines;
    public Sprite NoteSprite;
    public Sprite FretSprite;
    public AudioClip MetronomeBeatClip;
    public float TreadmillDistanceBetweenDownbeats = 3.0f;
    public float TreadmillLength = 8.0f;
    public float TapWindowSlopTimePositive = 0.1f;
    public float TapWindowSlopTimeNegative = -0.1f;

    public float SecondsPerBeat {
        get {
            return 60.0f / AudioHelm.AudioHelmClock.GetGlobalBpm();
        }
    }

    private bool treadmillRunning = false;
    public bool TreadmillRunning {
        get {
            return treadmillRunning;
        }
    }

    private bool gameplayRunning = false;
    public bool GameplayRunning {
        get {
            return gameplayRunning;
        }
    }

    public uint FretCount {
        get {
            return (uint)treadmillFrets.Count;
        }
    }

    private bool audibleMetronome = false;
    public bool AudibleMetronome {
        get {
            return audibleMetronome;
        }
    }

    private bool musicPlaying = false;
    public bool MusicPlaying {
        get {
            return musicPlaying;
        }
    }

    public delegate void OnNotePlayedHandler(float score);
    public event OnNotePlayedHandler OnNotePlayed;
    public delegate void OnNoteMissedHandler();
    public event OnNoteMissedHandler OnNoteMissed;
    public delegate void OnClamPlayedHandler();
    public event OnClamPlayedHandler OnClamPlayed;
    public delegate void OnMusicFinishedHandler();
    public event OnMusicFinishedHandler OnMusicFinished;

    // Impacting with the target line with sprites is center/center, but visually needs to be
    // when the outer edges of the sprites touch. Plus probably a little visual slack.
    private const float kTargetLineOffset = 0.25f;

    private class LineTapData {
        public int active = -1;
        public float time = 0.0f;
    }

    private TreadmillNotePool treadmillNotePool;
    private MetronomeClickPool metronomeClickPool;
    private List<AudioSource> playingClicks = new List<AudioSource>();
    private Queue<float> sortedClickQueue = new Queue<float>();
    private FeedbackSuccessNote[] successNotes;
    private LineTapData[] lineTapsActive;
    private List<GameObject> treadmillFrets = new List<GameObject>();
    private GameObject treadmillGameObject;
    private int lastMetronomeTick;
    private uint musicDelayMs;
    private uint musicSkipMs;
    private uint musicMeasuresToSkip;
    private uint musicBPM;
    private float musicStartMetronomeTime;
    List<GameplayController.Note> noteList;
    private GameObject musicGameObject = null;
    private AudioSource musicAudioSource = null;
    private bool autoMute = true;

    public class SequencerNote : GameplayController.Note {
        public AudioHelm.Sequencer Sequencer = null;
        public SequencerNote(GameplayController.Note note, AudioHelm.Sequencer sequencer) : base(note) {
            this.Sequencer = sequencer;
        }
    }

    private class GameplayData {
        public class GameplayNote {
            public Fretboard.SequencerNote SequencerNote = null;
            public uint LineIndex = 0;
            public GameplayNote(Fretboard.SequencerNote sequencerNote, uint lineIndex) {
                this.SequencerNote = sequencerNote;
                this.LineIndex = lineIndex;
            }
        }

        public List<GameplayNote> GameplayNotes = new List<GameplayNote>();
        public uint GameplayNoteIndex = 0;

        public class TrackData {
            public List<SequencerNote> SequencerNotes = new List<SequencerNote>();
            public uint SequencerNoteIndex = 0;
            public uint InstrumentPriority = 0;
            public AudioHelm.Sequencer GameplaySequencer = null;
            public AudioHelm.Sequencer OriginalSequencer = null;

            public TrackData(GameplayController.SequencerTrackData sequencerTrackData) {
                this.InstrumentPriority = sequencerTrackData.Priority;
                this.OriginalSequencer = sequencerTrackData.Sequencer;
                this.GameplaySequencer = UnityEngine.Object.Instantiate(sequencerTrackData.Sequencer);
            }
        }

        public Dictionary<uint, TrackData> TrackDataByTrackIndex = new Dictionary<uint, TrackData>();
    }

    private class SequencerNoteAndTrackData {
        public SequencerNote SequencerNote;
        public GameplayData.TrackData TrackData;

        public SequencerNoteAndTrackData(GameplayData.TrackData trackBullshit, SequencerNote sequencerNote) {
            this.TrackData = trackBullshit;
            this.SequencerNote = sequencerNote;
        }
    }

    GameplayData gameplayData = new GameplayData();

    void Awake() {
        // Pre-spawn the dynamic objects
        treadmillNotePool = new TreadmillNotePool(TargetBar.gameObject, NoteSprite);
        metronomeClickPool = new MetronomeClickPool(gameObject, MetronomeBeatClip);
        CreateFrets();
        CreateFeedbackItems();

        musicGameObject = new GameObject("MusicGameObject");
        musicAudioSource = musicGameObject.AddComponent<AudioSource>();

        lineTapsActive = new LineTapData[NoteLines.Length];
        for (int i = 0; i < NoteLines.Length; ++ i) {
            lineTapsActive[i] = new LineTapData();
        }
    }

    public void ToggleAutoMute() {
        autoMute = !autoMute;
    }

    public void SetSequencerTrackData(Dictionary<uint, GameplayController.SequencerTrackData> sequencerTrackData, uint ticksPerQuarterNote) {
        Dictionary<uint, Dictionary<uint, List<SequencerNote>>>
            allNotesAtAbsoluteTime = new Dictionary<uint, Dictionary<uint, List<SequencerNote>>>();

        foreach (var trackData in sequencerTrackData) {
            var gameplayTrackData = new GameplayData.TrackData(trackData.Value);

            foreach (var note in trackData.Value.NoteList) {
                // Note-offs
                if (note.NoteOn != null) {
                    // Add to sequencer playlist
                    gameplayTrackData.SequencerNotes.Add(new
                        SequencerNote(note, gameplayTrackData.OriginalSequencer));
                }
                // Note-ons
                else {
                    // Add to sequencer playlist
                    SequencerNote sequencerNote = new SequencerNote(note,
                        gameplayTrackData.OriginalSequencer);
                    gameplayTrackData.SequencerNotes.Add(sequencerNote);

                    // Record all notes on all tracks for each unique beat time stamp
                    if (!allNotesAtAbsoluteTime.ContainsKey((uint)sequencerNote.QuarterNoteTime)) {
                        allNotesAtAbsoluteTime.Add((uint)sequencerNote.QuarterNoteTime,
                            new Dictionary<uint, List<SequencerNote>>());
                    }
                    if (!allNotesAtAbsoluteTime[(uint)sequencerNote.QuarterNoteTime].ContainsKey(trackData.Key)) {
                        allNotesAtAbsoluteTime[(uint)sequencerNote.QuarterNoteTime].
                            Add(trackData.Key, new List<SequencerNote>());
                    }
                    allNotesAtAbsoluteTime[(uint)sequencerNote.QuarterNoteTime][trackData.Key].Add(sequencerNote);
                }
            }

            gameplayData.TrackDataByTrackIndex.Add(trackData.Key, gameplayTrackData);
        }

        // Generate visual notes based on difficulty setting
        uint quarterNotesToSkip = GameplayOptions.GetQuarterNotesToSkip();
        uint lastGameplayNoteBeat = 0;

        var sortedBeatTimes = new List<uint>(allNotesAtAbsoluteTime.Keys);
        sortedBeatTimes.Sort();

        foreach (var beatTime in sortedBeatTimes) {
            // Spacing between gameplay beats
            if (lastGameplayNoteBeat != 0 && (beatTime - lastGameplayNoteBeat) < quarterNotesToSkip) {
                continue;
            }

            lastGameplayNoteBeat = beatTime;

            // Pull all the notes from all the tracks on this beat into a list
            List<SequencerNoteAndTrackData> notesForBeat = new List<SequencerNoteAndTrackData>();
            foreach (var trackNotes in allNotesAtAbsoluteTime[beatTime]) {
                foreach (var trackNote in trackNotes.Value) {
                    notesForBeat.Add(new SequencerNoteAndTrackData(gameplayData.
                        TrackDataByTrackIndex[trackNotes.Key], trackNote));
                }
            }

            // Sort so highest-priority instruments are first in the list
            notesForBeat.Sort(delegate (SequencerNoteAndTrackData n1, SequencerNoteAndTrackData n2) {
                if (n1.TrackData.InstrumentPriority < n2.TrackData.InstrumentPriority) {
                    return -1;
                }
                if (n2.TrackData.InstrumentPriority < n1.TrackData.InstrumentPriority) {
                    return 1;
                }
                return 0;
            });

            // Ideally this would be designed by hand ... for now we just use a simple mapping to
            // assign notes to lines
            List<SequencerNoteAndTrackData>[] noteDataByLine = new List<SequencerNoteAndTrackData>[NoteLines.Length];
            for (int i = 0; i < NoteLines.Length; ++i) {
                noteDataByLine[i] = new List<SequencerNoteAndTrackData>();
            }
            foreach (var noteForBeat in notesForBeat) {
                noteDataByLine[noteForBeat.SequencerNote.NoteKey % NoteLines.Length].Add(noteForBeat);
            }

            // Ideally this would be designed by hand ... for now we just pick the first N
            uint numNotes = 0;
            uint maxNotes = GameplayOptions.GetMaxNotesPerBeat();
            for (int i = 0; i < NoteLines.Length && numNotes < maxNotes; ++i) {
                if (noteDataByLine[i].Count > 0) {
                    ++numNotes;

                    // This note takes this line
                    var noteData = noteDataByLine[i][0];

                    // Switch it to the gameplay sequencer
                    noteData.SequencerNote.Sequencer = noteData.TrackData.GameplaySequencer;
                    gameplayData.GameplayNotes.Add(new GameplayData.
                        GameplayNote(noteData.SequencerNote, (uint)i));
                }
            }
        }
    }

    public void SetMusic(AudioClip newAudioClip, uint musicDelayMs, uint musicSkipMs, uint musicMeasuresToSkip, uint musicBPM) {
        musicPlaying = false;

        this.musicDelayMs = musicDelayMs;
        this.musicSkipMs = musicSkipMs;
        this.musicMeasuresToSkip = musicMeasuresToSkip;
        this.musicBPM = musicBPM;

        musicAudioSource.Stop();

        musicAudioSource.clip = newAudioClip;
        musicAudioSource.playOnAwake = false;
    }

    public void ScheduleAudibleMetronome(Queue<float> sortedClickQueue) {
        this.sortedClickQueue = new Queue<float>(sortedClickQueue);
    }

    public void SetMetronomeTime(float metronomeTime, float lastMetronomeTime) {
        if (TreadmillRunning) {
            UpdateFrets(metronomeTime);
        }

        UpdateAudibleMetronome(metronomeTime);
        UpdateMusicState(metronomeTime);

        if (GameplayRunning) {
            float quarterNoteTime = metronomeTime * 4.0f;

            // Spawn gameplay notes
            while (gameplayData.GameplayNoteIndex < gameplayData.GameplayNotes.Count) {
                var nextNote = gameplayData.GameplayNotes[(int)gameplayData.GameplayNoteIndex];

                // Only spawn notes to the point just off top of screen
                if ((nextNote.SequencerNote.QuarterNoteTime * 0.25f) > metronomeTime + FretCount) {
                    break;
                }

                var line = NoteLines[nextNote.LineIndex];

                //Debug.Log("Creating note on beat " + globalBeat + " mt " + metronomeTime + " gt " + gameplayStartTime);
                treadmillNotePool.GetInstance(line.transform.localPosition.x,
                    nextNote.SequencerNote.QuarterNoteTime * 0.25f,
                    TreadmillDistanceBetweenDownbeats,
                    nextNote.LineIndex,
                    line.GetComponent<SpriteRenderer>(),
                    nextNote.SequencerNote);

                ++gameplayData.GameplayNoteIndex;
            }

            foreach (var trackData in gameplayData.TrackDataByTrackIndex) {
                // Play sequencer notes
                while (trackData.Value.SequencerNoteIndex < trackData.Value.SequencerNotes.Count) {
                    var nextNote = trackData.Value.SequencerNotes[(int)trackData.Value.SequencerNoteIndex];
                    if (nextNote.QuarterNoteTime > quarterNoteTime) {
                        break;
                    }

                    if (nextNote.NoteOn != null) {
                        nextNote.Sequencer.NoteOff(nextNote.NoteKey);
                    }
                    else {
                        nextNote.Sequencer.GetComponent<AudioSource>().mute = false;
                        nextNote.Sequencer.NoteOn(nextNote.NoteKey, nextNote.Velocity);
                    }

                    ++trackData.Value.SequencerNoteIndex;
                }
            }

            UpdateTreadmillNotes(metronomeTime, lastMetronomeTime);
            UpdateFeedback(metronomeTime);
        }
    }

    public void StartGameplay() {
        if (!gameplayRunning) {
            gameplayRunning = true;
        }
    }

    public void StopGameplay() {
        if (GameplayRunning) {
            gameplayRunning = false;
            sortedClickQueue.Clear();
            treadmillNotePool.ReturnAll();
            StopTreadmill();
        }
    }

    public void StopMusic() {
        if (MusicPlaying) {
            musicStartMetronomeTime = -1.0f;
            musicPlaying = false;
            musicAudioSource.Stop();
        }
    }

    public void ShowTreadmill() {
        foreach (var treadmillFret in treadmillFrets) {
            treadmillFret.GetComponent<SpriteRenderer>().enabled = true;
        }
    }

    public void StartTreadmill() {
        if (!TreadmillRunning) {
            ShowTreadmill();
            treadmillRunning = true;
        }
    }

    public void StopTreadmill() {
        if (TreadmillRunning) {
            treadmillRunning = false;
        }
    }

    public void StartAudibleMetronome(float metronomeTime) {
        audibleMetronome = true;
        lastMetronomeTick = (int)(metronomeTime - 1.0f);
    }

    public void StopAudibleMetronome() {
        audibleMetronome = false;
        playingClicks.Clear();
        metronomeClickPool.ReturnAll();
    }

    public void TapLine(uint lineIndex, float timeOfTap) {
        lineTapsActive[(int)lineIndex].time = timeOfTap;
        lineTapsActive[(int)lineIndex].active = 1;
    }

    void CreateFrets() {
        treadmillGameObject = new GameObject("MovingFretContainer");
        treadmillGameObject.transform.parent = TargetBar.transform;
        treadmillGameObject.transform.localPosition = new Vector3(0.0f, -TreadmillDistanceBetweenDownbeats, 0.0f);

        var numFrets = (uint)Math.Round(TreadmillLength / TreadmillDistanceBetweenDownbeats) + 1;
        var offsetY = 0.0f;
        for (var fretIndex = 0; fretIndex < numFrets; ++fretIndex) {
            var movingFretSprite = Sprite.Instantiate(FretSprite);

            if (movingFretSprite != null) {
                var movingFretObject = new GameObject("MovingFret" + fretIndex);
                movingFretObject.transform.parent = treadmillGameObject.transform;
                movingFretObject.transform.localPosition = new Vector3(0.0f, offsetY, 4.0f);
                offsetY += TreadmillDistanceBetweenDownbeats;

                var movingFretRenderer = movingFretObject.AddComponent<SpriteRenderer>();
                movingFretRenderer.enabled = false;
                movingFretRenderer.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                movingFretRenderer.sprite = movingFretSprite;

                treadmillFrets.Add(movingFretObject);
            }
        }
    }

    public void CreateFeedbackItems() {
        // Create the success notes - one per line
        successNotes = new FeedbackSuccessNote[NoteLines.Length];
        for (int lineIndex = 0; lineIndex < NoteLines.Length; ++lineIndex) {
            var successNoteSprite = Sprite.Instantiate(NoteSprite);

            if (successNoteSprite != null) {
                var parentLine = NoteLines[lineIndex];

                var successNote = new FeedbackSuccessNote((uint)lineIndex,
                    NoteSprite,
                    TargetBar.transform,
                    new Vector3(parentLine.transform.localPosition.x, 0.0f, -3.0f),
                    parentLine.GetComponent<SpriteRenderer>().color,
                    FeedbackSuccessNote.Types.BigPopFade);

                successNotes[lineIndex] = successNote;
            }
        }
    }

    public void UpdateFrets(float metronomeTime) {
        // Barber pole simulation
        treadmillGameObject.transform.localPosition = new Vector3(0.0f,
            ((float)Math.Truncate(metronomeTime) - metronomeTime) * TreadmillDistanceBetweenDownbeats + kTargetLineOffset,
            0.0f);
    }

    void UpdateAudibleMetronome(float metronomeTime) {
        // Click WAV also has to be played early with a delay or it will always be late

        // Click events
        while (sortedClickQueue.Count > 0) {
            var nextClickTime = sortedClickQueue.Peek();
            if ((int)metronomeTime >= (int)nextClickTime - 1) {
                sortedClickQueue.Dequeue();

                // If the metronome is free-running just consume the pending event
                if (audibleMetronome != true) {
                    var newClick = metronomeClickPool.Borrow();
                    newClick.PlayDelayed((nextClickTime - (float)Math.Truncate(metronomeTime)) * SecondsPerBeat);
                    playingClicks.Add(newClick);
                }
            }
            else {
                break;
            }
        }

        // Free-running metronome
        if (audibleMetronome) {
            if ((int)metronomeTime > lastMetronomeTick) {
                /*
                if (metronomeTime > StartingCountdown.Duration) {
                    UnityEngine.Debug.Log("Beat " + ((int)(metronomeTime - StartingCountdown.Duration) + 1));
                }
                */
                lastMetronomeTick = (int)metronomeTime;
                var newClick = metronomeClickPool.Borrow();
                newClick.PlayDelayed((1.0f - (metronomeTime - (float)Math.Truncate(metronomeTime))) * SecondsPerBeat);
                playingClicks.Add(newClick);
            }
        }

        // Update playing clicks to see if they can be returned to the pool
        int clickIndex = 0;
        while (clickIndex < playingClicks.Count) {
            var currentClick = playingClicks[clickIndex];
            if (currentClick.time >= currentClick.clip.length) {
                metronomeClickPool.Return(playingClicks[clickIndex]);
                playingClicks.RemoveAt(clickIndex);
                continue;
            }
            ++clickIndex;
        }
    }

    public void SetMusicStartTime(float musicStartMetronomeTime) {
        float musicOffsetBeats = (float)(TimeSpan.FromMilliseconds(musicDelayMs).TotalSeconds) *
            (AudioHelm.AudioHelmClock.GetGlobalBpm() / 60.0f);

        this.musicStartMetronomeTime = musicStartMetronomeTime + musicOffsetBeats;
    }

    void UpdateMusicState(float metronomeTime) {
        if (musicStartMetronomeTime > metronomeTime) {
            float timeRemaining = musicStartMetronomeTime - metronomeTime;
            if (timeRemaining < 1.0f && !MusicPlaying) {
                musicPlaying = true;

                musicAudioSource.pitch = AudioHelm.AudioHelmClock.GetGlobalBpm() / (float)musicBPM;
                musicAudioSource.time = (float)TimeSpan.FromMilliseconds(musicSkipMs).
                    TotalSeconds + (musicMeasuresToSkip * 4 * SecondsPerBeat * musicAudioSource.pitch);
                musicAudioSource.PlayDelayed((musicStartMetronomeTime -
                    metronomeTime) * SecondsPerBeat);
            }
        }
        if (MusicPlaying) {
            if (musicAudioSource.time >= musicAudioSource.clip.length) {
                musicPlaying = false;

                if (OnMusicFinished != null) {
                    OnMusicFinished();
                }
            }
        }
    }

    public void UpdateFeedback(float metronomeTime) {
        // Update success notes
        foreach (var successNote in successNotes) {
            successNote.SetMetronomeTime(metronomeTime);
        }
    }

    public void UpdateTreadmillNotes(float metronomeTime, float lastMetronomeTime) {
        // Update treadmill notes
        var treadmillNotes = treadmillNotePool.ActiveArray();
        foreach (var treadmillNote in treadmillNotes) {
            float relativeTime = treadmillNote.beat - metronomeTime;

            // If this note is active and there is a tap on its line
            if (!treadmillNote.isPlayed && lineTapsActive[treadmillNote.line].active >= 0) {
                float relativeTapTime = relativeTime;// treadmillNote.beat - lineTapsActive[treadmillNote.line].time;

                // If it's currently in the tap window ...
                bool playedNote = (relativeTapTime < TapWindowSlopTimePositive && relativeTapTime > TapWindowSlopTimeNegative);

                if (playedNote) {
                    lineTapsActive[treadmillNote.line].active = -1;

                    // 'Score' is coarsely defined by linear proximity to the target time/bar ...
                    // Maybe try banded scoring or a curve? Can we also learn someone's natural
                    // tendency and bias towards that?
                    treadmillNote.isPlayed = true;
                    if (relativeTime > 0.0f) {
                        // TODO: Indicate rushing
                        treadmillNote.score = 1.0f - (relativeTime / TapWindowSlopTimePositive);
                    }
                    else {
                        // TODO: Indicate dragging
                        treadmillNote.score = 1.0f - (relativeTime / TapWindowSlopTimeNegative);
                    }
                }
            }

            // Play tap feedback and expire note only at the bar
            if (relativeTime <= 0.0f) {
                if (treadmillNote.isPlayed) {
                    successNotes[treadmillNote.line].Show(metronomeTime);

                    if (OnNotePlayed != null) {
                        OnNotePlayed(treadmillNote.score);
                    }
                    treadmillNotePool.Return(treadmillNote);
                }
                // If we're out of the tap window delete the note
                else if (relativeTime <= TapWindowSlopTimeNegative * 2.0f) {
                    treadmillNote.ownerNote.Sequencer.GetComponent<AudioSource>().mute = true;
                    treadmillNote.ownerNote.Sequencer.AllNotesOff();// NoteOff(treadmillNote.ownerNote.NoteKey);
                    if (OnNoteMissed != null) {
                        OnNoteMissed();
                    }
                    treadmillNotePool.Return(treadmillNote);
                }
                else {
                    treadmillNote.Hide();
                }
            }

            var localPosition = treadmillNote.gameObject.transform.localPosition;
            localPosition.y = relativeTime * TreadmillDistanceBetweenDownbeats + kTargetLineOffset;
            treadmillNote.gameObject.transform.localPosition = localPosition;
        }

        // Handle any line taps that weren't successful at hitting notes, and clear line taps for next frame
        for (int lineTapIndex = 0; lineTapIndex < NoteLines.Length; ++lineTapIndex) {
            if (lineTapsActive[lineTapIndex].active >= 0) {
                --lineTapsActive[lineTapIndex].active;
                if (lineTapsActive[lineTapIndex].active < 0) {
                    if (OnClamPlayed != null) {
                        OnClamPlayed();
                    }
                }
            }
        }
    }
}
