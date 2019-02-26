using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Sanford.Multimedia.Midi;

public class GameplayController : MonoBehaviour {
    [System.Serializable]
    public class SequencerTrack : System.Object {
        public uint TrackIndex;
        public uint Priority;
        public AudioHelm.Sequencer Sequencer;
    }

    public string MidiFile;
    public uint MidiStartTick;
    public AudioClip MusicAudioClip;
    public uint MusicBPM;
    public SequencerTrack[] SequencerTracks;
    public VisualCounter StartingCountdown;
    public Fretboard Fretboard;
    public Canvas Canvas;

    public class Note {
        public byte NoteKey = 0;
        public Note NoteOn = null;
        public float QuarterNoteTime = 0.0f;
        public float Velocity = 0.0f;
        public uint AbsoluteTicks = 0;

        public Note() { }

        public Note(Note note) {
            this.NoteKey = note.NoteKey;
            this.NoteOn = note.NoteOn;
            this.QuarterNoteTime = note.QuarterNoteTime;
            this.Velocity = note.Velocity;
            this.AbsoluteTicks = note.AbsoluteTicks;
        }
    }

    public class SequencerTrackData {
        public AudioHelm.Sequencer Sequencer;
        public uint Priority;
        public List<Note> NoteList;
    }

    private int beatStreak = 0;
    private int longestBeatStreak = 0;
    private int kMinBeatsForBeatStreak = 4;
    private float lastMeasureTime = 0.0f;

    void Start() {
        AudioHelm.AudioHelmClock.GetInstance().bpm = MusicBPM * GameplayOptions.GetTimescale();

        StartingCountdown.OnStateChange += OnCountdownStateChange;
        StartingCountdown.StartCountdown(0.0f);

        Fretboard.ScheduleAudibleMetronome(StartingCountdown.SortedClickQueue);

        Fretboard.OnNoteMissed += OnNoteMissed;
        Fretboard.OnNotePlayed += OnNotePlayed;
        Fretboard.OnClamPlayed += OnClamPlayed;
        Fretboard.OnMusicFinished += OnMusicFinished;

        var midiFileBinaryAsset = Resources.Load("Midi/" + MidiFile) as TextAsset;
        if (midiFileBinaryAsset != null) {
            Sequence midiSequence = new Sequence(new MemoryStream(midiFileBinaryAsset.bytes));

            List<Track> trackList = new List<Track>();

            foreach (var midiTrack in midiSequence) {
                trackList.Add(midiTrack);
            }

            Dictionary<uint, SequencerTrackData> sequencerTrackData = new Dictionary<uint, SequencerTrackData>();
            // Map out the track/sequencer pairing
            foreach (var sequencerTrack in SequencerTracks) {
                SequencerTrackData trackData = new SequencerTrackData();
                trackData.Sequencer = sequencerTrack.Sequencer;
                trackData.Priority = sequencerTrack.Priority;
                trackData.NoteList = new List<Note>();
                sequencerTrackData.Add(sequencerTrack.TrackIndex, trackData);
            }

            // Load notes for each MIDI track that is bound to a sequencer
            uint trackIndex = 0;
            foreach (var track in midiSequence) {
                if (sequencerTrackData.ContainsKey(trackIndex)) {
                    var trackData = sequencerTrackData[trackIndex];
                    trackData.Sequencer.Clear();
                    trackData.Sequencer.length = 4 *
                        midiSequence.GetLength() / midiSequence.Division;
                    PrepareTrack(track, trackData, midiSequence.Division);
                }
                ++trackIndex;

            }

            Fretboard.SetSequencerTrackData(sequencerTrackData, (uint)midiSequence.Division);
            Fretboard.SetMusicStartTime(StartingCountdown.Duration);
            Fretboard.SetMusic(MusicAudioClip, 0, 0, GameplayOptions.GetStartingMeasure() - 1, MusicBPM);

            Fretboard.StartGameplay();
        }
    }

