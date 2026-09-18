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
        try {
            const saved = parseInt(localStorage.getItem(key), 10);
            if (sizes.includes(saved)) size = saved;
        } catch { /* storage unavailable */ }
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
            modal.innerHTML = `<div class="modal-dialog modal-dialog-centered modal-sm"><div class="modal-content">
                <div class="modal-header"><h2 class="modal-title h6" id="shortcut-help-title">Keyboard shortcuts</h2><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button></div>
                <div class="modal-body p-2"><table class="table table-sm mb-0"><tbody></tbody></table></div></div></div>`;
            const tbody = modal.querySelector('tbody');
            const row = (keys, text) => {
                const tr = document.createElement('tr');
                const keyCell = document.createElement('td');
                keys.forEach((k, i) => {
                    const kbd = document.createElement('kbd');
                    kbd.textContent = k;
                    if (i > 0) keyCell.appendChild(document.createTextNode(' '));
                    keyCell.appendChild(kbd);
                });
                const textCell = document.createElement('td');
                textCell.textContent = text;
                tr.append(keyCell, textCell);
                tbody.appendChild(tr);
            };
            if (document.getElementById('global-search')) row(['/'], 'Search');
            targets().forEach(el => row(el.dataset.shortcut.split(' '), (el.dataset.shortcutLabel || el.textContent).trim()));
            row(['?'], 'This help');
            document.body.appendChild(modal);
        }
        bootstrap.Modal.getOrCreateInstance(modal).toggle();
    }

    document.querySelectorAll('[data-shortcut-help]').forEach(el => el.addEventListener('click', e => { e.preventDefault(); help(); }));

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
        if (hit.matches('a[href]')) { window.showPageLoading?.(hit.href); location.assign(hit.href); } else hit.click();
    });
})();

