using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FeedbackSuccessNote {
    public enum Types {
        PopFade,
        Freakout,
        BigPopFade,
    }

    public GameObject GameObject;
    public SpriteRenderer SpriteRenderer;
    public float metronomeTimeDuration = 0.5f;
    private float metronomeTimeStarted;
    private Types type = Types.PopFade;

    public FeedbackSuccessNote(uint lineIndex, Sprite noteSprite, Transform parentTransform, Vector3 localPosition, Color color, Types type) {
        var sprite = Sprite.Instantiate(noteSprite);

        GameObject = new GameObject(this.GetType().Name + lineIndex);
        GameObject.transform.parent = parentTransform;
        GameObject.transform.localPosition = localPosition;

        SpriteRenderer = GameObject.AddComponent<SpriteRenderer>();

        SpriteRenderer.enabled = false;
        SpriteRenderer.sprite = sprite;
        SpriteRenderer.color = Utilities.ColorWithAlpha(color, 1.0f);

        this.type = type;
    }

    public void SetMetronomeTime(float metronomeTime) {
        if (SpriteRenderer.enabled) {
            var optimistPercent = (metronomeTime - metronomeTimeStarted) / metronomeTimeDuration;

            if (optimistPercent >= 1.0f) {
                Hide();
            }
            else {
                var pessimistPercent = 1.0f - optimistPercent;

                // Alpha
                if (type == Types.PopFade || type == Types.Freakout) {
                    SpriteRenderer.color = Utilities.ColorWithAlpha(SpriteRenderer.color, SpriteRenderer.color.a * pessimistPercent);
                }

                // Other stuff
                switch (type) {
                    case Types.Freakout: {
                            // Blew my mind
                            var newScale = GameObject.transform.localScale * optimistPercent;
                            if (newScale.x < 1.0f) {
                                newScale = new Vector3(1.0f, 1.0f, 1.0f);
                            }
                            GameObject.transform.localScale = newScale;
                            break;
                        }
                    case Types.BigPopFade: {
                            var newScale = GameObject.transform.localScale * pessimistPercent;
                            if (newScale.x < 1.0f) {
                                newScale = new Vector3(1.0f, 1.0f, 1.0f);
                            }
                            GameObject.transform.localScale = newScale;
                            break;
                        }
                }
            }
        }
    }

    public void Show(float metronomeTime) {
        SpriteRenderer.enabled = true;
        SpriteRenderer.color = Utilities.ColorWithAlpha(SpriteRenderer.color, 1.0f);
        switch (type) {
            case Types.BigPopFade: {
                    GameObject.transform.localScale = new Vector3(1.2f, 1.5f, 1.0f);
                    break;
                }
        }
        metronomeTimeStarted = metronomeTime;
    }

    public void Hide() {
        SpriteRenderer.color = Utilities.ColorWithAlpha(SpriteRenderer.color, 0.0f);
        SpriteRenderer.enabled = false;
    }
}
