// Sortable tables: <table data-sortable> makes every <th> (except data-nosort) click-to-sort.
// Cells may carry data-sort="value" for a machine-friendly key; otherwise text is used
// (numeric text, including currency/percent/thousand separators, sorts numerically).
(function () {
    const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

    function key(cell) {
        if (!cell) return '';
        const explicit = cell.getAttribute('data-sort');
        const raw = explicit !== null ? explicit : cell.textContent;
        return raw.trim();
    }

    // Numbers rendered with any currency symbol / percent / thousands separators; cells that also
    // contain words (e.g. "480 h") are treated as text unless they carry data-sort.
    function numeric(value) {
        if (/[A-Za-z]/.test(value)) return null;
        const n = value.replace(/[^\d.\-]/g, '');
        return n !== '' && /^-?\d+(\.\d+)?$/.test(n) ? parseFloat(n) : null;
    }

    function compare(a, b) {
        const na = numeric(a), nb = numeric(b);
        if (na !== null && nb !== null) return na - nb;
        if (a === '' && b !== '') return 1;
        if (b === '' && a !== '') return -1;
        return collator.compare(a, b);
    }

    function sort(table, index, direction) {
        const body = table.tBodies[0];
        if (!body) return;
        const rows = Array.from(body.rows).filter(r => !r.hasAttribute('data-nosort'));
        const fixed = Array.from(body.rows).filter(r => r.hasAttribute('data-nosort'));
        rows.sort((r1, r2) => direction * compare(key(r1.cells[index]), key(r2.cells[index])));
        rows.concat(fixed).forEach(r => body.appendChild(r));
        table.querySelectorAll('thead th').forEach((th, i) => {
            th.removeAttribute('aria-sort');
            th.classList.remove('sorted-asc', 'sorted-desc');
            if (i === index) {
                th.setAttribute('aria-sort', direction > 0 ? 'ascending' : 'descending');
                th.classList.add(direction > 0 ? 'sorted-asc' : 'sorted-desc');
            }
        });
    }

    const sortKey = table => 'is.sort:' + location.pathname + '#' + (table.id || Array.from(document.querySelectorAll('table[data-sortable]')).indexOf(table));
    function remember(table, index, direction) {
        try { localStorage.setItem(sortKey(table), index + ':' + direction); } catch { /* storage unavailable */ }
    }
    function restore(table) {
        try {
            const saved = localStorage.getItem(sortKey(table));
            if (!saved) return;
            const [index, direction] = saved.split(':').map(Number);
            const th = table.tHead && table.tHead.rows[0].cells[index];
            if (th && th.classList.contains('sortable')) sort(table, index, direction);
        } catch { /* storage unavailable */ }
    }

    document.querySelectorAll('table[data-sortable]').forEach(table => {
        const head = table.tHead;
        if (!head) return;
        Array.from(head.rows[0].cells).forEach((th, index) => {
            if (th.hasAttribute('data-nosort') || th.textContent.trim() === '') return;
            th.classList.add('sortable');
            th.tabIndex = 0;
            th.setAttribute('role', 'button');
            const sortHint = 'Sort by ' + th.textContent.trim();
            th.title = th.title ? th.title + ' — ' + sortHint : sortHint;
            const toggle = () => {
                const direction = th.classList.contains('sorted-asc') ? -1 : 1;
                sort(table, index, direction);
                remember(table, index, direction);
            };
            th.addEventListener('click', e => { if (!e.target.closest('input,button,a,select')) toggle(); });
            th.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); } });
        });
        restore(table);
    });
})();

// Remembered filters: a GET <form data-remember-filters> stores its last submitted query string per
// page; opening the page with no query string re-applies it. [data-filter-clear] links forget it.
(function () {
    const key = 'is.filters:' + location.pathname;
    const form = document.querySelector('form[method="get"][data-remember-filters]');
    if (!form) return;
    let saved = null;
    try { saved = localStorage.getItem(key); } catch { return; }

    form.addEventListener('submit', () => {
        const query = new URLSearchParams(new FormData(form));
        for (const [name, value] of Array.from(query.entries())) { if (value === '') query.delete(name); }
        const text = query.toString();
        try { text ? localStorage.setItem(key, text) : localStorage.removeItem(key); } catch { /* storage unavailable */ }
    });
    document.querySelectorAll('[data-filter-clear]').forEach(a => a.addEventListener('click', () => {
        try { localStorage.removeItem(key); } catch { /* storage unavailable */ }
    }));

    if (saved && !location.search && !document.referrer.startsWith(location.origin + location.pathname)) {
        location.replace(location.pathname + '?' + saved);
    }
})();

// Keyboard: "/" focuses the global search box (unless typing in a field).
document.addEventListener('keydown', e => {
    if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
    const t = e.target;
    if (t && (t.matches('input, textarea, select, [contenteditable]') || t.isContentEditable)) return;
    const box = document.getElementById('global-search');
    if (!box) return;
    e.preventDefault();
    box.focus();
    box.select();
});

