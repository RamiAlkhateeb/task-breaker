// Drag tasks by their handle to reorder them. Blazor owns the DOM, so the dragged element is put back
// where it was and .NET re-renders the list in the new order.
window.nxtTaskSort = {
    attach(list, dotnet) {
        if (!list || list._nxtSortable || !window.Sortable) return;
        list._nxtSortable = Sortable.create(list, {
            handle: ".drag-handle",
            draggable: ".task-group",
            animation: 150,
            delay: 120,
            delayOnTouchOnly: true,
            ghostClass: "drag-ghost",
            onEnd(evt) {
                if (evt.oldIndex === evt.newIndex) return;
                const item = evt.item;
                item.remove();
                list.insertBefore(item, list.children[evt.oldIndex] || null);
                dotnet.invokeMethodAsync("OnReorder", item.dataset.taskId, evt.newIndex);
            }
        });
    }
};
