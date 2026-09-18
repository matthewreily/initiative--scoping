import { test, expect, type Page } from '@playwright/test';
import { addPhase, applySize, createInitiative, isoDate, uniqueName } from './helpers';

/** Adds an active roster person through Admin → People with the given dimensions. */
async function createPerson(page: Page, name: string, resourceType: string, seniority: string): Promise<void> {
  await page.goto('/Admin/People/Create');
  await page.locator('#DisplayName').fill(name);
  await page.locator('#ResourceTypeId').selectOption({ label: resourceType });
  await page.locator('#BusinessUnitId').selectOption({ index: 1 });
  await page.locator('#SeniorityId').selectOption({ label: seniority });
  await page.locator('#Location').fill('Onshore');
  await page.locator('#ResourcingClass').selectOption('InternalFte');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/\/Admin\/People(\?|$)/);
}

test.describe('Named people on allocations', () => {
  test('a roster person matching the allocation dimensions can be assigned and shows on Details, Team and Capacity', async ({ page }) => {
    const jane = uniqueName('Jane');
    const quinn = uniqueName('Quinn');
    await createPerson(page, jane, 'Software Engineer', 'Senior');
    await createPerson(page, quinn, 'QA Analyst', 'Mid');

    const id = await createInitiative(page, uniqueName('E2E People'));
    await addPhase(page, 'Build', isoDate(0), isoDate(30));
    await applySize(page, 'M');

    // Edit the Senior Software Engineer allocation in the side panel: only Jane is offered; she fills one of the seats
    const row = page.locator('#allocations-table tbody tr').filter({ hasText: 'Software Engineer' }).first();
    await row.getByRole('link', { name: 'Edit' }).click();
    const panel = page.locator('#side-panel');
    await expect(panel.getByRole('heading', { name: 'Edit allocation' })).toBeVisible();
    const picker = panel.locator('.people-picker');
    const list = picker.locator('.people-picker-list');
    await expect(list.getByRole('checkbox', { name: jane })).toBeVisible();
    await expect(list.getByRole('checkbox', { name: quinn })).toHaveCount(0);
    await picker.getByRole('searchbox').fill('zzz-nobody');
    await expect(list).toContainText('No people match');
    await picker.getByRole('searchbox').fill(jane.split(' ')[0]);
    await list.getByRole('checkbox', { name: jane }).check();
    await expect(panel.locator('#PersonIds option:checked')).toHaveCount(1);
    await panel.getByLabel('Quantity').fill('2');
    await expect(panel.getByLabel('Quantity')).toHaveJSProperty('readOnly', false);
    await panel.getByRole('button', { name: 'Save' }).click();
    await expect(panel).toBeHidden();

    await expect(page.locator('#allocations-table .allocation-person', { hasText: jane })).toBeVisible();
    await expect(page.locator('#allocations-table')).toContainText('+ 1 unassigned');
    await page.getByRole('tab', { name: /Team/ }).click();
    await expect(page.locator('#pane-team')).toContainText(jane);

    const detailsUrl = page.url();
    await page.goto('/Capacity?view=People');
    await expect(page.locator('main')).toContainText(jane);
    await expect(page.locator('main')).toContainText('Unassigned');

    // Unassign: the chip's × in the side panel clears the selection; the × on the Details row frees the seat server-side
    await page.goto(detailsUrl);
    await page.getByRole('tab', { name: /Plan/ }).click();
    await row.getByRole('link', { name: 'Edit' }).click();
    await expect(panel.getByRole('heading', { name: 'Edit allocation' })).toBeVisible();
    const chip = panel.locator('.assigned-people').getByRole('button', { name: `Unassign ${jane}` });
    await expect(chip).toBeVisible();
    await chip.click();
    await expect(chip).toHaveCount(0);
    await expect(panel.locator('#PersonIds option:checked')).toHaveCount(0);
    await panel.getByRole('button', { name: 'Cancel' }).click();

    await page.locator('#allocations-table').getByRole('button', { name: `Unassign ${jane}` }).click();
    await expect(page.locator('#allocations-table .allocation-person', { hasText: jane })).toHaveCount(0);
    await expect(row).toContainText('—');
    await expect(row.locator('td').nth(9)).toHaveText('2');
  });
});
