// Scope by origin and app base path (multiple apps may share a Pages domain).
window.timeOpsConnection = {
    key: () => "timeops.connection.v1:" + new URL(document.baseURI).pathname,
    read() { return window.localStorage.getItem(this.key()); },
    save(value) { window.localStorage.setItem(this.key(), value); },
    remove() { window.localStorage.removeItem(this.key()); }
};