// Appearance: light / dark / system. The <head> applies the stored choice before first paint; this keeps the
// dropdown in sync, persists changes and follows the OS when "System" is selected.
(function () {
    const key = 'is-theme';
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const stored = () => { try { return localStorage.getItem(key); } catch { return null; } };
    const choice = () => { const s = stored(); return s === 'light' || s === 'dark' ? s : 'auto'; };
    const apply = () => {
        const c = choice();
        document.documentElement.setAttribute('data-bs-theme', c === 'auto' ? (media.matches ? 'dark' : 'light') : c);
        document.querySelectorAll('.theme-toggle [data-theme]').forEach(b => {
            const on = b.dataset.theme === c;
            b.classList.toggle('active', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
    };
    document.addEventListener('click', e => {
        const b = e.target.closest('.theme-toggle [data-theme]');
        if (!b) return;
        try { if (b.dataset.theme === 'auto') localStorage.removeItem(key); else localStorage.setItem(key, b.dataset.theme); } catch { /* storage unavailable */ }
        apply();
    });
    media.addEventListener('change', apply);
    apply();
})();

// Metric help: <help for="…"> renders a "?" button with a Bootstrap tooltip. Clicking it must not
// bubble to sortable headers or collapsible cards.
(function () {
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => bootstrap.Tooltip.getOrCreateInstance(el));
    document.querySelectorAll('.help-hint').forEach(el => el.addEventListener('keydown', e => e.stopPropagation()));
    document.addEventListener('click', e => {
        const hint = e.target.closest('.help-hint');
        if (!hint) return;
        e.stopPropagation();
        e.preventDefault();
        hint.focus();
    }, true);
})();

// Guided tour: elements tagged data-tour="<name>:<order>" with data-tour-title / data-tour-text form the
// steps of tour <name>. The tour named by <body data-tour> starts automatically the first time it is seen
// in this browser; "Take the tour" (data-tour-start) replays it.
(function () {
    const seenKey = name => 'is-tour-seen:' + name;
    const replayKey = 'is-tour-replay';
    const seen = name => { try { return localStorage.getItem(seenKey(name)) === '1'; } catch { return true; } };
    const markSeen = name => { try { localStorage.setItem(seenKey(name), '1'); } catch { /* storage unavailable */ } };

    const steps = name => Array.from(document.querySelectorAll(`[data-tour^="${name}:"]`))
        .filter(el => el.offsetParent !== null)
        .sort((a, b) => Number(a.dataset.tour.split(':')[1]) - Number(b.dataset.tour.split(':')[1]));

    let overlay = null;

    function start(name) {
        const list = steps(name);
        if (list.length === 0 || overlay) return;
        let index = 0;
        let previousFocus = document.activeElement;

        overlay = document.createElement('div');
        overlay.className = 'tour-overlay';
        overlay.innerHTML = `<div class="tour-spot" aria-hidden="true"></div>
            <div class="tour-card card shadow" role="dialog" aria-modal="true" aria-labelledby="tour-title" aria-describedby="tour-text">
                <div class="card-body">
                    <div class="d-flex justify-content-between align-items-start gap-3">
                        <h2 class="h6 mb-1" id="tour-title"></h2>
                        <span class="small text-muted text-nowrap" id="tour-count"></span>
                    </div>
                    <p class="small mb-3" id="tour-text"></p>
                    <div class="d-flex justify-content-between align-items-center">
                        <button type="button" class="btn btn-link btn-sm p-0" data-tour-action="skip">Skip tour</button>
                        <div class="d-flex gap-2">
                            <button type="button" class="btn btn-outline-secondary btn-sm" data-tour-action="back">Back</button>
                            <button type="button" class="btn btn-primary btn-sm" data-tour-action="next">Next</button>
                        </div>
                    </div>
                </div>
            </div>`;
        document.body.appendChild(overlay);
        const spot = overlay.querySelector('.tour-spot');
        const card = overlay.querySelector('.tour-card');
        const title = overlay.querySelector('#tour-title');
        const text = overlay.querySelector('#tour-text');
        const count = overlay.querySelector('#tour-count');
        const back = overlay.querySelector('[data-tour-action="back"]');
        const next = overlay.querySelector('[data-tour-action="next"]');

        function place() {
            const el = list[index];
            const r = el.getBoundingClientRect();
            const pad = 6;
            spot.style.top = (r.top - pad) + 'px';
            spot.style.left = (r.left - pad) + 'px';
            spot.style.width = (r.width + pad * 2) + 'px';
            spot.style.height = (r.height + pad * 2) + 'px';
            const cw = card.offsetWidth, ch = card.offsetHeight, gap = 12;
            let top = r.bottom + gap;
            if (top + ch > window.innerHeight - gap) top = Math.max(gap, r.top - ch - gap);
            let left = Math.min(Math.max(gap, r.left), window.innerWidth - cw - gap);
            card.style.top = top + 'px';
            card.style.left = left + 'px';
        }

        function show() {
            const el = list[index];
            el.scrollIntoView({ block: 'center', inline: 'nearest' });
            title.textContent = el.dataset.tourTitle || '';
            text.textContent = el.dataset.tourText || '';
            count.textContent = `${index + 1} of ${list.length}`;
            back.disabled = index === 0;
            next.textContent = index === list.length - 1 ? 'Done' : 'Next';
            place();
            next.focus();
        }

        function finish() {
            markSeen(name);
            window.removeEventListener('resize', place);
            window.removeEventListener('scroll', place, true);
            document.removeEventListener('keydown', keys);
            overlay.remove();
            overlay = null;
            if (previousFocus && previousFocus.focus) previousFocus.focus();
        }

        function keys(e) {
            if (e.key === 'Escape') { e.preventDefault(); finish(); }
            else if (e.key === 'ArrowRight') { e.preventDefault(); next.click(); }
            else if (e.key === 'ArrowLeft' && index > 0) { e.preventDefault(); back.click(); }
        }

        overlay.addEventListener('click', e => {
            const action = e.target.closest('[data-tour-action]')?.dataset.tourAction;
            if (action === 'skip') finish();
            else if (action === 'back') { if (index > 0) { index--; show(); } }
            else if (action === 'next') { if (index < list.length - 1) { index++; show(); } else finish(); }
        });
        window.addEventListener('resize', place);
        window.addEventListener('scroll', place, true);
        document.addEventListener('keydown', keys);
        show();
    }

    const pageTour = document.body.dataset.tour;
    const homeHref = document.body.dataset.tourHome || '/';
    // Steps only exist in the current document, so a tour whose steps live on another page is replayed
    // there: remember the request, navigate, and start on arrival.
    document.querySelectorAll('[data-tour-start]').forEach(el => el.addEventListener('click', e => {
        e.preventDefault();
        const name = el.dataset.tourStart || pageTour || 'welcome';
        if (name === 'welcome' && pageTour !== 'welcome') {
            try { sessionStorage.setItem(replayKey, name); } catch { /* storage unavailable */ }
            location.assign(homeHref);
            return;
        }
        start(name);
    }));
    let replay = null;
    try { replay = sessionStorage.getItem(replayKey); sessionStorage.removeItem(replayKey); } catch { /* storage unavailable */ }
    if (replay === pageTour || (pageTour && !seen(pageTour) && !window.matchMedia('(max-width: 575.98px)').matches)) {
        window.setTimeout(() => start(pageTour), 300);
    }
})();

// Side panel: <a data-panel="Title" href="/edit/…"> loads that page into the offcanvas (#side-panel) instead of
// navigating. The server sees the X-Panel header and renders the form without page chrome; inline scripts in the
// fragment are re-run so rate / hours / cost previews work. Saving posts the form from the panel: a redirect means
// success (reload to show the updated page and its toast), HTML means validation errors (re-render in the panel).
(function () {
    const panel = document.getElementById('side-panel');
    if (!panel || !window.bootstrap?.Offcanvas) return;
    const title = panel.querySelector('#side-panel-title');
    const body = panel.querySelector('#side-panel-body');
    const offcanvas = bootstrap.Offcanvas.getOrCreateInstance(panel);
    const headers = { 'X-Panel': '1' };
    let opener = null;

    function render(html) {
        body.innerHTML = html;
        // Scripts inserted through innerHTML are inert; re-create them so they execute against the panel's elements.
        body.querySelectorAll('script').forEach(old => {
            const s = document.createElement('script');
            for (const a of old.attributes) s.setAttribute(a.name, a.value);
            s.textContent = old.textContent;
            old.replaceWith(s);
        });
        if (window.jQuery?.validator?.unobtrusive) {
            body.querySelectorAll('form').forEach(f => { jQuery(f).removeData('validator').removeData('unobtrusiveValidation'); });
            jQuery.validator.unobtrusive.parse(body);
        }
        body.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => bootstrap.Tooltip.getOrCreateInstance(el));
        const first = body.querySelector('.is-invalid, .input-validation-error') || body.querySelector('input:not([type=hidden]):not([readonly]), select, textarea');
        first?.focus();
    }

    function fail(message) {
        render(`<div class="alert alert-danger">${message}</div>`);
    }

    async function load(url, label, trigger) {
        opener = trigger;
        title.textContent = label;
        body.innerHTML = '<div class="text-muted small py-3" role="status">Loading…</div>';
        offcanvas.show();
        try {
            const res = await fetch(url, { headers, credentials: 'same-origin', redirect: 'manual' });
            if (res.type === 'opaqueredirect') { location.assign(url); return; }
            if (!res.ok) { fail(`Could not load this form (HTTP ${res.status}). <a href="${url}">Open it as a page</a>.`); return; }
            render(await res.text());
        } catch {
            fail(`Could not load this form. <a href="${url}">Open it as a page</a>.`);
        }
    }

    document.addEventListener('click', e => {
        const link = e.target.closest('a[data-panel]');
        if (!link || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        load(link.href, link.dataset.panel || link.textContent.trim(), link);
    });

    panel.addEventListener('submit', async e => {
        const form = e.target;
        if (form.method.toLowerCase() !== 'post') return;
        e.preventDefault();
        if (window.jQuery?.validator && jQuery(form).data('validator') && !jQuery(form).valid()) return;
        const button = e.submitter || form.querySelector('button[type=submit]');
        button?.classList.add('is-loading');
        button?.setAttribute('aria-busy', 'true');
        if (button) button.disabled = true;
        try {
            const data = new FormData(form);
            if (e.submitter?.name) data.append(e.submitter.name, e.submitter.value);
            const res = await fetch(form.action, { method: 'POST', body: data, headers, credentials: 'same-origin', redirect: 'manual' });
            if (res.type === 'opaqueredirect') { location.reload(); return; }
            if (!res.ok) { fail(`Save failed (HTTP ${res.status}). Please try again.`); return; }
            render(await res.text());
        } catch {
            fail('Save failed. Please check your connection and try again.');
        }
    });

    panel.addEventListener('hidden.bs.offcanvas', () => {
        body.innerHTML = '';
        title.textContent = '';
        if (opener && document.contains(opener)) opener.focus();
        opener = null;
    });
})();



// Inline-form validation: when a POST from an inline (non-full-page) form fails, the server redirects back
// with the posted values and per-field errors as JSON (<script data-inline-form-errors>). Restore the values
// into the form, mark invalid fields, show the tab holding the form and focus the first problem.
(function () {
    const script = document.querySelector('script[data-inline-form-errors]');
    if (!script) return;
    let state;
    try { state = JSON.parse(script.textContent); } catch { return; }
    const form = document.getElementById(state.form);
    if (!form) return;

    for (const [name, value] of Object.entries(state.values ?? {})) {
        const field = form.elements.namedItem(name);
        const el = field instanceof RadioNodeList ? [...field].find(f => f.type !== 'hidden') : field;
        if (!el || el.type === 'hidden' || el.type === 'submit') continue;
        if (el.type === 'checkbox') el.checked = value === 'true' || value === 'on';
        else el.value = value;
        el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    }

    const formErrors = [];
    let first = null;
    for (const [name, message] of Object.entries(state.errors ?? {})) {
        const field = form.elements.namedItem(name);
        const el = field instanceof RadioNodeList ? [...field].find(f => f.type !== 'hidden') : field;
        if (!el || el.type === 'hidden') { formErrors.push(message); continue; }
        el.classList.add('is-invalid');
        el.setAttribute('aria-invalid', 'true');
        const feedback = document.createElement('div');
        feedback.className = 'invalid-feedback d-block';
        feedback.textContent = message;
        (el.closest('.input-group') ?? el).insertAdjacentElement('afterend', feedback);
        el.addEventListener('input', () => { el.classList.remove('is-invalid'); el.removeAttribute('aria-invalid'); feedback.remove(); }, { once: true });
        first ??= el;
    }
    if (formErrors.length) {
        const alert = document.createElement('div');
        alert.className = 'alert alert-danger py-2 small col-12 mb-0';
        alert.setAttribute('role', 'alert');
        alert.textContent = formErrors.join(' ');
        form.prepend(alert);
    }

    const pane = form.closest('.tab-pane');
    const tab = pane ? document.querySelector(`[data-bs-toggle="tab"][data-bs-target="#${pane.id}"]`) : null;
    if (tab && window.bootstrap?.Tab && !pane.classList.contains('active')) bootstrap.Tab.getOrCreateInstance(tab).show();
    (first ?? form).scrollIntoView({ block: 'center' });
    first?.focus();
})();

// Capacity heatmap: tap / focus a cell to show its title text in a panel under the table (touch has no hover).
(function () {
    const table = document.getElementById('capacity-heatmap');
    const panel = document.getElementById('capacity-detail');
    if (!table || !panel) return;
    const body = panel.querySelector('.heat-detail');
    let selected = null;
    function show(cell) {
        if (selected) selected.classList.remove('is-selected');
        selected = cell;
        cell.classList.add('is-selected');
        body.textContent = cell.getAttribute('title');
        panel.classList.remove('d-none');
    }
    table.addEventListener('click', e => {
        const cell = e.target.closest('.heat-cell[tabindex]');
        if (cell) show(cell);
    });
    table.addEventListener('focusin', e => {
        const cell = e.target.closest('.heat-cell[tabindex]');
        if (cell) show(cell);
    });
    table.addEventListener('keydown', e => {
        if (e.key !== 'Enter' && e.key !== ' ') return;
        const cell = e.target.closest('.heat-cell[tabindex]');
        if (cell) { e.preventDefault(); show(cell); }
    });
})();


// Loading skeletons: the Portfolio and Capacity pages aggregate every initiative, so when the user navigates
// to one (nav link, filter form, keyboard shortcut) swap <main> for a placeholder layout of the destination
// immediately instead of leaving the old page frozen until the response lands. A thin indeterminate bar at the
// top of the viewport covers every other same-origin navigation. bfcache restores are rolled back.
(function () {
    const main = document.getElementById('main');
    if (!main) return;
    const pages = {
        '/portfolio': { title: 'Portfolio', tiles: 6, tileCols: 'col-6 col-md-4 col-xl-2', chart: true, rows: 8, cols: 9 },
        '/capacity': { title: 'Capacity', tiles: 4, tileCols: 'col-6 col-md-3', rows: 7, cols: 13 }
    };
    const pageFor = url => {
        let u;
        try { u = new URL(url, location.href); } catch { return null; }
        if (u.origin !== location.origin) return null;
        const path = u.pathname.replace(/\/index\/?$/i, '').replace(/\/$/, '').toLowerCase();
        return pages[path] ?? null;
    };

    const bar = document.createElement('div');
    bar.className = 'page-progress';
    bar.setAttribute('aria-hidden', 'true');
    document.body.appendChild(bar);

    let original = null, pending = null;
    const block = cls => `<span class="skeleton ${cls}"></span>`;
    function render(spec) {
        const tiles = Array.from({ length: spec.tiles }, () =>
            `<div class="${spec.tileCols}"><div class="card h-100"><div class="card-body">${block('skeleton-text w-50')}${block('skeleton-title w-75')}${block('skeleton-text w-25')}</div></div></div>`).join('');
        const header = Array.from({ length: spec.cols }, () => `<th>${block('skeleton-text')}</th>`).join('');
        const rows = Array.from({ length: spec.rows }, () =>
            `<tr>${Array.from({ length: spec.cols }, (_, c) => `<td>${block('skeleton-text' + (c === 0 ? ' w-100' : ' w-75'))}</td>`).join('')}</tr>`).join('');
        return `
<div class="page-skeleton" role="status" aria-live="polite" aria-busy="true" data-skeleton>
  <span class="visually-hidden">Loading ${spec.title}…</span>
  <div class="d-flex justify-content-between align-items-center mb-3"><h1 class="h3 mb-0">${spec.title}</h1>${block('skeleton-button')}</div>
  <div class="row g-2 mb-3">${block('col-md-3 skeleton skeleton-input')}${block('col-md-3 skeleton skeleton-input')}${block('col-md-2 skeleton skeleton-input')}</div>
  <div class="row g-3 mb-4">${tiles}</div>
  ${spec.chart ? `<div class="card mb-4"><div class="card-body">${block('skeleton skeleton-chart')}</div></div>` : ''}
  <div class="table-responsive"><table class="table table-sm"><thead><tr>${header}</tr></thead><tbody>${rows}</tbody></table></div>
</div>`;
    }

    function start(url) {
        bar.classList.add('is-active');
        const spec = pageFor(url);
        if (!spec || original !== null || pending !== null) return;
        // Swap after the current event finishes: a form removed from the DOM during its submit event is never submitted.
        pending = setTimeout(() => {
            pending = null;
            original = main.innerHTML;
            main.innerHTML = render(spec);
            main.scrollIntoView({ block: 'start' });
        }, 0);
    }
    function reset() {
        bar.classList.remove('is-active');
        if (pending !== null) { clearTimeout(pending); pending = null; }
        if (original !== null) { main.innerHTML = original; original = null; }
    }
    window.showPageLoading = start;

    document.addEventListener('click', e => {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        const a = e.target.closest('a[href]');
        if (!a || a.target === '_blank' || a.hasAttribute('download') || a.getAttribute('href').startsWith('#')) return;
        if (a.origin !== location.origin || (a.protocol !== 'http:' && a.protocol !== 'https:')) return;
        if (/\/export(\/|$)/i.test(a.pathname)) return;
        start(a.href);
    });
    document.addEventListener('submit', e => {
        const form = e.target;
        if (e.defaultPrevented || form.target === '_blank') return;
        if (form.method.toLowerCase() === 'get' && pageFor(form.action || location.href)) start(form.action || location.href);
        else bar.classList.add('is-active');
    });
    window.addEventListener('pageshow', e => { if (e.persisted) reset(); });
    window.addEventListener('pagehide', () => { bar.classList.remove('is-active'); });
})();
