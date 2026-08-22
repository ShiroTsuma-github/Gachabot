export function enableHorizontalWheel(viewport) {
    const onWheel = event => {
        if (event.deltaY === 0 || viewport.scrollWidth <= viewport.clientWidth) {
            return;
        }

        event.preventDefault();
        viewport.scrollLeft += event.deltaY;
    };

    viewport.addEventListener("wheel", onWheel, { passive: false });
    return {
        dispose: () => viewport.removeEventListener("wheel", onWheel),
    };
}
