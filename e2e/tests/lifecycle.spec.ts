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

  test('change request is raised on an active initiative, approved, and implemented by the next baseline', async ({ page }) => {
    const name = uniqueName('E2E Change');
    await createInitiative(page, name);
    await applySize(page, 'S');
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: 'Activate', exact: true }).click();
    await expect(page.locator('h1 .badge', { hasText: 'Active' })).toBeVisible();

    await page.locator('#tab-changes').click();
    const pane = page.locator('#pane-changes');
    await pane.locator('#cr-title').fill('Add QA automation');
    await pane.locator('#cr-description').fill('One more engineer for the Build phase.');
    await pane.locator('#cr-reason').fill('Manual regression is too slow.');
    await pane.locator('#cr-cost').fill('6000');
    await pane.getByRole('button', { name: 'Raise change request' }).click();

    await expect(pane).toContainText('CR-1');
    await expect(pane.locator('.badge', { hasText: 'Pending' })).toBeVisible();

    // the Approvals queue lists it
    await page.goto('/Approvals');
    const queueRow = page.locator('tr', { hasText: 'CR-1' }).filter({ hasText: name });
    await expect(queueRow).toBeVisible();
    await queueRow.getByRole('button', { name: 'Approve' }).click();

    // approval opened an approved re-baseline, so scope is unlocked
    await expect(page.locator('#pane-changes .badge', { hasText: 'Approved' })).toBeVisible();
    await expect(page.getByText(/scope is unlocked/i).first()).toBeVisible();

    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: /finalize/i }).first().click();
    await expect(page.getByText('Baseline v2').first()).toBeVisible();
    await page.locator('#tab-changes').click();
    await expect(page.locator('#pane-changes .badge', { hasText: 'Implemented' })).toBeVisible();
    await expect(page.locator('#pane-changes')).toContainText('v2');
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
