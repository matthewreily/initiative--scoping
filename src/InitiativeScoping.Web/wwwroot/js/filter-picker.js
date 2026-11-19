document.querySelectorAll('[data-filter-picker]').forEach(picker => {
    const query = picker.querySelector('[data-filter-query]');
    const rows = Array.from(picker.querySelectorAll('[data-filter-name]'));
    const status = picker.querySelector('[data-filter-status]');
    function filter() {
        const text = query.value.trim().toLocaleLowerCase();
        let shown = 0;
        rows.forEach(row => {
            const matches = row.dataset.filterName.toLocaleLowerCase().includes(text);
            row.hidden = !matches;
            row.classList.toggle('d-none', !matches);
            if (matches) shown++;
        });
        status.textContent = `${shown} of ${rows.length} shown`;
    }
    query.addEventListener('input', filter);
    filter();
});
