const bindings = new WeakMap();

export function attach(root) {
    const navigation = root.querySelector('.tab-navigation');
    const strip = navigation.querySelector('[role="tablist"]');
    const previous = navigation.querySelector('.tab-previous');
    const next = navigation.querySelector('.tab-next');
    function reveal(tab) {
        if (!tab) return;
        const bounds = strip.getBoundingClientRect();
        const item = tab.getBoundingClientRect();
        if (item.left < bounds.left) strip.scrollLeft -= bounds.left - item.left;
        else if (item.right > bounds.right) strip.scrollLeft += item.right - bounds.right;
    }
    function updateButtons() {
        previous.disabled = strip.scrollLeft <= 1;
        next.disabled = strip.scrollLeft + strip.clientWidth >= strip.scrollWidth - 1;
    }
    function update() {
        const overflow = strip.scrollWidth > navigation.clientWidth + 1;
        navigation.classList.toggle('has-overflow', overflow);
        previous.hidden = next.hidden = !overflow;
        reveal(strip.querySelector('[aria-selected="true"]'));
        updateButtons();
    }
    const keydown = event => {
        const tab = event.target.closest('[role="tab"]');
        if (!tab || tab.closest('.section-tabs') !== root) return;
        const tabs = [...strip.querySelectorAll('[role="tab"]')];
        const index = tabs.indexOf(tab);
        let target;
        switch (event.key) {
            case 'ArrowRight': target = (index + 1) % tabs.length; break;
            case 'ArrowLeft': target = (index + tabs.length - 1) % tabs.length; break;
            case 'Home': target = 0; break;
            case 'End': target = tabs.length - 1; break;
            default: return;
        }
        event.preventDefault();
        tabs[target].focus({ preventScroll: true });
        tabs[target].click();
        reveal(tabs[target]);
        updateButtons();
    };
    const scrollPrevious = () => strip.scrollBy({ left: -strip.clientWidth * .75 });
    const scrollNext = () => strip.scrollBy({ left: strip.clientWidth * .75 });
    const resize = new ResizeObserver(update);
    const mutation = new MutationObserver(update);
    resize.observe(navigation);
    mutation.observe(strip, { attributes: true, attributeFilter: ['aria-selected'], childList: true, subtree: true });
    root.addEventListener('keydown', keydown);
    strip.addEventListener('scroll', updateButtons);
    previous.addEventListener('click', scrollPrevious);
    next.addEventListener('click', scrollNext);
    bindings.set(root, { resize, mutation, keydown, strip, updateButtons, previous, next, scrollPrevious, scrollNext });
    update();
}

export function detach(root) {
    const binding = bindings.get(root);
    if (!binding) return;
    binding.resize.disconnect();
    binding.mutation.disconnect();
    root.removeEventListener('keydown', binding.keydown);
    binding.strip.removeEventListener('scroll', binding.updateButtons);
    binding.previous.removeEventListener('click', binding.scrollPrevious);
    binding.next.removeEventListener('click', binding.scrollNext);
    bindings.delete(root);
}
