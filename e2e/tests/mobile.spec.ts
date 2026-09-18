import { test, expect, devices } from '@playwright/test';
import { addPhase, applySize, createInitiative, isoDate, uniqueName } from './helpers';

test.use({ ...devices['Pixel 7'] });

test.describe('phone layout', () => {

  test('capacity, scenarios compare and drill-down fit a phone without page-level horizontal scroll', async ({ page }) => {
    const name = uniqueName('E2E Mobile');
    const id = await createInitiative(page, name);
    await addPhase(page, 'Build', isoDate(0), isoDate(60));
    await applySize(page, 'M');

    const noPageOverflow = async () =>
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);

    // Drill-down: allocation lines stack into labelled blocks instead of a 4-column row
    await page.goto(`/Initiatives/Explain/${id}`);
    const firstLine = page.locator('.explain-lines tbody tr').first();
    await expect(firstLine).toBeVisible();
    expect(await firstLine.locator('td').first().evaluate(td => getComputedStyle(td).display)).toBe('block');
    expect(await firstLine.locator('td').first().evaluate(td => getComputedStyle(td, '::before').content)).toContain('Allocation');
    await noPageOverflow();

    // Scenarios compare: row labels stay pinned while scenario columns scroll inside the table wrapper
    await page.goto(`/Initiatives/${id}/Scenarios`);
    const rowHeader = page.locator('.scenario-compare tbody th[scope="row"]').first();
    await expect(rowHeader).toBeVisible();
    expect(await rowHeader.evaluate(th => getComputedStyle(th).position)).toBe('sticky');
    await noPageOverflow();

    // Capacity: tapping a cell shows its detail (hover titles are unusable on touch)
    await page.goto('/Capacity');
    const cell = page.locator(`#capacity-heatmap .heat-cell[tabindex][title*="${name}"]`).first();
    await expect(cell).toBeVisible();
    await expect(page.locator('#capacity-detail')).toBeHidden();
    await cell.tap();
    const detail = page.locator('#capacity-detail');
    await expect(detail).toBeVisible();
    await expect(detail).toContainText('Demand');
    await expect(detail).toContainText(name);
    await expect(cell).toHaveClass(/is-selected/);
    expect(await page.locator('#capacity-heatmap .heat-fte').first().isVisible()).toBe(false);
    await noPageOverflow();
  });
});
