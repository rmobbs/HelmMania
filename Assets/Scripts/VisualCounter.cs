using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisualCounter : MonoBehaviour {
    public delegate void OnStateChangeHandler(State newState, float metronomeTime);
    public event OnStateChangeHandler OnStateChange;
    public delegate void OnCompleteHandler(float metronomeTime);
    public event OnCompleteHandler OnComplete;

    [System.Serializable]
    public class CounterState {
        public State State;
        public float MetronomeDuration;
        public bool Click = false;
        public GameObject ShowObject;

        private float metronomeTimeAtStart = 0.0f;

        public void Init() {
            if (ShowObject) {
                ShowObject.SetActive(false);
            }
        }
        public void Present(float metronomeTime) {
            if (ShowObject != null) {
                ShowObject.SetActive(true);
            }
            metronomeTimeAtStart = metronomeTime;
        }
        public void Hide() {
            if (ShowObject != null) {
                ShowObject.SetActive(false);
            }
        }
        public bool StateComplete(float metronomeTime) {
            if ((metronomeTime - metronomeTimeAtStart) >= MetronomeDuration) {
                return true;
            }
            return false;
        }
    }
    public CounterState[] StateSequence;

    public enum State {
        Preparing,
        GetReady,
        Count12,
        Count34,
        Count1,
        Count2,
        Count3,
        Count4,
        Finished,
    }
    private int currentStateIndex = -1;

    private Queue<float> sortedClickQueue = new Queue<float>();
    public Queue<float> SortedClickQueue {
        get {
            return sortedClickQueue;
        }
    }

    private float metronomeDuration = 0.0f;
    public float Duration {
        get {
            return metronomeDuration;
        }
    }

    void NextState(float metronomeTime) {
        if (currentStateIndex < StateSequence.Length) {
            if (currentStateIndex != -1) {
                StateSequence[currentStateIndex].Hide();
            }

            ++currentStateIndex;

            if (currentStateIndex < StateSequence.Length) {
                var currentState = StateSequence[currentStateIndex];
                currentState.Present(metronomeTime);
                if (OnStateChange != null) {
                    OnStateChange(currentState.State, metronomeTime);
                }
            }
            else {
                if (OnComplete != null) {
                    OnComplete(metronomeTime);
                }
            }
        }
    }

    public void StartCountdown(float metronomeTime) {
        metronomeDuration = 0.0f;
        foreach (var state in StateSequence) {
            if (state.Click) {
                sortedClickQueue.Enqueue(metronomeDuration);
            }
            metronomeDuration += state.MetronomeDuration;
            state.Init();
        }
        NextState(metronomeTime);
    }

    public void SetMetronomeTime(float metronomeTime) {
        if (currentStateIndex != -1 && currentStateIndex < StateSequence.Length) {
            var currentState = StateSequence[currentStateIndex];
            if (currentState.StateComplete(metronomeTime)) {
                NextState(metronomeTime);
            }
        }
    }
}
