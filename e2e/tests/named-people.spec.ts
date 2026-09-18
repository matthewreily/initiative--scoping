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
    const person = panel.locator('#PersonIds');
    await expect(person.locator('option', { hasText: jane })).toHaveCount(1);
    await expect(person.locator('option', { hasText: jane })).toBeEnabled();
    await expect(person.locator('option', { hasText: quinn })).toBeDisabled();
    await person.selectOption({ label: jane });
    await panel.getByLabel('Quantity').fill('2');
    await expect(panel.getByLabel('Quantity')).toHaveJSProperty('readOnly', false);
    await panel.getByRole('button', { name: 'Save' }).click();
    await expect(panel).toBeHidden();

    await expect(page.locator('#allocations-table .allocation-person', { hasText: jane })).toBeVisible();
    await expect(page.locator('#allocations-table')).toContainText('+ 1 unassigned');
    await page.getByRole('tab', { name: /Team/ }).click();
    await expect(page.locator('#pane-team')).toContainText(jane);

    await page.goto('/Capacity?view=People');
    await expect(page.locator('main')).toContainText(jane);
    await expect(page.locator('main')).toContainText('Unassigned');
  });
});
