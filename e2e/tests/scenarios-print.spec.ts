import { test, expect } from '@playwright/test';
import { addPhase, applySize, createInitiative, isoDate, uniqueName } from './helpers';

test.describe('Scenario comparison print view', () => {
  test('Print / PDF opens a chrome-free comparison with the phase schedule', async ({ page }) => {
    const name = uniqueName('E2E Scenario print');
    const id = await createInitiative(page, name);
    await addPhase(page, 'Build', isoDate(0), isoDate(60));
    await applySize(page, 'M');

    await page.goto(`/Initiatives/${id}/Scenarios`);
    await page.locator('input[name="Name"]').fill(`${name} — Lean`);
    await page.getByRole('button', { name: 'New scenario from live plan' }).click();
    await expect(page).toHaveURL(/\/Initiatives\/Details\/\d+$/);
    await expect(page.locator('main h1')).toContainText(`${name} — Lean`);

    await page.goto(`/Initiatives/${id}/Scenarios`);
    await page.locator('#scenarios-print-link').click();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/${id}/Scenarios/Print$`));

    await expect(page.locator('main h1')).toContainText(name);
    await expect(page.getByText('Scenario comparison', { exact: true })).toBeVisible();
    await expect(page.locator('#print-button')).toBeVisible();
    await expect(page.locator('.navbar')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Promote' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Edit' })).toHaveCount(0);

    const compare = page.locator('#scenario-print-compare');
    await expect(compare).toContainText('Live plan');
    await expect(compare).toContainText(`${name} — Lean`);
    await expect(compare).toContainText('Forecast cost');

    const phases = page.locator('#scenario-print-phases');
    await expect(phases.locator('table')).toHaveCount(2);
    await expect(phases).toContainText('Build');

    await page.emulateMedia({ media: 'print' });
    await expect(page.locator('.print-toolbar')).toBeHidden();
  });
});
