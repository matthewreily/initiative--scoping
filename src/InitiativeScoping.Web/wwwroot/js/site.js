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

    function numeric(value) {
        const n = value.replace(/[$,%+\s]/g, '');
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

    document.querySelectorAll('table[data-sortable]').forEach(table => {
        const head = table.tHead;
        if (!head) return;
        Array.from(head.rows[0].cells).forEach((th, index) => {
            if (th.hasAttribute('data-nosort') || th.textContent.trim() === '') return;
            th.classList.add('sortable');
            th.tabIndex = 0;
            th.setAttribute('role', 'button');
            th.title = 'Sort by ' + th.textContent.trim();
            const toggle = () => sort(table, index, th.classList.contains('sorted-asc') ? -1 : 1);
            th.addEventListener('click', e => { if (!e.target.closest('input,button,a,select')) toggle(); });
            th.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); } });
        });
    });
})();

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
