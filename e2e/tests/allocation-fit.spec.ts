import { test, expect } from '@playwright/test';
import { addPhase, createInitiative, isoDate, uniqueName } from './helpers';

test('pod allocations keep rate and cost visible without horizontal scrolling on desktop', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  const id = await createInitiative(page, uniqueName('Allocation viewport'));
  await addPhase(page, 'Implementation and verification', isoDate(0), isoDate(20));
  const pod = uniqueName('Customer integration delivery pod');
  await page.goto('/Admin/Pods/Create');
  await page.locator('#Name').fill(pod);
  await page.locator('#BusinessUnitId').selectOption({ index: 1 });
  await page.locator('input[name="PersonIds"]').nth(0).check();
  await page.locator('input[name="PersonIds"]').nth(1).check();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.goto(`/Initiatives/DevelopmentPods/${id}`);
  await page.locator('#PodId').selectOption({ label: pod });
  await page.locator('input[name="PhaseIds"]').check();
  await page.getByRole('button', { name: 'Assign pod', exact: true }).click();
  const container = page.locator('#allocations-table');
  await expect(container.locator('.allocation-person').first()).toBeVisible();
  await expect(container).toContainText(pod);

  if (process.env.ALLOCATION_BASELINE) {
    // #114 added a badge inside the existing BU cell, not a column. Removing it
    // recreates the merge parent's table cells for a controlled same-data comparison.
    console.log('WITH_PODS', await container.evaluate(el => ({ clientWidth: el.clientWidth, scrollWidth: el.scrollWidth })));
    await container.locator('.text-bg-info').evaluateAll(badges => badges.forEach(badge => badge.remove()));
    console.log('PRE_PODS_CELLS', await container.evaluate(el => ({ clientWidth: el.clientWidth, scrollWidth: el.scrollWidth })));
    await page.reload();
  }

  for (const width of [1024, 1280, 1440, 1920]) {
    await page.setViewportSize({ width, height: 900 });
    const metrics = await container.evaluate(el => {
      const bounds = el.getBoundingClientRect();
      const table = el.querySelector('table')!;
      const headers = Array.from(table.querySelectorAll('thead th'));
      const cells = Array.from(table.querySelector('tbody tr:not([data-nosort])')!.children);
      return {
        clientWidth: el.clientWidth, scrollWidth: el.scrollWidth,
        tableWidth: table.getBoundingClientRect().width,
        moneyVisible: ['Rate', 'Cost'].every(label => {
          const index = headers.findIndex(h => h.textContent?.trim() === label);
          const cell = cells[index] as HTMLElement;
          const rect = cell.getBoundingClientRect();
          return rect.width > 0 && rect.left >= bounds.left && rect.right <= bounds.right && getComputedStyle(cell).visibility === 'visible';
        })
      };
    });
    console.log(`ALLOCATION_FIT ${width}`, JSON.stringify(metrics));
    if (width === 1024) await container.screenshot({ path: testInfo.outputPath('allocations-1024.png') });
    expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth);
    expect(metrics.moneyVisible).toBe(true);
  }
});
