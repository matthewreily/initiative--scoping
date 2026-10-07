import { test, expect } from '@playwright/test';
import { createInitiative } from './helpers';

test.describe('Team tab user picker', () => {
  test('members are added by searching users instead of typing an object id', async ({ page }) => {
    const tag = Date.now().toString(36);
    const name = `Pat Picker ${tag}`;

    await page.goto('/Admin/Users/Create');
    await page.locator('#Email').fill(`pat.${tag}@example.com`);
    await page.locator('#DisplayName').fill(name);
    await page.getByRole('button', { name: /Add|Save|Create/ }).first().click();
    await expect(page).toHaveURL(/\/Admin\/Users/);

    const id = await createInitiative(page, `Team picker ${tag}`);
    await page.getByRole('tab', { name: /Team/ }).click();
    const pane = page.locator('#pane-team');
    await expect(pane.locator('input[placeholder*="object id"]')).toHaveCount(0);

    const search = pane.getByRole('combobox', { name: 'User' });
    await search.fill('zz-nobody');
    await expect(pane.getByRole('listbox')).toContainText('No users match');

    await search.fill(`pat.${tag}`);
    const option = pane.getByRole('option', { name: new RegExp(name) });
    await expect(option).toBeVisible();
    await option.click();
    await expect(search).toHaveValue(`${name} (pat.${tag}@example.com)`);

    await pane.locator('#role').selectOption({ label: 'Contributor' });
    await pane.getByRole('button', { name: 'Add' }).click();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
    await expect(page.locator('main')).toContainText(`${name}`);
    await page.getByRole('tab', { name: /Team/ }).click();
    await expect(pane.locator('table')).toContainText(name);

    // the added member no longer appears in the picker
    await pane.getByRole('combobox', { name: 'User' }).fill(`pat.${tag}`);
    await expect(pane.getByRole('listbox')).toContainText('No users match');
  });
});
