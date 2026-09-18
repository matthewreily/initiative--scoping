import { test, expect } from '@playwright/test';
import { addPhase, createInitiative, isoDate, uniqueName } from './helpers';

test.describe('Resourcing classes and labor Capex % defaults', () => {
  test('Admin → Resourcing classes lists the seeded classes with their defaults and the Add-allocation row follows the class', async ({ page }) => {
    await page.goto('/Admin/ResourcingClasses');
    const internalRow = page.locator('tbody tr', { has: page.locator('td', { hasText: /^Internal$/ }) });
    await expect(internalRow).toContainText('No');
    await expect(internalRow).toContainText('70%');
    const vendorRow = page.locator('tbody tr', { has: page.locator('td', { hasText: /^Vendor$/ }) });
    await expect(vendorRow).toContainText('Yes');
    await expect(vendorRow).toContainText('100%');

    // The legacy Work calendar page no longer carries the Capex defaults.
    await page.goto('/Admin/WorkCalendar');
    await expect(page.locator('#InternalCapexPercent')).toHaveCount(0);

    await createInitiative(page, uniqueName('E2E Capex'));
    await addPhase(page, 'Build', isoDate(0), isoDate(30));

    const capex = page.locator('#capitalization');
    await expect(capex).toHaveValue('70');

    await page.locator('#resourcingclass').selectOption({ label: 'Vendor' });
    await expect(capex).toHaveValue('100');

    // A hand-typed value is kept when the class changes again.
    await capex.fill('42');
    await page.locator('#resourcingclass').selectOption({ label: 'Internal' });
    await expect(capex).toHaveValue('42');
  });

  test('a new class can be created, is deletable while unreferenced, and appears on the People form', async ({ page }) => {
    const name = uniqueName('Partner');
    await page.goto('/Admin/ResourcingClasses/Create');
    await page.locator('#Name').fill(name);
    await page.locator('#IsVendor').check();
    await page.locator('#DefaultCapexPercent').fill('85');
    await page.locator('#SortOrder').fill('9');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(/\/Admin\/ResourcingClasses(\?|$)/);
    const row = page.locator('tbody tr', { has: page.locator('td', { hasText: name }) });
    await expect(row).toContainText('85%');
    await expect(row.getByRole('button', { name: 'Delete' })).toBeEnabled();

    await page.goto('/Admin/People/Create');
    await expect(page.locator('#ResourcingClassId option', { hasText: name })).toHaveCount(1);
  });
});
