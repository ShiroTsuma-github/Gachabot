export function enableHorizontalWheel(viewport) {
    const onWheel = event => {
        if (viewport.scrollWidth <= viewport.clientWidth) {
            return;
        }

        const rawDelta = Math.abs(event.deltaX) > Math.abs(event.deltaY)
            ? event.deltaX
            : event.deltaY;
        if (rawDelta === 0) {
            return;
        }

        const multiplier = event.deltaMode === WheelEvent.DOM_DELTA_LINE
            ? 20
            : event.deltaMode === WheelEvent.DOM_DELTA_PAGE
                ? viewport.clientWidth
                : 1;
        event.preventDefault();
        viewport.scrollLeft += rawDelta * multiplier;
    };

    viewport.addEventListener("wheel", onWheel, { passive: false });
    return {
        dispose: () => viewport.removeEventListener("wheel", onWheel),
    };
}