// Bulk selection: a <form data-bulk> containing (or, with data-bulk="container-id", pointing at an
// element containing) a table with one [data-bulk-all] header checkbox and per-row [data-bulk-item]
// checkboxes; the form's action bar [data-bulk-bar] is enabled only while something is selected and
// [data-bulk-count] shows the count. When the table lives outside the form, the checkboxes carry
// form="<form id>" so they still post with it and per-row forms can stay independent.
// Buttons may carry data-confirm="Delete {n} entries?" ({n} = selected count).
(function () {
    document.querySelectorAll('form[data-bulk]').forEach(form => {
        const scope = (form.dataset.bulk && document.getElementById(form.dataset.bulk)) || form;
        const all = scope.querySelector('[data-bulk-all]');
        const items = () => Array.from(scope.querySelectorAll('[data-bulk-item]'));
        const bar = form.querySelector('[data-bulk-bar]') || scope.querySelector('[data-bulk-bar]');
        const count = form.querySelector('[data-bulk-count]') || scope.querySelector('[data-bulk-count]');

        function refresh() {
            const list = items();
            const selected = list.filter(i => i.checked).length;
            if (all) {
                all.checked = list.length > 0 && selected === list.length;
                all.indeterminate = selected > 0 && selected < list.length;
            }
            if (count) count.textContent = selected === 0 ? 'None selected' : selected + ' selected';
            if (bar) bar.querySelectorAll('button,input:not([type=hidden]),select').forEach(el => { el.disabled = selected === 0; });
            list.forEach(i => i.closest('tr')?.classList.toggle('table-active', i.checked));
        }

        if (all) all.addEventListener('change', () => { items().forEach(i => { i.checked = all.checked; }); refresh(); });
        scope.addEventListener('change', e => { if (e.target.matches('[data-bulk-item]')) refresh(); });
        form.addEventListener('keydown', e => {
            if (e.key !== 'Enter' || !e.target.matches('input')) return;
            const rowButton = e.target.closest('tr')?.querySelector('button[type=submit]');
            const barButton = e.target.closest('[data-bulk-bar] .input-group')?.querySelector('button[type=submit]');
            const target = rowButton || barButton;
            if (target) { e.preventDefault(); target.click(); }
        });
        form.addEventListener('click', e => {
            const button = e.target.closest('button[data-confirm]');
            if (!button) return;
            const n = items().filter(i => i.checked).length;
            if (!confirm(button.getAttribute('data-confirm').replace('{n}', n))) e.preventDefault();
        });
        refresh();
    });
})();

// Flash toasts rendered by _Flash: show via Bootstrap so success auto-hides and errors stay until dismissed.
(function () {
    document.querySelectorAll('[data-flash]').forEach(el => {
        if (window.bootstrap?.Toast) bootstrap.Toast.getOrCreateInstance(el).show();
    });
})();

// Submit feedback: once a form is really submitting, mark its submit button busy so double-clicks
// are ignored and the user sees progress. Skipped for GET forms (filters) and forms with data-no-busy.
(function () {
    document.addEventListener('submit', e => {
        const form = e.target;
        if (e.defaultPrevented || form.method.toLowerCase() !== 'post' || form.hasAttribute('data-no-busy')) return;
        const button = e.submitter || form.querySelector('button[type=submit],input[type=submit]');
        if (!button || button.classList.contains('is-loading')) return;
        button.classList.add('is-loading');
        button.setAttribute('aria-busy', 'true');
        // Disable after the submit has dispatched so the button's name/value still posts.
        setTimeout(() => { button.disabled = true; }, 0);
    });
})();

// Tab strips with data-tab-memory: open the tab named in the URL hash (#pane-x or #x), otherwise the
// one last used for this key, and keep both in sync so form round-trips return to the same tab.
// An anchor inside a pane (e.g. #non-labor-costs) wins over memory and is scrolled to once its pane is shown.
(function () {
    if (!window.bootstrap?.Tab) return;
    const hashId = location.hash.slice(1);
    const anchor = hashId ? document.getElementById(hashId) : null;
    const anchorPane = anchor?.closest('.tab-pane');
    document.querySelectorAll('[data-tab-memory]').forEach(strip => {
        const key = 'tab:' + strip.dataset.tabMemory;
        const buttons = [...strip.querySelectorAll('[data-bs-toggle="tab"]')];
        const byPane = id => id ? buttons.find(b => b.dataset.bsTarget === '#pane-' + id.replace(/^pane-/, '')) : undefined;
        let wanted = byPane(hashId) ?? (anchorPane ? byPane(anchorPane.id) : undefined);
        if (!wanted && !anchorPane) wanted = byPane(sessionStorage.getItem(key));
        let revealingAnchor = false;
        strip.addEventListener('shown.bs.tab', e => {
            const pane = e.target.dataset.bsTarget.slice(1);
            sessionStorage.setItem(key, pane);
            if (revealingAnchor) { revealingAnchor = false; anchor.scrollIntoView(); }
            else history.replaceState(null, '', '#' + pane);
        });
        if (wanted && !wanted.classList.contains('active')) {
            revealingAnchor = !!anchorPane && buttons.includes(wanted);
            bootstrap.Tab.getOrCreateInstance(wanted).show();
        }
    });
})();
