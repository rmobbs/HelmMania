using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SimpleObjectPool<T> where T : class {
    List<T> itemList = new List<T>();
    List<T> itemUsed = new List<T>();
    Queue<T> itemFree = new Queue<T>();

    // Function invoked to create an object, mandatory override
    protected abstract T ItemConstructor(int currentStorageSize);

    // Function invoked when an object is borrowed, optional override
    protected virtual void OnItemBorrow(T borrowItem) {

    }

    // Function invoked when an object is returned, optional override
    protected virtual void OnItemReturn(T borrowItem) {

    }

    public void FlushAndRefill(uint newPoolSize) {
        itemFree.Clear();
        itemUsed.Clear();
        itemList.Clear();
        for (uint index = 0; index < newPoolSize; ++ index) {
            itemFree.Enqueue(AllocateNew());
        }
    }

    public T AllocateNew() {
        T newObject = ItemConstructor(itemList.Count);
        if (newObject != null) {
            itemList.Add(newObject);
        }
        return newObject;
    }

    public T Borrow() {
        T borrowItem = null;
        try {
            borrowItem = itemFree.Dequeue();
        }
        catch {
            borrowItem = AllocateNew();
        }
        OnItemBorrow(borrowItem);
        itemUsed.Add(borrowItem);
        return borrowItem;
    }

    public void Return(T borrowItem) {
        OnItemReturn(borrowItem);
        itemUsed.Remove(borrowItem);
        itemFree.Enqueue(borrowItem);
    }

    public void ReturnAll() {
        foreach (var usedObject in itemUsed) {
            OnItemReturn(usedObject);
            itemFree.Enqueue(usedObject);
        }
        itemUsed.Clear();
    }

    public T[] ActiveArray() {
        return itemUsed.ToArray();
    }
}

