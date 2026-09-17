import { test, expect } from '@playwright/test';
import { applySize, createInitiative, uniqueName } from './helpers';

// Regression checks for the layout / validation issues fixed in PRs #63, #64 and #65.
test.describe('UI regressions', () => {
  test('edit initiative: default % values pass client validation and inputs share one baseline', async ({ page }) => {
    const name = uniqueName('E2E Edit');
    const id = await createInitiative(page, name);

    await page.goto(`/Initiatives/Edit/${id}`);
    const variance = page.locator('#VarianceThresholdPct');
    const contingency = page.locator('#ContingencyPct');
    await expect(variance).toHaveAttribute('step', '0.01');
    await expect(contingency).toHaveAttribute('step', '0.01');

    // Saving with untouched defaults ("0.00" etc.) must not trip "Please enter a multiple of …"
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
    await expect(page.getByText(/Please enter a multiple of/)).toHaveCount(0);

    // Sizing / Size / Variance / Contingency / Confidence controls are bottom-aligned on one row
    await page.goto(`/Initiatives/Edit/${id}`);
    const bottoms = await Promise.all(
      ['#sizing-method', '#size-key', '#VarianceThresholdPct', '#ContingencyPct', '#EstimateConfidence'].map(async sel => {
        const box = await page.locator(sel).boundingBox();
        expect(box, sel).not.toBeNull();
        return Math.round(box!.y + box!.height);
      })
    );
    expect(Math.max(...bottoms) - Math.min(...bottoms)).toBeLessThanOrEqual(2);
  });

  test('details: allocations table scrolls sideways instead of clipping at narrow widths', async ({ page }) => {
    const name = uniqueName('E2E Scroll');
    await createInitiative(page, name);
    await applySize(page, 'M');

    await page.setViewportSize({ width: 900, height: 900 });
    const wrapper = page.locator('#allocations-table');
    await expect(wrapper).toBeVisible();

    const metrics = await wrapper.evaluate(el => {
      const table = el.querySelector('table')!;
      return {
        overflowX: getComputedStyle(el).overflowX,
        wrapperWidth: el.clientWidth,
        tableWidth: table.scrollWidth,
        rowHeights: [...table.querySelectorAll('tbody tr')].map(r => (r as HTMLElement).offsetHeight)
      };
    });
    expect(metrics.overflowX).toBe('auto');
    expect(metrics.tableWidth).toBeGreaterThan(metrics.wrapperWidth); // wide enough to need scrolling …
    // … and rows stay single-line rather than wrapping into tall cells
    const maxRow = Math.max(...metrics.rowHeights);
    expect(maxRow).toBeLessThan(60);

    // the last column (cost) is reachable by scrolling
    const cost = wrapper.locator('tbody tr').first().locator('a.explain-link');
    await cost.scrollIntoViewIfNeeded();
    await expect(cost).toBeVisible();
  });
});
