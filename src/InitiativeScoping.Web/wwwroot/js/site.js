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
        table.dispatchEvent(new CustomEvent('table:sorted'));
    }

    const sortKey = table => 'is.sort:' + location.pathname + '#' + (table.id || Array.from(document.querySelectorAll('table[data-sortable]')).indexOf(table));
    const headerId = th => th.dataset.sortId || th.textContent.trim();
    function remember(table, th, direction) {
        try { localStorage.setItem(sortKey(table), JSON.stringify({ column: headerId(th), direction })); } catch { /* storage unavailable */ }
    }
    function restore(table) {
        try {
            const saved = JSON.parse(localStorage.getItem(sortKey(table)) || 'null');
            if (!saved) return;
            const cells = Array.from(table.tHead.rows[0].cells);
            const index = cells.findIndex(th => th.classList.contains('sortable') && headerId(th) === saved.column);
            if (index >= 0) sort(table, index, saved.direction === -1 ? -1 : 1);
        } catch { /* storage unavailable or stale value */ }
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
                remember(table, th, direction);
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

    let referrer = null;
    try { referrer = document.referrer ? new URL(document.referrer) : null; } catch { /* invalid referrer */ }
    const fromSamePage = referrer !== null && referrer.origin === location.origin && referrer.pathname === location.pathname;
    if (saved && !location.search && !fromSamePage) {
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


// Column visibility: <table data-columns> gets a "Columns" menu (placed before the table's
// .table-responsive wrapper) to hide/show columns; the choice is remembered per page and table.
(function () {
    const storageKey = table => 'is.columns:' + location.pathname + '#' + (table.id || Array.from(document.querySelectorAll('table[data-columns]')).indexOf(table));
    const label = th => th.dataset.sortId || th.textContent.trim();

    document.querySelectorAll('table[data-columns]').forEach(table => {
        const head = table.tHead;
        if (!head) return;
        const headers = Array.from(head.rows[0].cells).filter(th => label(th) !== '' && !th.hasAttribute('data-nohide'));
        if (headers.length < 2) return;
        let hidden = [];
        try { hidden = JSON.parse(localStorage.getItem(storageKey(table)) || '[]'); } catch { /* storage unavailable */ }

        const apply = () => {
            Array.from(head.rows[0].cells).forEach((th, index) => {
                const hide = hidden.includes(label(th));
                th.classList.toggle('col-hidden', hide);
                Array.from(table.tBodies).concat(table.tFoot ? [table.tFoot] : []).forEach(section => {
                    Array.from(section.rows).forEach(row => { if (row.cells[index] && row.cells.length === head.rows[0].cells.length) row.cells[index].classList.toggle('col-hidden', hide); });
                });
            });
        };

        const menu = document.createElement('div');
        menu.className = 'dropdown d-inline-block column-picker';
        menu.innerHTML = '<button type="button" class="btn btn-outline-secondary btn-sm dropdown-toggle" data-bs-toggle="dropdown" aria-expanded="false">Columns</button>';
        const list = document.createElement('div');
        list.className = 'dropdown-menu dropdown-menu-end p-2';
        headers.forEach(th => {
            const id = 'col-' + Math.random().toString(36).slice(2, 8);
            const item = document.createElement('div');
            item.className = 'form-check form-check-sm mb-1';
            item.innerHTML = `<input class="form-check-input" type="checkbox" id="${id}"><label class="form-check-label small" for="${id}"></label>`;
            const box = item.querySelector('input');
            item.querySelector('label').textContent = label(th);
            box.checked = !hidden.includes(label(th));
            box.addEventListener('change', () => {
                hidden = box.checked ? hidden.filter(h => h !== label(th)) : hidden.concat(label(th));
                try { localStorage.setItem(storageKey(table), JSON.stringify(hidden)); } catch { /* storage unavailable */ }
                apply();
            });
            list.appendChild(item);
        });
        const reset = document.createElement('button');
        reset.type = 'button';
        reset.className = 'btn btn-link btn-sm p-0 mt-1';
        reset.textContent = 'Show all';
        reset.addEventListener('click', () => {
            hidden = [];
            try { localStorage.removeItem(storageKey(table)); } catch { /* storage unavailable */ }
            list.querySelectorAll('input').forEach(i => { i.checked = true; });
            apply();
        });
        list.appendChild(reset);
        menu.appendChild(list);

        const anchor = table.closest('.table-responsive') || table;
        const toolbar = document.createElement('div');
        toolbar.className = 'd-flex justify-content-end mb-2 table-toolbar';
        toolbar.appendChild(menu);
        anchor.parentNode.insertBefore(toolbar, anchor);
        apply();
    });
})();

// Client-side pagination: <table data-paginate="25"> shows that many body rows at a time with
// controls under the table; page size is remembered per page. Re-applies after sorting.
(function () {
    document.querySelectorAll('table[data-paginate]').forEach(table => {
        const body = table.tBodies[0];
        if (!body) return;
        const key = 'is.pagesize:' + location.pathname;
        const sizes = [25, 50, 100, 0];
        let size = parseInt(table.dataset.paginate, 10) || 25;
        try { size = parseInt(localStorage.getItem(key), 10) || size; } catch { /* storage unavailable */ }
        if (localStorage.getItem(key) === '0') size = 0;
        let page = 1;

        const nav = document.createElement('div');
        nav.className = 'd-flex flex-wrap align-items-center justify-content-between gap-2 small table-pager';
        const anchor = table.closest('.table-responsive') || table;
        anchor.parentNode.insertBefore(nav, anchor.nextSibling);

        const render = () => {
            const rows = Array.from(body.rows).filter(r => !r.hasAttribute('data-nosort'));
            const total = rows.length;
            const pages = size === 0 ? 1 : Math.max(1, Math.ceil(total / size));
            page = Math.min(Math.max(1, page), pages);
            rows.forEach((row, i) => { row.classList.toggle('page-hidden', size !== 0 && (i < (page - 1) * size || i >= page * size)); });
            const first = total === 0 ? 0 : (size === 0 ? 1 : (page - 1) * size + 1);
            const last = size === 0 ? total : Math.min(total, page * size);
            nav.innerHTML = '';
            const info = document.createElement('span');
            info.className = 'text-muted';
            info.textContent = `Showing ${first}–${last} of ${total}`;
            nav.appendChild(info);
            const right = document.createElement('div');
            right.className = 'd-flex align-items-center gap-2';
            const select = document.createElement('select');
            select.className = 'form-select form-select-sm w-auto';
            select.setAttribute('aria-label', 'Rows per page');
            sizes.forEach(n => {
                const o = document.createElement('option');
                o.value = n; o.textContent = n === 0 ? 'All rows' : n + ' per page'; o.selected = n === size;
                select.appendChild(o);
            });
            select.addEventListener('change', () => {
                size = parseInt(select.value, 10); page = 1;
                try { localStorage.setItem(key, String(size)); } catch { /* storage unavailable */ }
                render();
            });
            right.appendChild(select);
            if (pages > 1) {
                const ul = document.createElement('ul');
                ul.className = 'pagination pagination-sm mb-0';
                const add = (text, target, disabled, active, aria) => {
                    const li = document.createElement('li');
                    li.className = 'page-item' + (disabled ? ' disabled' : '') + (active ? ' active' : '');
                    const b = document.createElement('button');
                    b.type = 'button'; b.className = 'page-link'; b.textContent = text; b.disabled = disabled;
                    if (aria) b.setAttribute('aria-label', aria);
                    if (active) b.setAttribute('aria-current', 'page');
                    b.addEventListener('click', () => { page = target; render(); });
                    li.appendChild(b); ul.appendChild(li);
                };
                add('‹', page - 1, page === 1, false, 'Previous page');
                for (let p = 1; p <= pages; p++) {
                    if (pages > 9 && Math.abs(p - page) > 3 && p !== 1 && p !== pages) {
                        if (Math.abs(p - page) === 4) add('…', p, true, false, null);
                        continue;
                    }
                    add(String(p), p, false, p === page, null);
                }
                add('›', page + 1, page === pages, false, 'Next page');
                right.appendChild(ul);
            }
            nav.appendChild(right);
        };
        table.addEventListener('table:sorted', () => { page = 1; render(); });
        render();
    });
})();

// Keyboard shortcuts: any element with data-shortcut="g i" is activated by that key sequence;
// "?" opens the help overlay listing them. Ignored while typing in a field.
(function () {
    const targets = () => Array.from(document.querySelectorAll('[data-shortcut]'));
    if (targets().length === 0) return;
    let buffer = '';
    let timer = null;

    const typing = e => {
        const t = e.target;
        return t && (t.matches('input, textarea, select, [contenteditable]') || t.isContentEditable);
    };

    function help() {
        let modal = document.getElementById('shortcut-help');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'shortcut-help';
            modal.className = 'modal fade';
            modal.tabIndex = -1;
            modal.setAttribute('aria-labelledby', 'shortcut-help-title');
            modal.setAttribute('aria-hidden', 'true');
            const rows = targets().map(el => `<tr><td><kbd>${el.dataset.shortcut.split(' ').join('</kbd> <kbd>')}</kbd></td><td>${(el.dataset.shortcutLabel || el.textContent).trim()}</td></tr>`).join('');
            modal.innerHTML = `<div class="modal-dialog modal-dialog-centered modal-sm"><div class="modal-content">
                <div class="modal-header"><h2 class="modal-title h6" id="shortcut-help-title">Keyboard shortcuts</h2><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button></div>
                <div class="modal-body p-2"><table class="table table-sm mb-0"><tbody>
                    ${document.getElementById('global-search') ? '<tr><td><kbd>/</kbd></td><td>Search</td></tr>' : ''}
                    ${rows}
                    <tr><td><kbd>?</kbd></td><td>This help</td></tr>
                </tbody></table></div></div></div>`;
            document.body.appendChild(modal);
        }
        bootstrap.Modal.getOrCreateInstance(modal).toggle();
    }

    document.addEventListener('keydown', e => {
        if (e.ctrlKey || e.metaKey || e.altKey || typing(e)) return;
        if (e.key === '?') { e.preventDefault(); help(); return; }
        if (e.key.length !== 1) return;
        buffer = (buffer + e.key).slice(-4);
        clearTimeout(timer);
        timer = setTimeout(() => { buffer = ''; }, 1200);
        const hit = targets().find(el => el.dataset.shortcut.replace(/\s+/g, '') === buffer || el.dataset.shortcut.replace(/\s+/g, '') === buffer.slice(-1));
        if (!hit) return;
        buffer = '';
        e.preventDefault();
        if (hit.matches('a[href]')) location.assign(hit.href); else hit.click();
    });
})();
