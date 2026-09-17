import { test, expect } from '@playwright/test';
import { addPhase, applySize, createInitiative, isoDate, uniqueName } from './helpers';

test.describe('Initiative planning', () => {
  test('create initiative, add a phase, apply size, forecast is priced and explainable', async ({ page }) => {
    const name = uniqueName('E2E Plan');
    const id = await createInitiative(page, name);

    await expect(page.locator('h1 .badge', { hasText: 'Draft' })).toBeVisible();

    await addPhase(page, 'Discovery', isoDate(0), isoDate(20));

    await applySize(page, 'M');
    const allocations = page.locator('#allocations-table tbody tr');
    await expect(allocations.first()).toBeVisible();
    expect(await allocations.count()).toBeGreaterThan(0);

    // forecast tile shows a currency total greater than zero and no unpriced warning
    await page.locator('#tab-costs').click();
    const forecastTile = page.getByText('Forecast cost').locator('..');
    await expect(forecastTile).toContainText(/\$[1-9][\d,]*/);
    await expect(page.getByText('Incomplete – unpriced lines')).toHaveCount(0);

    // "Why this number" drill-down lists every phase with its labor cost
    await page.getByRole('link', { name: 'Why this number?' }).first().click();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Explain/${id}`));
    await expect(page.getByRole('heading', { name: /Forecast total \$/ })).toBeVisible();
    await expect(page.getByRole('heading', { name: /Phase 1/ })).toBeVisible();
  });

  test('initiative appears in the list, global search and portfolio', async ({ page }) => {
    const name = uniqueName('E2E Find');
    await createInitiative(page, name);

    await page.goto('/Initiatives');
    await expect(page.getByRole('link', { name }).first()).toBeVisible();

    await page.locator('#global-search').fill(name);
    await page.locator('#global-search').press('Enter');
    await expect(page.getByRole('link', { name }).first()).toBeVisible();

    await page.goto('/Portfolio');
    await expect(page.getByRole('link', { name }).first()).toBeVisible();
  });

  test('portfolio CSV export downloads a file with the initiative', async ({ page }) => {
    const name = uniqueName('E2E Export');
    await createInitiative(page, name);

    await page.goto('/Portfolio');
    await page.getByRole('button', { name: /export/i }).click();
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('link', { name: 'CSV', exact: true }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.csv$/i);

    const stream = await download.createReadStream();
    const chunks: Buffer[] = [];
    for await (const chunk of stream) chunks.push(Buffer.from(chunk));
    expect(Buffer.concat(chunks).toString('utf8')).toContain(name);
  });
});
