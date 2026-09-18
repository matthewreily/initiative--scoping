import { test, expect } from '@playwright/test';
import { addPhase, applySize, createInitiative, isoDate, uniqueName } from './helpers';

test.describe('Side-panel editing', () => {
  test('phase and allocation edits open in a side panel, validate in place and save without leaving Details', async ({ page }) => {
    const id = await createInitiative(page, uniqueName('E2E Panel'));
    await addPhase(page, 'Build', isoDate(0), isoDate(30));
    await applySize(page, 'M');

    const panel = page.locator('#side-panel');

    // Phase: validation error stays in the panel; a good save closes it and updates the table
    await page.locator('table').filter({ hasText: 'Build' }).getByRole('link', { name: 'Edit' }).first().click();
    await expect(panel).toBeVisible();
    await expect(panel.getByRole('heading', { name: 'Edit phase' })).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
    await panel.getByLabel('Name').fill('');
    await panel.getByRole('button', { name: 'Save' }).click();
    await expect(panel.getByText(/required/i).first()).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
    await panel.getByLabel('Name').fill('Build & test');
    await panel.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
    await expect(page.locator('td', { hasText: 'Build & test' }).first()).toBeVisible();
    await expect(panel).toBeHidden();

    // Allocation: the rate preview script runs inside the panel; quantity change persists
    const row = page.locator('#allocations-table tbody tr').first();
    await row.getByRole('link', { name: 'Edit' }).click();
    await expect(panel.getByRole('heading', { name: 'Edit allocation' })).toBeVisible();
    await expect(panel.locator('#rate-preview')).toHaveValue(/\$/);
    await panel.getByLabel('Quantity').fill('3');
    await panel.getByRole('button', { name: 'Save' }).click();
    await expect(panel).toBeHidden();
    await expect(page.locator('#allocations-table tbody tr').first()).toContainText('3');

    // Cancel closes without saving
    await row.getByRole('link', { name: 'Edit' }).click();
    await expect(panel).toBeVisible();
    await panel.getByRole('button', { name: 'Cancel' }).click();
    await expect(panel).toBeHidden();
  });

  test('the edit page still works as a full page', async ({ page }) => {
    await createInitiative(page, uniqueName('E2E Panel Page'));
    await addPhase(page, 'Solo', isoDate(0), isoDate(10));
    const href = await page.locator('a[data-panel="Edit phase"]').first().getAttribute('href');
    await page.goto(href!);
    await expect(page.getByRole('heading', { name: 'Edit phase' })).toBeVisible();
    await expect(page.locator('.breadcrumb')).toContainText('Edit phase');
  });
});
