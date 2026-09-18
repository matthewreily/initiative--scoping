import { test, expect } from '@playwright/test';
import { createInitiative, isoDate, uniqueName } from './helpers';

test.describe('inline Add-row validation', () => {
  test('invalid Add phase keeps the typed values and shows the error beside the field', async ({ page }) => {
    await createInitiative(page, uniqueName('E2E Inline'));

    const form = page.locator('#add-phase-form');
    await form.locator('input[name="Name"]').fill('Backwards');
    await form.locator('input[name="PlannedStart"]').fill(isoDate(10));
    await form.locator('input[name="PlannedEnd"]').fill(isoDate(0));
    await form.getByRole('button', { name: 'Add' }).click();

    await expect(page).toHaveURL(/\/Initiatives\/Details\/\d+/);
    const end = form.locator('input[name="PlannedEnd"]');
    await expect(end).toHaveClass(/is-invalid/);
    await expect(end).toBeFocused();
    await expect(form.locator('.invalid-feedback')).toHaveText('Planned end must be on or after planned start.');
    await expect(form.locator('input[name="Name"]')).toHaveValue('Backwards');
    await expect(form.locator('input[name="PlannedStart"]')).toHaveValue(isoDate(10));

    // Fixing the field clears the error; a valid retry adds the row.
    await end.fill(isoDate(20));
    await expect(end).not.toHaveClass(/is-invalid/);
    await form.getByRole('button', { name: 'Add' }).click();
    await expect(page.locator('#pane-plan table').filter({ hasText: 'Backwards' }).first()).toBeVisible();
    await expect(page.locator('#add-phase-form .is-invalid')).toHaveCount(0);
  });

  test('duplicate phase name is flagged on the Name field', async ({ page }) => {
    await createInitiative(page, uniqueName('E2E Inline Dup'));
    const form = page.locator('#add-phase-form');
    for (const _ of [1, 2]) {
      await form.locator('input[name="Name"]').fill('Discovery');
      await form.locator('input[name="PlannedStart"]').fill(isoDate(0));
      await form.locator('input[name="PlannedEnd"]').fill(isoDate(5));
      await form.getByRole('button', { name: 'Add' }).click();
      await expect(page).toHaveURL(/\/Initiatives\/Details\/\d+/);
    }
    await expect(form.locator('input[name="Name"]')).toHaveClass(/is-invalid/);
    await expect(form.locator('.invalid-feedback')).toContainText("A phase named 'Discovery' already exists.");
  });
});
