using System.Collections;
using System.Collections.Generic;
using UnityEngine;

class MetronomeClickPool : SimpleObjectPool<AudioSource> {
    private GameObject ownerObject;
    private AudioClip audioClip;
    private const int kPoolSize = 4;

    protected override AudioSource ItemConstructor(int currentStorageSize) {
        GameObject gameObject = new GameObject(this.GetType().Name + currentStorageSize);
        if (gameObject) {
            gameObject.transform.parent = ownerObject.transform;
            var audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = audioClip;
            return audioSource;
        }
        return null;
    }

    protected override void OnItemReturn(AudioSource audioSource) {
        audioSource.Stop();
    }

    public MetronomeClickPool(GameObject parentObject, AudioClip audioClip) {
        this.ownerObject = parentObject;
        this.audioClip = audioClip;
        base.FlushAndRefill(kPoolSize);
    }
}

