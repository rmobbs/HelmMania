using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameplayOptions {
    public enum FingeringDifficulty {
        Easy,
        Medium,
        Hard,
    }

    static uint[] difficultyQuarterNotesToSkip = {
        8,
        4,
        2,
    };

    public enum ChordingDifficulty {
        Easy,
        Medium,
        Hard,
        Rachmaninov,
    }

    static uint[] difficultyMaxNotesPerBeat = {
        1,
        2,
        3,
        4,
    };

    static uint fingeringDifficultyIndex = 0;
    static uint chordingDifficultyIndex = 0;
    static float timescale = 1.0f;
    static uint startingMeasure = 1;

    public static void SetFingeringDifficultyIndex(uint _difficultyIndex) {
        fingeringDifficultyIndex = _difficultyIndex;
    }
    public static uint GetFingeringDifficultyIndex() {
        return fingeringDifficultyIndex;
    }
    public static uint GetQuarterNotesToSkip() {
        return difficultyQuarterNotesToSkip[fingeringDifficultyIndex];
    }
    public static void SetChordingDifficultyIndex(uint _chordingDifficultyIndex) {
        chordingDifficultyIndex = _chordingDifficultyIndex;
    }
    public static uint GetChordingDifficultyIndex() {
        return chordingDifficultyIndex;
    }
    public static uint GetMaxNotesPerBeat() {
        return difficultyMaxNotesPerBeat[chordingDifficultyIndex];
    }
    public static void SetTimescale(float _timescale) {
        timescale = _timescale;
    }
    public static float GetTimescale() {
        return timescale;
    }
    public static uint GetStartingMeasure() {
        return startingMeasure;
    }
    public static void SetStartingMeasure(uint _startingMeasure) {
        startingMeasure = Math.Max(_startingMeasure, 1);
    }

}

public class OptionsMenuController : MonoBehaviour
{
    public Canvas Canvas;

    private Dropdown fingeringDifficulty = null;
    private Dropdown chordingDifficulty = null;
    private InputField timescaleInput = null;
    private InputField startingMeasureInput = null;

    // Start is called before the first frame update
    void Start() {
        fingeringDifficulty = Canvas.transform.Find("FingeringDifficultyDropdown").GetComponent<Dropdown>();
        fingeringDifficulty.ClearOptions();
        var fingeringDifficultyOptions = new List<string>();
        foreach (var difficultyEnum in Enum.GetValues(typeof(GameplayOptions.FingeringDifficulty))) {
            fingeringDifficultyOptions.Add(difficultyEnum.ToString());
        }
        fingeringDifficulty.AddOptions(fingeringDifficultyOptions);
        fingeringDifficulty.value = (int)GameplayOptions.GetFingeringDifficultyIndex();

        chordingDifficulty = Canvas.transform.Find("ChordingDifficultyDropdown").GetComponent<Dropdown>();
        chordingDifficulty.ClearOptions();
        var chordingDifficultyOptions = new List<string>();
        foreach (var difficultyEnum in Enum.GetValues(typeof(GameplayOptions.ChordingDifficulty))) {
            chordingDifficultyOptions.Add(difficultyEnum.ToString());
        }
        chordingDifficulty.AddOptions(chordingDifficultyOptions);
        chordingDifficulty.value = (int)GameplayOptions.GetChordingDifficultyIndex();

        timescaleInput = Canvas.transform.Find("TimescaleInputField").GetComponent<InputField>();
        timescaleInput.text = GameplayOptions.GetTimescale().ToString();

        startingMeasureInput = Canvas.transform.Find("StartingMeasureInputField").GetComponent<InputField>();
        startingMeasureInput.text = GameplayOptions.GetStartingMeasure().ToString();
    }

    // Update is called once per frame
    void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            MainMenu();
        }
    }

    public void MainMenu() {
        GameplayOptions.SetFingeringDifficultyIndex((uint)fingeringDifficulty.value);
        GameplayOptions.SetChordingDifficultyIndex((uint)chordingDifficulty.value);
        float newTimescale = (float)Convert.ToDouble(timescaleInput.text);
        if (newTimescale >= 0.25f && newTimescale <= 2.0f) {
            GameplayOptions.SetTimescale(newTimescale);
        }
        uint newStartingMeasure = (uint)Convert.ToInt32(startingMeasureInput.text);
        if (newStartingMeasure > 0) {
            GameplayOptions.SetStartingMeasure(newStartingMeasure);
        }
        SceneManager.LoadScene("MainMenu");
    }
}
