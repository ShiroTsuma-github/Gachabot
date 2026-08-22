const moveTimelineWithWheel = (event, viewport) => {
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

document.addEventListener("wheel", event => {
    const target = event.target;
    if (!(target instanceof Element)) {
        return;
    }

    const viewport = target.closest(".calendar-timeline-scroll");
    if (viewport instanceof HTMLElement) {
        moveTimelineWithWheel(event, viewport);
    }
}, { passive: false });
