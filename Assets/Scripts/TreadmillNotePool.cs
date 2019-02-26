using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TreadmillNote {
    public GameObject gameObject;
    public SpriteRenderer spriteRenderer;
    public float beat;
    public uint line;
    public bool isPlayed;
    public float score;
    public Fretboard.SequencerNote ownerNote;

    public TreadmillNote(int globalIndex, Sprite noteSprite, Transform parentTransform) {

        gameObject = new GameObject(this.GetType().Name + globalIndex);

        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = Sprite.Instantiate(noteSprite);
        spriteRenderer.enabled = false;

        gameObject.transform.parent = parentTransform;

    }

    public void Show(float beat, uint line, SpriteRenderer lineRenderer, Vector3 localPosition, Fretboard.SequencerNote ownerNote) {
        spriteRenderer.material = lineRenderer.material;
        spriteRenderer.color = Utilities.ColorWithAlpha(lineRenderer.color, 1.0f);
        spriteRenderer.enabled = true;

        this.beat = beat;
        this.line = line;
        this.isPlayed = false;
        this.score = 0.0f;
        this.ownerNote = ownerNote;

        gameObject.transform.localPosition = localPosition;
    }

    public void Hide() {
        spriteRenderer.color = Utilities.ColorWithAlpha(spriteRenderer.color, 0.0f);
        spriteRenderer.enabled = false;
    }
}

public class TreadmillNotePool : SimpleObjectPool<TreadmillNote> {
    private GameObject ownerObject;
    private Sprite noteSprite;
    private const int kPoolSize = 20;

    protected override TreadmillNote ItemConstructor(int currentStorageSize) {
        return new TreadmillNote(currentStorageSize, noteSprite, ownerObject.transform);
    }

    protected override void OnItemReturn(TreadmillNote usedObject) {
        usedObject.Hide();
    }

    public TreadmillNotePool(GameObject parentObject, Sprite noteSprite) {
        this.ownerObject = parentObject;
        this.noteSprite = noteSprite;
        base.FlushAndRefill(kPoolSize);
    }

    public TreadmillNote GetInstance(float posX, float beat, float distanceBetweenDownbeats, uint line, SpriteRenderer lineRenderer, Fretboard.SequencerNote ownerNote) {
        TreadmillNote treadmillNote = Borrow();

        treadmillNote.Show(beat, line, lineRenderer, 
            new Vector3(posX, distanceBetweenDownbeats * beat, 0.0f), ownerNote);

        return treadmillNote;
    }

}