    void PrepareTrack(Track trackToPrepare, SequencerTrackData trackData, int sequenceDivision) {
        Dictionary<byte, Note> noteOns = new Dictionary<byte, Note>();

        int lastAbsoluteTicks = 0;
        for (int i = 0; i < trackToPrepare.Count; ++i) {
            var midiEvent = trackToPrepare.GetMidiEvent(i);
            var midiBytes = midiEvent.MidiMessage.GetBytes();

            if (midiBytes.Length < 3) {
                continue;
            }

            var eventType = (midiBytes[0] & 0xF0);
            var noteKey = midiBytes[1];
            var noteVel = midiBytes[2];

            int eventAbsoluteTicks = midiEvent.AbsoluteTicks - ((int)(GameplayOptions.
                GetStartingMeasure() - 1) * sequenceDivision * 4) - (int)MidiStartTick;
            if (eventAbsoluteTicks < 0) {
                continue;
            }

            // sequenceDivision is 'ticks per quarter note'
            // midiEvent.AbsoluteTicks is number of ticks expired from beginning of song to this note
            // so we're expressing 'Time' in terms of amount of quarter notes that have passed
            var quarterNoteTime = ((4.0f * (float)eventAbsoluteTicks) /
                (float)sequenceDivision) + (StartingCountdown.Duration * 4.0f);

            if (eventType == (byte)ChannelCommand.NoteOff) {
                if (noteOns.ContainsKey(noteKey)) {
                    Note newNote = new Note();

                    newNote.NoteKey = noteKey;
                    newNote.NoteOn = noteOns[noteKey];
                    newNote.QuarterNoteTime = quarterNoteTime;
                    newNote.AbsoluteTicks = (uint)eventAbsoluteTicks;
                    trackData.NoteList.Add(newNote);

                    noteOns.Remove(noteKey);
                }
            }
            else if (eventType == (byte)ChannelCommand.NoteOn) {
                // Some files have weird tiny distances between note-ons ... flatten or discard
                int deltaTicks = eventAbsoluteTicks - lastAbsoluteTicks;
                if (deltaTicks > 0 && deltaTicks < (sequenceDivision / 4)) {
                    continue;
                }

                lastAbsoluteTicks = eventAbsoluteTicks;

                Note newNote = new Note();

                newNote.NoteKey = noteKey;
                newNote.NoteOn = null;
                newNote.QuarterNoteTime = quarterNoteTime;
                newNote.AbsoluteTicks = (uint)eventAbsoluteTicks;
                newNote.Velocity = Mathf.Min(1.0f, noteVel / 127.0f);

                trackData.NoteList.Add(newNote);

                noteOns[noteKey] = newNote;
            }
        }
    }

    private void OnApplicationPause(bool pause) {
    }

    void OnCountdownStateChange(VisualCounter.State newState, float metronomeTime) {
        if (newState == VisualCounter.State.GetReady) {
            Fretboard.ShowTreadmill();
        }
        else if (newState == VisualCounter.State.Count12) {
            Fretboard.StartTreadmill();

            StartingCountdown.OnStateChange -= OnCountdownStateChange;
        }
    }

    void UpdateInput(float metronomeTime) {
        if (Input.GetKeyDown(KeyCode.A)) {
            Fretboard.TapLine(0, metronomeTime);
        }
        if (Input.GetKeyDown(KeyCode.S)) {
            Fretboard.TapLine(1, metronomeTime);
        }
        if (Input.GetKeyDown(KeyCode.D)) {
            Fretboard.TapLine(2, metronomeTime);
        }
        if (Input.GetKeyDown(KeyCode.F)) {
            Fretboard.TapLine(3, metronomeTime);
        }
        if (Input.GetKeyDown(KeyCode.Space)) {
            if (Fretboard.AudibleMetronome) {
                Fretboard.StopAudibleMetronome();
            }
            else {
                Fretboard.StartAudibleMetronome(metronomeTime);
            }
        }
        if (Input.GetKeyDown(KeyCode.Return)) {
            Fretboard.ToggleAutoMute();
        }
        if (Input.GetKeyDown(KeyCode.Escape)) {
            ToMainMenu();
        }
    }

    public void ToMainMenu() {
        SceneManager.LoadScene("MainMenu");
    }

    void FixedUpdate() {
        float measureTime = (float)AudioHelm.AudioHelmClock.GetGlobalBeatTime();
        if (measureTime < 0.0f)
            return;

        UpdateInput(measureTime);

        StartingCountdown.SetMetronomeTime(measureTime);
        Fretboard.SetMetronomeTime(measureTime, lastMeasureTime);

        if (beatStreak < kMinBeatsForBeatStreak) {
            var streakDisplay = Canvas.transform.Find("StreakDisplay").GetComponent<Text>();

            var color = streakDisplay.color;
            color.a -= Time.deltaTime;
            streakDisplay.color = color;
            if (color.a <= 0.0f) {
                streakDisplay.enabled = false;
            }
        }
        lastMeasureTime = measureTime;
    }

    void OnNotePlayed(float score) {
        ++ beatStreak;
        if (beatStreak >= kMinBeatsForBeatStreak) {
            if (longestBeatStreak < beatStreak) {
                longestBeatStreak = beatStreak;
            }
            var streakDisplay = Canvas.transform.Find("StreakDisplay").GetComponent<Text>();

            streakDisplay.enabled = true;
            var color = streakDisplay.color;
            color.a = 1.0f;
            streakDisplay.color = color;
            streakDisplay.text = beatStreak + " note streak!";
        }
    }

    void OnNoteMissed() {
        beatStreak = 0;
    }

    void OnClamPlayed() {
        beatStreak = 0;
        
        // TODO: Lock out input for a time
        // TODO: Play Family Feud X sound
    }

    void OnMusicFinished() {
        var longestStreakText = Canvas.transform.
            Find("Results").Find("LongestStreakText").GetComponent<Text>();
        longestStreakText.enabled = true;
        longestStreakText.text = "Longest streak: " + longestBeatStreak;
    }
}