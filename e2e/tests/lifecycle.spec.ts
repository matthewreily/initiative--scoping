import { test, expect } from '@playwright/test';
import { applySize, createInitiative, uniqueName } from './helpers';

test.describe('Lifecycle', () => {
  test('admin activates a fully priced draft, capturing baseline v1 and locking scope', async ({ page }) => {
    const name = uniqueName('E2E Activate');
    await createInitiative(page, name);

    // an empty draft cannot be activated
    const activate = page.getByRole('button', { name: 'Activate', exact: true });
    await expect(activate).toBeDisabled();

    await applySize(page, 'S');
    await expect(activate).toBeEnabled();

    page.once('dialog', d => d.accept());
    await activate.click();

    await expect(page.locator('h1 .badge', { hasText: 'Active' })).toBeVisible();
    await expect(page.getByText('Baseline v1').first()).toBeVisible();
    await expect(page.getByText(/Scope is locked/)).toBeVisible();

    // History tab lists the captured baseline as current
    await page.locator('#tab-history').click();
    const history = page.locator('#baseline-history tbody tr').first();
    await expect(history).toContainText('v1');
    await expect(history).toContainText('current');
  });

  test('notes can be added to an initiative', async ({ page }) => {
    const name = uniqueName('E2E Notes');
    await createInitiative(page, name);

    await page.locator('#tab-notes').click();
    const pane = page.locator('#pane-notes');
    await pane.locator('textarea').first().fill('First note from the E2E suite.');
    await pane.getByRole('button', { name: /add note/i }).click();

    await expect(page.locator('#pane-notes')).toContainText('First note from the E2E suite.');
    await expect(page.locator('#pane-notes')).toContainText('Dev User');
  });
});
