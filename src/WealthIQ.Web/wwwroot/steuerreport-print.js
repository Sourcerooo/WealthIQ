// Paged.js driver for the printable tax report (/steuerreport/print).
// Loaded as an ES module from that page only, so the polyfill never touches the
// rest of the app.

let pagedReady = null;

function loadPaged() {
    if (pagedReady) return pagedReady;
    // MUST be set before the polyfill executes: its default is to paginate
    // document.body on DOMContentLoaded, which would shred Blazor's DOM.
    window.PagedConfig = { auto: false };
    pagedReady = new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = '/lib/pagedjs/paged.polyfill.js';
        s.onload = () => resolve();
        s.onerror = () => reject(new Error('Paged.js konnte nicht geladen werden.'));
        document.head.appendChild(s);
    });
    return pagedReady;
}

// Blazor renders the document into #sourceId (hidden). Paged.js is handed a CLONE
// and builds pages inside #targetId, which Blazor never re-renders — so the two
// renderers never fight over the same nodes.
export async function paginate(sourceId, targetId, cssHref) {
    await loadPaged();
    const source = document.getElementById(sourceId);
    const target = document.getElementById(targetId);
    if (!source || !target) return false;

    target.innerHTML = '';
    const content = source.cloneNode(true);
    content.removeAttribute('id');
    content.style.display = '';

    const previewer = new window.Paged.Previewer();
    await previewer.preview(content, [cssHref], target);
    return true;
}

export function print() {
    window.print();
}
