// Select by date across the whole plot, including steep changes and empty space.
export function nearestPoint(points, x, y, step) {
    if (!points.length || x < 70 || x > 892 || y < 42 || y > 286
        || x > points.at(-1).x + step / 2) return null;
    let low = 0;
    let high = points.length - 1;
    while (low < high) {
        const middle = Math.floor((low + high) / 2);
        if (x <= (points[middle].x + points[middle + 1].x) / 2) high = middle;
        else low = middle + 1;
    }
    return points[low];
}

export function tooltipPosition(pointer, size, bounds) {
    const clamp = (value, max) => Math.max(8, Math.min(value, max - 8));
    return {
        x: clamp(pointer.x - size.width / 2, bounds.width - size.width),
        y: clamp(pointer.y - size.height - 16, bounds.height - size.height)
    };
}

export function attach(shell) {
    const svg = shell.querySelector('.burndown-chart');
    const tooltip = shell.querySelector('.burndown-tooltip-anchor');
    const guide = svg.querySelector('.burndown-guide');
    const points = Array.from(svg.querySelectorAll('[data-burndown-index]'), element => {
        const index = element.dataset.burndownIndex;
        const circle = element.querySelector('circle');
        const legend = shell.querySelector(`[data-burndown-legend="${index}"]`);
        return { element, legend, x: Number(circle.getAttribute('cx')), y: Number(circle.getAttribute('cy')) };
    });
    const step = Number(svg.dataset.burndownStep);
    const events = new AbortController();
    const mobile = window.matchMedia('(max-width: 760px)');
    let active = null;
    let focused = null;
    let frame = 0;
    let pendingPointer = null;
    let tooltipSize = null;

    function show(point, clientPosition) {
        if (active !== point) {
            if (active) {
                active.element.classList.remove('is-active');
                active.element.setAttribute('aria-expanded', 'false');
                active.element.removeAttribute('aria-describedby');
                active.legend.hidden = true;
            }
            active = point;
            tooltip.hidden = !point;
            guide.setAttribute('visibility', point ? 'visible' : 'hidden');
            if (point) {
                point.legend.hidden = false;
                point.element.classList.add('is-active');
                point.element.setAttribute('aria-expanded', 'true');
                point.element.setAttribute('aria-describedby', point.legend.firstElementChild.id);
                guide.setAttribute('x1', point.x);
                guide.setAttribute('x2', point.x);
            }
        }
        if (!point || mobile.matches) return;
        const bounds = shell.getBoundingClientRect();
        // Measure once; pointer movement only changes the composited translation.
        tooltipSize ??= { width: tooltip.offsetWidth, height: tooltip.offsetHeight };
        const position = tooltipPosition({ x: clientPosition.x - bounds.left, y: clientPosition.y - bounds.top }, tooltipSize, bounds);
        tooltip.style.setProperty('--tooltip-x', `${position.x}px`);
        tooltip.style.setProperty('--tooltip-y', `${position.y}px`);
    }

    function cancelPointer() {
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
        pendingPointer = null;
    }

    function showFocused() {
        if (!focused) return show(null);
        const matrix = svg.getScreenCTM();
        if (!matrix) return;
        show(focused, new DOMPoint(focused.x, focused.y).matrixTransform(matrix));
    }

    function updatePointer() {
        frame = 0;
        const pointer = pendingPointer;
        pendingPointer = null;
        const matrix = svg.getScreenCTM();
        if (!pointer || !matrix) return;
        const position = new DOMPoint(pointer.x, pointer.y).matrixTransform(matrix.inverse());
        const point = nearestPoint(points, position.x, position.y, step);
        if (point) show(point, pointer);
        else showFocused();
    }

    function queuePointer(event) {
        pendingPointer = { x: event.clientX, y: event.clientY };
        if (!frame) frame = requestAnimationFrame(updatePointer);
    }

    svg.addEventListener('pointermove', event => {
        if (event.pointerType === 'mouse' || event.pointerType === 'pen') queuePointer(event);
    }, { signal: events.signal, passive: true });
    svg.addEventListener('click', event => {
        // Native clicks work for touch without cancelling horizontal scrolling.
        const point = points.find(item => item.element.contains(event.target));
        if (point) {
            cancelPointer();
            show(point, { x: event.clientX, y: event.clientY });
        } else queuePointer(event);
    }, { signal: events.signal });
    shell.addEventListener('pointerleave', event => {
        if (event.pointerType !== 'mouse' && event.pointerType !== 'pen') return;
        cancelPointer();
        showFocused();
    }, { signal: events.signal });
    svg.addEventListener('focusin', event => {
        focused = points.find(item => item.element === event.target) ?? null;
        cancelPointer();
        showFocused();
    }, { signal: events.signal });
    svg.addEventListener('focusout', () => {
        focused = null;
        cancelPointer();
        show(null);
    }, { signal: events.signal });
    shell.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            cancelPointer();
            show(null);
        } else if (focused && (event.key === 'Enter' || event.key === ' ')) {
            event.preventDefault();
            showFocused();
        }
    }, { signal: events.signal });

    const resize = new ResizeObserver(() => {
        tooltipSize = null;
        cancelPointer();
        showFocused();
    });
    resize.observe(svg);
    shell.querySelector('.burndown-chart-scroll').addEventListener('scroll', () => {
        cancelPointer();
        showFocused();
    }, { signal: events.signal, passive: true });

    return {
        dispose() {
            events.abort();
            resize.disconnect();
            cancelPointer();
            show(null);
        }
    };
}
